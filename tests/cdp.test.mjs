import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { READ_METADATA } from '../src/backend/cdp.mjs';

test('task marker without its own layout box uses the visible composer root', () => {
  const root = { parentElement: null, getBoundingClientRect: () => ({ width: 700, height: 120 }), contains: () => true, getAttribute: () => 'thread' };
  const marker = { parentElement: root, getBoundingClientRect: () => ({ width: 0, height: 0 }), contains: () => false,
    closest: () => root, getAttribute: () => 'current-task' };
  const context = { URL, location: new URL('app://-/index.html'), innerWidth: 900, devicePixelRatio: 1,
    getComputedStyle: () => ({ display: 'block', visibility: 'visible', opacity: '1' }),
    document: { querySelectorAll: () => [marker], querySelector: () => null, activeElement: root, hasFocus: () => true, visibilityState: 'visible' } };
  assert.equal(vm.runInNewContext(READ_METADATA, context).task, 'current-task');
  context.getComputedStyle = () => ({ display: 'none', visibility: 'visible', opacity: '1' });
  assert.equal(vm.runInNewContext(READ_METADATA, context).task, null, 'hidden cached composers must remain excluded');
});
