import fs from 'node:fs';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { usageDelta, estimateCredits, DAY } from './core.mjs';

export class UsageIndex {
  constructor(home, card, cache = {}) {
    this.home = home; this.card = card;
    // A rate-card revision requires recalculation; cursor reuse would preserve obsolete costs.
    const compatible = cache.rateVersion === card.version;
    this.files = compatible ? cache.files || {} : {}; this.events = compatible ? cache.events || [] : [];
    this.threads = new Map(); this.seen = new Set(this.events.map(e => e.id)); this.scanning = false;
  }
  loadThreadMetadata() {
    let db;
    try {
      db = new DatabaseSync(path.join(this.home, 'state_5.sqlite'), { readOnly: true });
      const columns = new Set(db.prepare('PRAGMA table_info(threads)').all().map(c => c.name));
      const wanted = ['id', 'title', 'cwd', 'rollout_path', 'model', 'created_at'].filter(c => columns.has(c));
      if (wanted.includes('id')) for (const row of db.prepare('SELECT ' + wanted.join(',') + ' FROM threads').all()) {
        // The database title may be the initial user message, not the desktop's renamed title.
        this.threads.set(row.id, { ...row, title: '任务 ' + row.id.slice(0, 8) });
      }
    } catch { /* A locked or future database version falls back to session metadata. */ }
    finally { db?.close(); }
    try {
      const names = new Map();
      for (const line of fs.readFileSync(path.join(this.home, 'session_index.jsonl'), 'utf8').split('\n')) {
        let item; try { item = JSON.parse(line); } catch { continue; }
        if (typeof item.id !== 'string' || typeof item.thread_name !== 'string' || !item.thread_name.trim()) continue;
        const previous = names.get(item.id);
        if (!previous || (Date.parse(item.updated_at) || 0) >= (Date.parse(previous.updated_at) || 0)) names.set(item.id, item);
      }
      for (const [id, item] of names) this.threads.set(id, { ...this.threads.get(id), id, title: item.thread_name.trim() });
    } catch { /* No display-name index: use a neutral task ID, never a prompt as its name. */ }
  }
  async scan(now = Date.now()) {
    if (this.scanning) return;
    this.scanning = true;
    try {
      this.loadThreadMetadata();
      const paths = new Set();
      for (const root of ['sessions', 'archived_sessions']) {
        const dir = path.join(this.home, root);
        if (!fs.existsSync(dir)) continue;
        // Filenames carry dates, so old histories do not have to be opened on every tick.
        for (const entry of fs.readdirSync(dir, { recursive: true, withFileTypes: true })) {
          if (!entry.isFile() || !entry.name.endsWith('.jsonl')) continue;
          const date = entry.name.match(/rollout-(\d{4}-\d{2}-\d{2})/);
          if (date && Date.parse(date[1]) < now - 35 * DAY) continue;
          paths.add(path.join(entry.parentPath, entry.name));
        }
      }
      // Older currently viewed tasks may still receive new usage.
      for (const item of this.threads.values()) if (item.rollout_path && fs.existsSync(item.rollout_path)) {
        if (fs.statSync(item.rollout_path).mtimeMs > now - 2 * DAY) paths.add(item.rollout_path);
      }
      let count = 0;
      for (const file of paths) {
        await this.readFile(file);
        if (++count % 10 === 0) await new Promise(resolve => setImmediate(resolve));
      }
      this.events = this.events.filter(e => e.time >= now - 35 * DAY);
      this.events.sort((a, b) => a.time - b.time);
      this.seen = new Set(this.events.map(e => e.id));
    } finally { this.scanning = false; }
  }
  async readFile(file) {
    let handle;
    try {
      const stat = fs.statSync(file);
      let state = this.files[file];
      if (!state || stat.size < state.offset) state = this.files[file] = { offset: 0, model: null, tier: 'default', previous: null, thread: null, inherited: false };
      if (stat.size === state.offset) return;
      handle = await fs.promises.open(file, 'r');
      let offset = state.offset;
      // Only commit complete lines; partial UTF-8 and interrupted writes are reread next time.
      while (offset < stat.size) {
        const buffer = Buffer.alloc(Math.min(2 * 1024 * 1024, stat.size - offset));
        const { bytesRead } = await handle.read(buffer, 0, buffer.length, offset);
        let newline = buffer.lastIndexOf(10, bytesRead - 1);
        if (newline < 0) {
          // Very large non-metric records are skipped by finding their newline without retaining their body.
          if (bytesRead < buffer.length || offset + bytesRead >= stat.size) break;
          let probe = offset + bytesRead, found = false;
          while (probe < stat.size) { const part = Buffer.alloc(65536); const result = await handle.read(part, 0, part.length, probe);
            const end = part.indexOf(10, 0); if (end >= 0 && end < result.bytesRead) { offset = probe + end + 1; found = true; break; } probe += result.bytesRead; }
          if (!found) break;
          state.offset = offset; continue;
        }
        for (const line of buffer.subarray(0, newline).toString('utf8').split('\n')) this.consumeLine(line, state);
        offset += newline + 1; state.offset = offset;
      }
    } catch { /* Sessions may be moved to the archive while a scan is in progress. */ }
    finally { await handle?.close(); }
  }
  consumeLine(line, state) {
    // Never persist prompts, messages, tool output or credentials.
    if (!line.includes('token_count') && !line.includes('turn_context') && !line.includes('session_meta')) return;
    let record;
    try { record = JSON.parse(line); } catch { return; }
    const p = record.payload;
    if (!p) return;
    if (record.type === 'session_meta') {
      state.thread = p.id; state.cwd = p.cwd || ''; state.inherited = Boolean(p.forked_from_id);
      state.parent = p.source?.subagent?.thread_spawn?.parent_thread_id || null;
      return;
    }
    if (record.type === 'turn_context') { state.model = p.model || state.model; state.tierKnown = Boolean(p.service_tier || p.speed); state.tier = p.service_tier || p.speed || 'default'; return; }
    if (record.type !== 'event_msg' || p.type !== 'token_count' || !p.info?.total_token_usage || !state.thread) return;
    const total = p.info.total_token_usage;
    const delta = state.inherited && !state.previous ? null : usageDelta(total, state.previous);
    if (delta) delta.cache_write_input_tokens = Math.max(0, (total.cache_write_input_tokens || 0) - (state.previous?.cache_write_input_tokens || 0));
    state.previous = total;
    if (!delta || !Object.values(delta).some(n => n > 0)) return;
    const time = Date.parse(record.timestamp); if (!Number.isFinite(time)) return;
    const id = state.thread + ':' + time + ':' + total.input_tokens + ':' + total.output_tokens;
    if (this.seen.has(id)) return;
    const metadata = this.threads.get(state.thread);
    const model = state.model || metadata?.model || null;
    const credits = estimateCredits(delta, model, state.tier, this.card);
    this.events.push({ id, time, thread: state.thread, parent: state.parent, project: metadata?.cwd || state.cwd || '', model, credits, speedKnown: Boolean(state.tierKnown) });
    this.seen.add(id);
  }
  snapshot() { return { rateVersion: this.card.version, files: this.files, events: this.events }; }
}
