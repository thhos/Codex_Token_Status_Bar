import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { createInterface } from 'node:readline';
import { AppServer } from './rpc.mjs';
import { UsageIndex } from './usage.mjs';
import { CodexMetadata } from './cdp.mjs';
import { comparisonGroups, forecastPresentation, resetCreditCount, datePeriod } from './comparison.mjs';
import { selectQuota, forecastQuota, bucketCredits, chartWindow, DAY, HOUR } from './core.mjs';

const args = process.argv.slice(2);
const arg = (name, fallback) => args.includes(name) ? args[args.indexOf(name) + 1] : fallback;
const home = process.env.CODEX_HOME || path.join(os.homedir(), '.codex');
const dataDir = arg('--data-dir', path.join(process.env.LOCALAPPDATA || os.homedir(), 'CodexPetCredits'));
fs.mkdirSync(dataDir, { recursive: true });
const read = (name, fallback) => { try { return JSON.parse(fs.readFileSync(path.join(dataDir, name), 'utf8')); } catch { return fallback; } };
function save(name, value) { const destination = path.join(dataDir, name), temp = destination + '.tmp'; fs.writeFileSync(temp, JSON.stringify(value)); fs.renameSync(temp, destination); }
const card = JSON.parse(fs.readFileSync(new URL('./rates.json', import.meta.url), 'utf8'));
let settings = { density: 0, opacity: 90, range: '24h', scope: 'current', theme: 'dark', accent: 'mint', smoothing: 'smooth', ...read('settings.json', {}) };
const ranges = { '1h': HOUR, '6h': 6 * HOUR, '24h': DAY, '7d': 7 * DAY, '30d': 30 * DAY };
const index = new UsageIndex(home, card, read('usage-cache.json', {}));
let samples = read('quota-history.json', []), rates = null, quota = null, lastQuota = 0, error = '', initialized = false;
const cdp = new CodexMetadata(Number(arg('--port', '9337')));
let rpc, running = true, quotaBusy = false, scanBusy = false, cdpBusy = false, officialThread = null;
let parents = new Map();
let predictionCache = { key: '', value: null };

function findCodex() {
  if (arg('--codex', null)) return arg('--codex', null);
  const bin = path.join(process.env.LOCALAPPDATA || '', 'OpenAI', 'Codex', 'bin');
  const candidates = fs.existsSync(bin) ? fs.readdirSync(bin).map(p => path.join(bin, p, 'codex.exe')).filter(p => fs.existsSync(p)) : [];
  candidates.sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  if (!candidates.length) throw new Error('未找到 Codex 数据服务');
  return candidates[0];
}

async function refreshQuota() {
  if (quotaBusy || !running) return;
  quotaBusy = true;
  try {
    if (!rpc?.child) { rpc = new AppServer(findCodex()); await rpc.start(); }
    rates = await rpc.request('account/rateLimits/read');
    quota = selectQuota(rates); lastQuota = Date.now(); error = '';
    if (quota) samples.push({ time: lastQuota, used: quota.usedPercent, reset: quota.resetsAt });
    samples = samples.filter(s => s.time > Date.now() - 35 * DAY);
    save('quota-history.json', samples);
    // The documented protocol exposes thread credit estimates when available.
    const task = currentTask()?.id;
    if (task) {
      try { const result = await rpc.request('account/usage/read', { threadId: task });
        if (Number.isFinite(result?.threadUsage?.estimatedUsageCreditsMicros)) officialThread = { task, credits: result.threadUsage.estimatedUsageCreditsMicros / 1e6, time: Date.now() };
      } catch { /* Local accounting remains available when the service has not returned this estimate. */ }
    }
  } catch (e) { error = e.message; }
  finally { quotaBusy = false; emit(); }
}

function rootTask(id) {
  const visited = new Set(); let result = id;
  while (!visited.has(result)) { visited.add(result); const parent = parents.get(result); if (!parent) break; result = parent; }
  return result;
}

function currentTask() {
  const viewed = cdp.current?.task;
  const id = viewed || (index.events.at(-1) && rootTask(index.events.at(-1).thread));
  if (!id) return null;
  const metadata = index.threads.get(id);
  return { id, title: metadata?.title || '任务 ' + id.slice(0, 8), project: metadata?.cwd || index.events.find(e => e.thread === id)?.project || '', followed: Boolean(viewed) };
}

