import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';

// A private stdio connection reuses Codex authentication without reading credentials.
export class AppServer {
  constructor(executable) { this.executable = executable; this.pending = new Map(); this.nextId = 0; }
  async start() {
    this.child = spawn(this.executable, ['app-server', '--listen', 'stdio://'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    this.child.stderr.on('data', () => {});
    this.child.on('error', () => this.failAll('无法启动 Codex 数据服务'));
    this.child.on('exit', () => { this.child = null; this.failAll('Codex 数据服务已退出'); });
    createInterface({ input: this.child.stdout }).on('line', line => {
      try {
        const message = JSON.parse(line), pending = this.pending.get(message.id);
        if (!pending) return;
        clearTimeout(pending.timer); this.pending.delete(message.id);
        if (message.error) pending.reject(new Error('Codex 接口暂不可用 (' + message.error.code + ')'));
        else pending.resolve(message.result);
      } catch { /* Non-protocol output must never be forwarded to the interface. */ }
    });
    await this.request('initialize', { clientInfo: { name: 'codex_pet_credits', version: '1.0.0' }, capabilities: { experimentalApi: true } });
    this.child.stdin.write(JSON.stringify({ method: 'initialized' }) + '\n');
  }
  request(method, params = {}) {
    if (!this.child) return Promise.reject(new Error('数据服务未连接'));
    return new Promise((resolve, reject) => {
      const id = ++this.nextId;
      const timer = setTimeout(() => { this.pending.delete(id); reject(new Error('数据请求超时')); }, 20_000);
      this.pending.set(id, { resolve, reject, timer });
      this.child.stdin.write(JSON.stringify({ id, method, params }) + '\n');
    });
  }
  failAll(message) { for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(new Error(message)); } this.pending.clear(); }
  close() { this.failAll('数据服务关闭'); this.child?.stdin.end(); const child = this.child; setTimeout(() => child?.kill(), 1000).unref(); }
}
