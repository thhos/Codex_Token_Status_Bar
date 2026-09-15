import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';

test('backend survives an empty profile, reports missing data, validates and persists UI settings', { timeout: 10_000 }, async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'credits-backend-'));
  const child = spawn(process.execPath, ['src/backend/main.mjs', '--data-dir', dir, '--codex', path.join(dir, 'missing.exe'), '--port', '65530'], {
    env: { ...process.env, CODEX_HOME: dir }, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe']
  });
  child.stderr.on('data', () => {});
  const messages = [], waiters = [];
  createInterface({ input: child.stdout }).on('line', line => { try { const m = JSON.parse(line); messages.push(m); for (const resolve of waiters.splice(0)) resolve(); } catch {} });
  const exit = new Promise(resolve => child.on('exit', resolve));
  async function until(predicate) {
    const deadline = Date.now() + 4000;
    while (!messages.some(predicate)) { if (Date.now() > deadline) throw new Error('Expected view not published'); await Promise.race([new Promise(resolve => waiters.push(resolve)), new Promise(resolve => setTimeout(resolve, 40))]); }
    return messages.find(predicate);
  }
  try {
    const empty = await until(m => m.type === 'view');
    assert.equal(empty.remaining, null); assert.equal(empty.total, null); assert.equal(empty.forecast, '正在学习');
    child.stdin.write(JSON.stringify({ type: 'settings', density: 2, opacity: 63, range: '7d', scope: 'projects', theme: 'light' }) + '\n');
    await until(m => m.settings?.opacity === 63 && m.settings?.range === '7d');
    const saved = JSON.parse(fs.readFileSync(path.join(dir, 'settings.json')));
    assert.deepEqual(saved, { density: 2, opacity: 63, range: '7d', scope: 'projects', theme: 'light' });
    child.stdin.write(JSON.stringify({ type: 'settings', opacity: -10, range: 'invalid', density: 999 }) + '\n');
    const validated = await until(m => m.settings?.opacity === 40);
    assert.equal(validated.settings.range, '7d'); assert.equal(validated.settings.density, 2);
    child.stdin.write('{invalid\n'); child.stdin.write(JSON.stringify({ type: 'shutdown' }) + '\n');
    await exit;
  } finally {
    if (child.exitCode == null) { child.kill(); await exit; }
    const resolved = path.resolve(dir);
    if (path.dirname(resolved) !== path.resolve(os.tmpdir()) || !path.basename(resolved).startsWith('credits-backend-')) throw new Error('Unsafe cleanup path');
    fs.rmSync(resolved, { recursive: true, force: true });
  }
});