function buildView() {
  const now = Date.now(), task = currentTask(), timeline = chartWindow(now, ranges[settings.range] || DAY), start = timeline.start;
  const all = index.events.filter(e => e.time >= start && e.time <= now);
  const rootCache = new Map();
  const root = id => { if (!rootCache.has(id)) rootCache.set(id, rootTask(id)); return rootCache.get(id); };
  let selected = all, subtitle = '', groups = [], comparisonChoices = [];
  if (settings.scope === 'current') {
    selected = task ? all.filter(e => root(e.thread) === root(task.id)) : [];
    subtitle = (task?.followed ? '正在查看 · ' : '最近活跃 · ') + (task?.title || '暂无任务');
    groups = [{ id: task?.id || 'current', name: task?.title || '当前任务', events: selected }];
  } else if (settings.scope === 'projects' || settings.scope === 'tasks') {
    const map = new Map();
    for (const e of index.events) { const key = settings.scope === 'projects' ? e.project || '未知项目' : root(e.thread); if (!map.has(key)) map.set(key, []); map.get(key).push(e); }
    const selectionKey = settings.scope === 'projects' ? 'selectedProjects' : 'selectedTasks';
    // Retain saved choices even after their local records age out of the visible range.
    for (const id of settings[selectionKey] || []) if (!map.has(id)) map.set(id, []);
    const catalog = [...map].map(([key, history]) => { const events = history.filter(e => e.time >= start && e.time <= now);
      return { id: key, name: settings.scope === 'projects' ? path.basename(key) || key : index.threads.get(key)?.title || '任务 ' + key.slice(0, 8), events, total: sum(events), latest: history.at(-1)?.time || 0 }; });
    const comparison = comparisonGroups(catalog, settings[selectionKey]); groups = comparison.groups; comparisonChoices = comparison.choices;
    selected = groups.flatMap(g => g.events);
  } else groups = [{ id: 'account', name: '本机汇总', events: selected }];
  const unknown = selected.filter(e => e.credits == null).length;
  const predictionKey = Math.floor(now / 60_000) + ':' + lastQuota + ':' + Math.floor((index.events.at(-1)?.time || 0) / 60_000);
  if (predictionCache.key !== predictionKey) predictionCache = { key: predictionKey, value: forecastQuota(samples, index.events.map(e => e.time), now, quota) };
  const prediction = predictionCache.value;
  const series = groups.map(g => ({ name: g.name, total: g.events.some(e => e.credits != null) ? sum(g.events) : null, points: g.events.length ? bucketCredits(g.events, start, timeline.end).map(p => p.value) : Array(48).fill(null) }));
  const otherQuotas = Object.entries(rates?.rateLimitsByLimitId || {}).filter(([id]) => id !== 'codex').map(([id, item]) => {
    const w = [item.primary, item.secondary].filter(Boolean), name = item.limitName || id;
    return { name, value: w.map(x => (x.windowDurationMins === 10080 ? '周 ' : Math.round(x.windowDurationMins / 60) + 'h ') + Math.max(0, 100 - x.usedPercent) + '%').join(' · ') };
  });
  const other = otherQuotas.map(row => row.name + '  ' + row.value);
  const recentGroups = new Map();
  for (const e of selected) { const name = e.model || '未知模型', group = recentGroups.get(name) || { credits: 0, known: 0, missing: 0 };
    if (e.credits == null) group.missing++; else { group.credits += e.credits; group.known++; } recentGroups.set(name, group); }
  const detailRows = [...recentGroups].sort((a, b) => b[1].credits - a[1].credits).slice(0, 5).map(([name, value]) => ({ name, value: value.known ? value.credits.toFixed(2) + ' cr' + (value.missing ? '（部分）' : '') : '费率未知' }));
  const recentDay = index.events.filter(e => e.time >= now - DAY && e.credits != null);
  const recentHour = recentDay.filter(e => e.time >= now - HOUR);
  const resetLabel = quota ? new Date(quota.resetsAt * 1000).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false }) : '等待数据';
  return { type: 'view', settings, remaining: quota?.remaining ?? null, quotaLabel: (quota?.windowDurationMins === 10080 ? 'CODEX · 周剩余' : 'CODEX · 剩余额度') + (lastQuota && now - lastQuota > 5 * 60_000 ? ' · 待更新' : ''),
    forecast: prediction.label, forecastDisplay: forecastPresentation(prediction), forecastDetail: prediction.detail, warning: prediction.warning, subtitle,
    comparisonChoices, observedAt: now,
    resetDate: quota ? new Date(quota.resetsAt * 1000).toLocaleDateString('en-US', { month: 'numeric', day: 'numeric' }) : '—',
    resetTime: quota ? new Date(quota.resetsAt * 1000).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit', hour12: false }) : '',
    resetCreditCount: resetCreditCount(rates),
    resetDisplay: datePeriod(quota ? quota.resetsAt * 1000 : null),
    taskTitle: task?.title || '暂无任务', projectTitle: task?.project ? path.basename(task.project) : '',
    samplingStatus: error ? '更新暂停' : !initialized ? '整理记录中' : '已更新',
    followLabel: task?.followed ? '正在查看' : '最近活跃',
    chartKey: settings.scope + ':' + settings.range + ':' + groups.map(g => g.id).join('|'),
    totalLabel: (settings.scope === 'current' ? '当前任务' : settings.scope === 'account' ? '本机全部' : '图中 ' + groups.length + ' 个' + (settings.scope === 'projects' ? '项目' : '任务')) + ' · 所选时段合计',
    officialTaskCredits: officialThread && task && officialThread.task === task.id ? officialThread.credits : null,
    recentCredits: { hour: recentHour.length ? sum(recentHour) : null, day: recentDay.length ? sum(recentDay) : null },
    total: selected.some(e => e.credits != null) ? sum(selected) : null, series, range: settings.range, details: detailRows, other, otherQuotas, resetLabel,
    updated: lastQuota ? Math.floor((now - lastQuota) / 1000) : null,
    status: error || (!initialized ? '正在索引本机历史…' : !cdp.connected ? '任务跟随待连接 · 请从启动入口打开 Codex' : task?.followed ? '已跟随当前任务' : '当前页面未识别 · 使用最近活跃任务'),
    coverage: (unknown ? unknown + ' 次调用缺少费率，合计为已知部分。' : '') + '本机记录；缺少速度档位时按标准费率估算。未含其他设备及工具额外费用。',
    rateVersion: card.version, pet: cdp.pet?.mascot || null, petPageVisible: cdp.pet?.visible || false, cdpConnected: cdp.connected,
    windowStart: start, windowEnd: timeline.end };
}
function sum(events) { return events.reduce((s, e) => s + (e.credits || 0), 0); }
function emit() { if (running) process.stdout.write(JSON.stringify(buildView()) + '\n'); }

