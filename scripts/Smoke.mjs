import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import path from 'node:path';
import fs from 'node:fs';
const child = spawn(process.execPath, ['src/backend/main.mjs', '--smoke', '--data-dir', path.resolve('artifacts/smoke-data')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
let summary, latest, errors = '';
child.stderr.on('data', chunk => { errors += chunk.toString(); });
createInterface({ input: child.stdout }).on('line', line => {
  try { const message = JSON.parse(line); if (message.type === 'smoke') summary = message; if (message.type === 'view') latest = message; } catch {}
});
const timeout = setTimeout(() => { child.kill(); process.exitCode = 1; }, 45_000);
child.on('exit', code => {
  clearTimeout(timeout); fs.mkdirSync('artifacts', { recursive: true });
  if (latest) fs.writeFileSync('artifacts/live-view.json', JSON.stringify(latest, null, 2));
  if (summary) console.log(JSON.stringify(summary));
  else console.log(JSON.stringify({ error: 'No smoke result', exit: code, diagnostic: errors.slice(0, 300) }));
  if (!summary?.quota) process.exitCode = 1;
});
