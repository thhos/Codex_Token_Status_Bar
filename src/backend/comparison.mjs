// Saved IDs are independent of rank, titles, and the currently selected time range.
export function comparisonGroups(catalog, selectedIds, maximum = 5) {
  const sorted = [...catalog].sort((a, b) => b.total - a.total || b.latest - a.latest || a.id.localeCompare(b.id));
  const ids = Array.isArray(selectedIds) ? [...new Set(selectedIds)].slice(0, maximum) : sorted.slice(0, 3).map(g => g.id);
  const byId = new Map(sorted.map(g => [g.id, g]));
  const groups = ids.map(id => byId.get(id)).filter(Boolean);
  return { groups, choices: sorted.map(({ id, name, total }) => ({ id, name, total, selected: ids.includes(id) })) };
}

// Use the same coarse clock vocabulary for depletion and scheduled resets.
export function datePeriod(timestamp) {
  if (!Number.isFinite(timestamp)) return '—';
  const date = new Date(timestamp), hour = date.getHours();
  const period = hour < 6 ? '凌晨' : hour < 11 ? '早上' : hour < 14 ? '中午' : hour < 18 ? '下午' : '晚上';
  return `${date.getMonth() + 1}/${date.getDate()} ${period}`;
}

export function forecastPresentation(prediction) {
  if (prediction.label === '额度已用尽') return { value: '已用尽', label: '等待重置' };
  if (prediction.label === '等待重置更新') return { value: '待更新', label: '等待重置' };
  if (Number.isFinite(prediction.exhaustion)) {
    return { value: datePeriod(prediction.exhaustion), label: '！预计提前耗尽' };
  }
  if (prediction.label === '预计可用至重置') return { value: '至重置', label: '预计不会耗尽' };
  return { value: prediction.label === '数据待更新' ? '待更新' : '学习中', label: '耗尽预测' };
}

export function resetCreditCount(rates) {
  const count = rates?.rateLimitResetCredits?.availableCount;
  return Number.isInteger(count) && count >= 0 ? count : null;
}