async function scan() {
  if (scanBusy || !running) return;
  scanBusy = true;
  try { await index.scan(); parents = new Map(index.events.filter(e => e.parent).map(e => [e.thread, e.parent])); initialized = true; save('usage-cache.json', index.snapshot()); }
  catch { error = '本机历史索引暂不可用'; }
  finally { scanBusy = false; emit(); }
}
async function pollCdp() { if (cdpBusy || !running) return; cdpBusy = true; const before = cdp.current?.task; try { await cdp.poll(); } finally { cdpBusy = false; if (cdp.current?.task !== before) emit(); } }
let petBusy = false;
async function pollPet() {
  if (petBusy || !running) return;
  petBusy = true;
  try { await cdp.pollPet(); if (running) process.stdout.write(JSON.stringify({ type: 'pet', pet: cdp.pet?.mascot || null, visible: cdp.pet?.visible || false, observedAt: cdp.petObservedAt }) + '\n'); }
  finally { petBusy = false; }
}
function updateSettings(message) {
  const dataKeys = ['range', 'scope', 'selectedTasks', 'selectedProjects'];
  const before = dataKeys.map(key => JSON.stringify(settings[key]));
  if (Number.isFinite(message.opacity)) settings.opacity = Math.max(40, Math.min(100, message.opacity));
  if ([0, 1, 2].includes(message.density)) settings.density = message.density;
  if (ranges[message.range]) settings.range = message.range;
  if (['current', 'account', 'projects', 'tasks'].includes(message.scope)) settings.scope = message.scope;
  if (['light', 'dark'].includes(message.theme)) settings.theme = message.theme;
  if (['mint', 'blue', 'violet', 'amber', 'rose'].includes(message.accent)) settings.accent = message.accent;
  if (['smooth', 'raw'].includes(message.smoothing)) settings.smoothing = message.smoothing;
  for (const key of ['selectedTasks', 'selectedProjects']) {
    if (message[key] === null) delete settings[key];
    else if (Array.isArray(message[key])) settings[key] = [...new Set(message[key].filter(id => typeof id === 'string' && id.length > 0 && id.length <= 4096))].slice(0, 5);
  }
  save('settings.json', settings);
  // Appearance changes only need an acknowledgement; do not rebuild usage groups or chart bins.
  if (dataKeys.some((key, i) => JSON.stringify(settings[key]) !== before[i])) emit();
  else if (running) process.stdout.write(JSON.stringify({ type: 'settings', settings }) + '\n');
}
function shutdown() { if (!running) return; running = false; rpc?.close(); cdp.close(); for (const timer of timers) clearInterval(timer); setTimeout(() => process.exit(0), 1200).unref(); }
const input = createInterface({ input: process.stdin });
input.on('line', line => { try { const m = JSON.parse(line); if (m.type === 'settings') updateSettings(m); else if (m.type === 'shutdown') shutdown(); else if (m.type === 'refresh') refreshQuota(); } catch {} });
input.on('close', shutdown); process.on('SIGTERM', shutdown); process.on('SIGINT', shutdown);
process.stdout.on('error', shutdown);
process.on('exit', () => rpc?.child?.kill());
const timers = [setInterval(refreshQuota, 30_000), setInterval(scan, 10_000), setInterval(pollCdp, 1000), setInterval(pollPet, 40), setInterval(emit, 5000)];
emit(); void refreshQuota(); void scan(); void pollCdp();

// Read-only smoke mode exercises real adapters and exits without launching any UI.
if (args.includes('--smoke')) setTimeout(() => { process.stdout.write(JSON.stringify({ type: 'smoke', quota: Boolean(quota), remaining: quota?.remaining, events: index.events.length, priced: index.events.filter(e => e.credits != null).length, cdp: cdp.connected }) + '\n'); shutdown(); }, 25_000);
