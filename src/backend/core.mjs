// Pure accounting and prediction functions. No UI or disk access belongs here.
export const HOUR = 3_600_000;
export const DAY = 24 * HOUR;

export function usageDelta(current, previous) {
  const fields = ['input_tokens', 'cached_input_tokens', 'output_tokens'];
  if (!current || fields.some(k => !Number.isFinite(current[k]) || current[k] < 0)) return null;
  if (previous && fields.some(k => current[k] < (previous[k] || 0))) return null;
  return Object.fromEntries(fields.map(k => [k, current[k] - (previous?.[k] || 0)]));
}

export function estimateCredits(usage, model, tier, card) {
  // Explicit user-provided aliases use the corresponding model's complete rate and speed table.
  const rate = card.models[model] || card.models[card.modelAliases?.[model]];
  if (!rate || !usage || usage.cache_write_input_tokens > 0) return null;
  const { input_tokens: input, cached_input_tokens: cached, output_tokens: output } = usage;
  if (![input, cached, output].every(n => Number.isFinite(n) && n >= 0) || cached > input) return null;
  // Cached input is a subset of input; reasoning output is already part of output.
  const standard = ['default', 'standard', 'auto'].includes(tier);
  const multiplier = tier === 'fast' || tier === 'priority' ? rate.fastMultiplier : standard ? 1 : null;
  if (!Number.isFinite(multiplier)) return null;
  return ((input - cached) * rate.input + cached * rate.cached + output * rate.output) * multiplier / 1e6;
}

export function selectQuota(response) {
  const bucket = response?.rateLimitsByLimitId?.codex || response?.rateLimits;
  if (!bucket) return null;
  const windows = [bucket.primary, bucket.secondary].filter(w => w && Number.isFinite(w.usedPercent));
  // Window names are not semantic: primary can be a weekly window.
  const week = windows.find(w => w.windowDurationMins === 10080);
  const selected = week || windows[0];
  return selected ? { ...selected, remaining: Math.max(0, Math.min(100, 100 - selected.usedPercent)), windows } : null;
}

