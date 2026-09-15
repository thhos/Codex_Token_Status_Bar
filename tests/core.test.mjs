import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { usageDelta, estimateCredits, selectQuota, forecastQuota, bucketCredits, chartWindow, monotoneSegments, selectFocusedTask, HOUR, DAY } from '../src/backend/core.mjs';
import { UsageIndex } from '../src/backend/usage.mjs';
const card = JSON.parse(fs.readFileSync(new URL('../src/backend/rates.json', import.meta.url)));

test('cached input and reasoning output are not counted twice; fast is model-specific', () => {
  const usage = { input_tokens: 1_000_000, cached_input_tokens: 800_000, output_tokens: 10_000, reasoning_output_tokens: 9000 };
  assert.equal(estimateCredits(usage, 'gpt-6-astra', 'default', card), 82.5);
  assert.equal(estimateCredits(usage, 'gpt-6-astra', 'fast', card), 206.25);
  assert.equal(estimateCredits(usage, 'unknown-model', 'default', card), null);
  assert.equal(estimateCredits(usage, 'gpt-5.4-mini', 'fast', card), null);
  assert.equal(estimateCredits({ ...usage, cached_input_tokens: 2e6 }, 'gpt-5.5', 'default', card), null);
});

test('quota uses codex bucket and duration rather than primary/secondary assumptions', () => {
  const q = selectQuota({ rateLimitsByLimitId: { codex: { primary: { usedPercent: 6, windowDurationMins: 10080, resetsAt: 42 } }, spark: { primary: { usedPercent: 95, windowDurationMins: 300 } } } });
  assert.equal(q.remaining, 94); assert.equal(q.windowDurationMins, 10080);
  assert.equal(selectQuota({}), null);
});

test('cumulative counters emit deltas and reject resets', () => {
  const a = { input_tokens: 20, cached_input_tokens: 5, output_tokens: 2 };
  assert.deepEqual(usageDelta({ input_tokens: 25, cached_input_tokens: 6, output_tokens: 4 }, a), { input_tokens: 5, cached_input_tokens: 1, output_tokens: 2 });
  assert.equal(usageDelta({ input_tokens: 2, cached_input_tokens: 0, output_tokens: 0 }, a), null);
});

test('session accounting handles duplicate snapshots, model switches, reset and fork baselines', () => {
  const index = new UsageIndex('', card), state = {};
  const feed = (type, payload, timestamp = '2026-09-15T10:00:00Z') => index.consumeLine(JSON.stringify({ type, payload, timestamp }), state);
  feed('session_meta', { id: 'task', cwd: 'project' });
  feed('turn_context', { model: 'gpt-5.6-luna' });
  const usage = { input_tokens: 1000, cached_input_tokens: 0, output_tokens: 100 };
  feed('event_msg', { type: 'token_count', info: { total_token_usage: usage } });
  feed('event_msg', { type: 'token_count', info: { total_token_usage: usage } });
  assert.equal(index.events.length, 1); assert.equal(index.events[0].credits, .008);
  feed('turn_context', { model: 'gpt-6-astra', service_tier: 'fast' });
  feed('event_msg', { type: 'token_count', info: { total_token_usage: { input_tokens: 2000, cached_input_tokens: 0, output_tokens: 200 } } }, '2026-09-15T10:01:00Z');
  assert.equal(index.events[1].credits, .9375);
  const forkState = {};
  index.consumeLine(JSON.stringify({ type: 'session_meta', payload: { id: 'fork', forked_from_id: 'task' } }), forkState);
  index.consumeLine(JSON.stringify({ type: 'event_msg', timestamp: '2026-09-15T10:02:00Z', payload: { type: 'token_count', info: { total_token_usage: usage } } }), forkState);
  assert.equal(index.events.length, 2);
});

test('forecast learns one depletion date and never bridges downtime/reset/stale data', () => {
  const now = Date.parse('2026-09-15T12:00:00Z'), reset = (now + 5 * DAY) / 1000;
  const samples = Array.from({ length: 121 }, (_, i) => ({ time: now - (120 - i) * 60_000, used: i / 10, reset }));
  const activity = Array.from({ length: 577 }, (_, i) => now - (576 - i) * HOUR / 12);
  const quota = { remaining: 5, resetsAt: reset };
  assert.equal(forecastQuota(samples, activity, now, quota).warning, true);
  assert.ok(forecastQuota(samples, activity, now, quota).exhaustion > now);
  assert.equal(forecastQuota(samples.slice(0, 1), activity, now, quota).label, '数据待更新');
  assert.equal(forecastQuota([{ time: now - DAY, used: 0, reset }, { time: now, used: 99, reset }], activity, now, quota).label, '正在学习');
  assert.equal(forecastQuota(samples, activity, now, { ...quota, resetsAt: reset + DAY }).label, '正在学习');
});

test('chart bounds, missing rates, and zero usage remain distinct', () => {
  const buckets = bucketCredits([{ time: 1, credits: 2 }, { time: 2, credits: null }, { time: 10, credits: 3 }], 0, 10, 5);
  assert.equal(buckets[0].value, 2); assert.equal(buckets[1].value, null); assert.equal(buckets[2].value, 0); assert.equal(buckets[4].value, 3);
});

test('smooth curves do not create extrema between observations', () => {
  const points = [{ x: 0, y: 0 }, { x: 1, y: 10 }, { x: 2, y: 10 }, { x: 3, y: 2 }];
  for (const segment of monotoneSegments(points)) for (let step = 0; step <= 100; step++) {
    const t = step / 100, s = 1 - t, y = s ** 3 * segment.a.y + 3 * s ** 2 * t * segment.c1.y + 3 * s * t ** 2 * segment.c2.y + t ** 3 * segment.b.y;
    assert.ok(y >= Math.min(segment.a.y, segment.b.y) - 1e-9 && y <= Math.max(segment.a.y, segment.b.y) + 1e-9);
  }
});

test('task follows focus but does not jump to background work', () => {
  const a = { task: 'a', targetId: '1', visible: true }, b = { task: 'b', targetId: '2', visible: true };
  assert.equal(selectFocusedTask([a, b], a).task, 'a');
  assert.equal(selectFocusedTask([a, { ...b, focused: true }], a).task, 'b');
  assert.equal(selectFocusedTask([a, b], null), null);
});

test('refreshing the same time window does not move historic observations between buckets', () => {
  const now = 1789452000123;
  const a = chartWindow(now, DAY), b = chartWindow(now + 500, DAY);
  assert.deepEqual(a, b);
  const events = [{ time: a.start + DAY / 48 + 200, credits: 9 }];
  assert.deepEqual(bucketCredits(events, a.start, a.end), bucketCredits(events, b.start, b.end));
  assert.ok(a.end > now && a.end - now <= DAY / 48);
});
