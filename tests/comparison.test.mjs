import test from 'node:test';
import assert from 'node:assert/strict';
import { comparisonGroups, forecastPresentation, resetCreditCount, datePeriod } from '../src/backend/comparison.mjs';

test('manual comparisons persist across changes in rank, time range and title', () => {
  const catalog = [{ id: 'a', name: 'One', total: 10, latest: 1 }, { id: 'b', name: 'Two', total: 30, latest: 2 }];
  assert.deepEqual(comparisonGroups(catalog, ['a']).groups.map(g => g.id), ['a']);
  assert.deepEqual(comparisonGroups(catalog, []).groups, []);
  assert.equal(comparisonGroups(catalog).groups[0].id, 'b');
  assert.equal(comparisonGroups([{ ...catalog[0], name: 'Renamed', total: 0 }, catalog[1]], ['a']).groups[0].name, 'Renamed');
  assert.equal(comparisonGroups(catalog, ['a', 'a']).groups.length, 1);
});
test('depletion is expressed as a day period and reset credits distinguish zero from unavailable', () => {
  for (const [hour, period] of [[2, '凌晨'], [8, '早上'], [12, '中午'], [15, '下午'], [21, '晚上']]) {
    assert.deepEqual(forecastPresentation({ exhaustion: new Date(2026, 8, 20, hour, 37).getTime() }), { value: '9/20 '+period, label: '！预计提前耗尽' });
  }
  assert.equal(datePeriod(new Date(2026,8,21,12,16).getTime()), '9/21 中午');
  assert.deepEqual(forecastPresentation({label:'预计可用至重置',exhaustion:null}), {value:'至重置',label:'预计不会耗尽'});
  assert.equal(resetCreditCount({ rateLimitResetCredits: { availableCount: 3 } }), 3);
  assert.equal(resetCreditCount({ rateLimitResetCredits: { availableCount: 0 } }), 0);
  assert.equal(resetCreditCount({}), null);
});