export function forecastQuota(samples, activity, now, quota) {
  const empty = { label: '正在学习', detail: '等待足够的额度变化和活跃时段样本', warning: false, exhaustion: null };
  if (!quota || !samples.length) return empty;
  if (now - samples.at(-1).time > 5 * 60_000) return { ...empty, label: '数据待更新', detail: '额度数据超过 5 分钟未更新，预测已暂停' };
  if (quota.remaining <= 0) return { label: '额度已用尽', detail: '等待额度重置', warning: true, exhaustion: now };
  const end = quota.resetsAt * 1000;
  if (end <= now) return { ...empty, label: '等待重置更新' };
  const valid = samples.filter(s => s.reset === quota.resetsAt && s.time >= now - 14 * DAY && s.time <= now);
  const periods = [];
  for (let i = 1; i < valid.length; i++) {
    const a = valid[i - 1], b = valid[i], dt = b.time - a.time, consumed = b.used - a.used;
    // Never attribute downtime, reset drops or one-point quantization jumps to a short active interval.
    if (dt > 0 && dt <= 120_000 && consumed >= 0) periods.push({ start: a.time, end: b.time, consumed, dt });
  }
  const observedMs = periods.reduce((sum, p) => sum + p.dt, 0);
  const consumed = periods.reduce((sum, p) => sum + p.consumed, 0);
  const activeTimes = activity.filter(t => t > now - 14 * DAY && t <= now).sort((a, b) => a - b);
  const observedDays = new Set(activeTimes.map(t => new Date(t).toDateString()));
  if (observedMs < HOUR || consumed < 2 || observedDays.size < 2) return empty;
  const activeIn = (start, end) => {
    let low = 0, high = activeTimes.length;
    while (low < high) { const middle = (low + high) >>> 1; if (activeTimes[middle] < start - 10 * 60_000) low = middle + 1; else high = middle; }
    return low < activeTimes.length && activeTimes[low] <= end;
  };
  const activePeriods = periods.filter(p => activeIn(p.start, p.end));
  const activeHours = activePeriods.reduce((sum, p) => sum + p.dt, 0) / HOUR;
  if (activeHours < 0.5) return empty;
  const historicRate = consumed / activeHours;
  const recent = periods.filter(p => p.end >= now - 2 * HOUR);
  const recentHours = recent.filter(p => activeIn(p.start, p.end)).reduce((s, p) => s + p.dt, 0) / HOUR;
  const recentConsumed = recent.reduce((s, p) => s + p.consumed, 0);
  const rate = recentHours >= 0.5 && recentConsumed >= 2 ? historicRate * 0.4 + recentConsumed / recentHours * 0.6 : historicRate;
  if (!(rate > 0)) return empty;
  // A day/hour is counted once, so high-frequency events do not distort work habits.
  const hourly = new Map();
  for (const t of activeTimes) {
    const d = new Date(t), key = d.getHours();
    if (!hourly.has(key)) hourly.set(key, new Set());
    hourly.get(key).add(d.toDateString());
  }
  let remaining = quota.remaining;
  for (let t = now; t < end; t += HOUR / 4) {
    const stepHours = Math.min(HOUR / 4, end - t) / HOUR;
    let probability = (hourly.get(new Date(t).getHours())?.size || 0) / observedDays.size;
    if (t < now + HOUR && activeTimes.at(-1) > now - 10 * 60_000) probability = Math.max(probability, 0.85);
    remaining -= rate * probability * stepHours;
    if (remaining <= 0) {
      const exhaustion = t + stepHours * HOUR;
      return { label: '预计 ' + new Date(exhaustion).toLocaleString('zh-CN', { month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false }) + ' 耗尽',
        detail: '根据已观察的工作时段与近期消耗速度估算；未采样期间会降低预测可靠性', warning: true, exhaustion };
    }
  }
  return { label: '预计可用至重置', detail: '根据已观察的工作习惯估算，重置前预计仍有余量', warning: false, exhaustion: null };
}

export function bucketCredits(events, start, end, count = 48) {
  const width = (end - start) / count;
  const values = Array(count).fill(0), unknown = Array(count).fill(false);
  for (const event of events) {
    if (event.time < start || event.time > end) continue;
    const index = Math.min(count - 1, Math.floor((event.time - start) / width));
    if (index < 0) continue;
    if (event.credits == null) unknown[index] = true;
    else values[index] += event.credits;
  }
  return values.map((value, i) => ({ time: start + (i + 0.5) * width, value: unknown[i] ? null : value }));
}

export function chartWindow(now, duration, count = 48) {
  // Anchor buckets to clock boundaries; a half-second UI refresh must not rebucket old usage.
  const width = duration / count;
  const end = (Math.floor(now / width) + 1) * width;
  return { start: end - duration, end };
}

export function monotoneSegments(points) {
  // Monotone cubic interpolation: smooth curves without invented extrema.
  if (points.length < 2) return [];
  const slopes = points.slice(1).map((p, i) => (p.y - points[i].y) / (p.x - points[i].x));
  const tangent = points.map((_, i) => i === 0 ? slopes[0] : i === points.length - 1 ? slopes.at(-1) : slopes[i - 1] * slopes[i] <= 0 ? 0 : 2 / (1 / slopes[i - 1] + 1 / slopes[i]));
  return slopes.map((_, i) => { const a = points[i], b = points[i + 1], h = (b.x - a.x) / 3;
    return { a, c1: { x: a.x + h, y: a.y + h * tangent[i] }, c2: { x: b.x - h, y: b.y - h * tangent[i + 1] }, b }; });
}

export function selectFocusedTask(targets, previous) {
  const main = targets.filter(t => t.task && t.visible && !t.pet);
  const focused = main.find(t => t.focused);
  // Keep the last viewed task while another app is foreground; never select on token activity.
  return focused || (previous && main.find(t => t.targetId === previous.targetId && t.task === previous.task)) || (main.length === 1 ? main[0] : null);
}
