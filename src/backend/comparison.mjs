// Saved IDs are independent of rank, titles, and the currently selected time range.
export function comparisonGroups(catalog, selectedIds, maximum = 5) {
  const sorted = [...catalog].sort((a, b) => b.total - a.total || b.latest - a.latest || a.id.localeCompare(b.id));
  const ids = Array.isArray(selectedIds) ? [...new Set(selectedIds)].slice(0, maximum) : sorted.slice(0, 3).map(g => g.id);
  const byId = new Map(sorted.map(g => [g.id, g]));
  const groups = ids.map(id => byId.get(id)).filter(Boolean);
  return { groups, choices: sorted.map(({ id, name, total }) => ({ id, name, total, selected: ids.includes(id) })) };
}

export function forecastPresentation(prediction) {
  if (prediction.label === '额度已用尽') return { value: '已用尽', label: '等待重置' };
  if (prediction.label === '等待重置更新') return { value: '待更新', label: '等待重置' };
  if (Number.isFinite(prediction.exhaustion)) {
    const date = new Date(prediction.exhaustion), hour = date.getHours();
    return { value: hour < 6 ? '凌晨' : hour < 12 ? '早上' : hour < 18 ? '下午' : '晚上', label: `${date.getMonth() + 1}/${date.getDate()} 耗尽` };
  }
  if (prediction.label === '预计可用至重置') return { value: '充足', label: '可用至重置' };
  return { value: prediction.label === '数据待更新' ? '待更新' : '学习中', label: '耗尽预测' };
}

export function resetCreditCount(rates) {
  const count = rates?.rateLimitResetCredits?.availableCount;
  return Number.isInteger(count) && count >= 0 ? count : null;
}
