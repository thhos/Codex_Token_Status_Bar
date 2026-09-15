import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { UsageIndex } from '../src/backend/usage.mjs';
const card = JSON.parse(fs.readFileSync(new URL('../src/backend/rates.json', import.meta.url)));
const line = (type, payload, timestamp = '2026-09-15T00:00:00Z') => JSON.stringify({ type, payload, timestamp }) + '\n';

test('incremental reader handles partial writes, UTF-8, archive duplicate and cold restart', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'credits-io-'));
  try {
    const file = path.join(dir, 'rollout.jsonl'), usage = { input_tokens: 1000, cached_input_tokens: 100, output_tokens: 200 };
    const metadata = line('session_meta', { id: 'task-io', cwd: '测试项目' }) + line('turn_context', { model: 'gpt-5.6-sol' });
    const count = line('event_msg', { type: 'token_count', info: { total_token_usage: usage } });
    fs.writeFileSync(file, metadata + count.slice(0, -5));
    const index = new UsageIndex(dir, card); await index.readFile(file); assert.equal(index.events.length, 0);
    fs.appendFileSync(file, count.slice(-5)); await index.readFile(file); assert.equal(index.events.length, 1); assert.equal(index.events[0].project, '测试项目');
    await index.readFile(file); assert.equal(index.events.length, 1);
    const archived = path.join(dir, 'archived.jsonl'); fs.copyFileSync(file, archived); await index.readFile(archived); assert.equal(index.events.length, 1);
    const restarted = new UsageIndex(dir, card, index.snapshot()); await restarted.readFile(file); assert.equal(restarted.events.length, 1);
    fs.appendFileSync(file, line('event_msg', { type: 'token_count', info: { total_token_usage: { input_tokens: 1500, cached_input_tokens: 150, output_tokens: 300 } } }, '2026-09-15T00:01:00Z'));
    await restarted.readFile(file); assert.equal(restarted.events.length, 2);
    assert.ok(Math.abs(restarted.events[1].credits - .0955) < 1e-10);
    const revised = new UsageIndex(dir, { ...card, version: 'next' }, index.snapshot()); assert.equal(revised.events.length, 0);
  } finally {
    const resolved = path.resolve(dir), tempRoot = path.resolve(os.tmpdir());
    if (path.dirname(resolved) !== tempRoot || !path.basename(resolved).startsWith('credits-io-')) throw new Error('Unsafe test cleanup path');
    fs.rmSync(resolved, { recursive: true, force: true });
  }
});

test('unknown speeds and unpriced cache-write usage are not silently priced', () => {
  const index = new UsageIndex('', card), state = {};
  index.consumeLine(line('session_meta', { id: 'cache' }), state);
  index.consumeLine(line('turn_context', { model: 'gpt-6-astra', service_tier: 'new-tier' }), state);
  index.consumeLine(line('event_msg', { type: 'token_count', info: { total_token_usage: { input_tokens: 100, cached_input_tokens: 0, output_tokens: 20 } } }), state);
  assert.equal(index.events[0].credits, null);
});
