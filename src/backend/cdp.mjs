import { selectFocusedTask } from './core.mjs';

export const READ_PET = String.raw`(() => {
  const visible=e=>{if(!e)return false;const r=e.getBoundingClientRect();if(!r.width||!r.height)return false;for(let p=e;p;p=p.parentElement){const s=getComputedStyle(p);if(s.display==='none'||s.visibility==='hidden'||Number(s.opacity)===0)return false;}return true;};
  const e=document.querySelector('[data-testid="avatar-mascot-button"][data-avatar-mascot="true"]');
  const r=visible(e)?e.getBoundingClientRect():null;
  const regions=[...document.querySelectorAll('[data-avatar-overlay-hit-region]')].filter(e=>e.getAttribute('data-avatar-overlay-hit-region')!=='mascot'&&visible(e)).map(e=>{const r=e.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height};}).filter(r=>r.width>15&&r.height>10);
  return {visible:document.visibilityState==='visible',mascot:r?{x:r.x,y:r.y,width:r.width,height:r.height,dpr:devicePixelRatio,innerWidth,innerHeight,regions}:null};
})()`;

export const READ_METADATA = String.raw`(() => {
  const visible = el => { if (!el) return false; const r=el.getBoundingClientRect(); if(r.width<=0 || r.height<=0)return false; for(let n=el;n;n=n.parentElement){const s=getComputedStyle(n);if(s.display==='none'||s.visibility==='hidden'||Number(s.opacity)===0)return false;} return true; };
  // The ID marker may be a zero-sized sibling of the editor. Visibility belongs to its composer root.
  const owner = el => el.closest('[data-codex-composer-root]') || el;
  const composer = [...document.querySelectorAll('[data-above-composer-conversation-id]')].filter(el => visible(owner(el)));
  const active = document.activeElement;
  const focusedComposer = composer.find(el => owner(el).contains(active));
  const mainComposer = composer.find(el => el.closest('[data-composer-placement]')?.getAttribute('data-composer-placement') !== 'sidebar');
  const selected = focusedComposer || mainComposer || composer[0];
  const uuid = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;
  const task = selected?.getAttribute('data-above-composer-conversation-id') || (location.pathname.match(uuid)||[])[0] || null;
  const mascot = document.querySelector('[data-testid="avatar-mascot-button"][data-avatar-mascot="true"]');
  const isPet = location.pathname==='/avatar-overlay' || new URL(location.href).searchParams.get('initialRoute')==='/avatar-overlay';
  const r = visible(mascot) ? mascot.getBoundingClientRect() : null;
  return {task, focused:document.hasFocus(), visible:document.visibilityState==='visible', pet:isPet,
    mascot:r ? {x:r.x,y:r.y,width:r.width,height:r.height,dpr:devicePixelRatio,innerWidth:innerWidth} : null};
})()`;

class Connection {
  constructor(url) { this.url = url; this.pending = new Map(); this.id = 0; }
  async open() {
    this.socket = new WebSocket(this.url);
    await new Promise((resolve, reject) => { const timer = setTimeout(() => { this.socket.close(); reject(new Error('CDP timeout')); }, 2000);
      this.socket.addEventListener('open', () => { clearTimeout(timer); resolve(); }, { once: true });
      this.socket.addEventListener('error', () => { clearTimeout(timer); reject(new Error('CDP unavailable')); }, { once: true }); });
    this.socket.addEventListener('message', e => { try { const m = JSON.parse(e.data), p = this.pending.get(m.id); if (p) { clearTimeout(p.timer); this.pending.delete(m.id); m.error ? p.reject(new Error('CDP command failed')) : p.resolve(m.result); } } catch {} });
    this.socket.addEventListener('close', () => { for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(new Error('CDP disconnected')); } this.pending.clear(); });
  }
  call(method, params) { return new Promise((resolve, reject) => { const id = ++this.id, timer = setTimeout(() => { this.pending.delete(id); reject(new Error('CDP timeout')); }, 2000); this.pending.set(id, { resolve, reject, timer }); this.socket.send(JSON.stringify({ id, method, params })); }); }
  close() { this.socket.close(); }
}

export class CodexMetadata {
  constructor(port) { this.port = port; this.connections = new Map(); this.current = null; this.pet = null; this.connected = false; this.petTarget = null; this.petObservedAt = 0; }
  async poll() {
    try {
      const response = await fetch('http://127.0.0.1:' + this.port + '/json/list', { signal: AbortSignal.timeout(1500) });
      const targets = (await response.json()).filter(t => t.type === 'page' && t.webSocketDebuggerUrl && /^ws:\/\/127\.0\.0\.1:/.test(t.webSocketDebuggerUrl));
      const ids = new Set(targets.map(t => t.id));
      for (const [id, c] of this.connections) if (!ids.has(id)) { c.close(); this.connections.delete(id); }
      const metadata = await Promise.all(targets.map(async t => {
        try {
          let c = this.connections.get(t.id);
          if (!c) { c = new Connection(t.webSocketDebuggerUrl); await c.open(); this.connections.set(t.id, c); }
          const result = await c.call('Runtime.evaluate', { expression: READ_METADATA, returnByValue: true });
          return { ...result.result.value, targetId: t.id };
        } catch { const c = this.connections.get(t.id); c?.close(); this.connections.delete(t.id); return null; }
      }));
      this.connected = true;
      this.current = selectFocusedTask(metadata.filter(Boolean), this.current);
      const found = metadata.find(t => t?.pet);
      this.petTarget = found?.targetId || null;
      if (!this.petTarget && Date.now() - this.petObservedAt > 1200) this.pet = null;
    } catch { this.connected = false; this.current = null; if (Date.now() - this.petObservedAt > 1200) this.pet = null; }
  }
  async pollPet() {
    const connection = this.connections.get(this.petTarget);
    if (!connection) return;
    try { const result = await connection.call('Runtime.evaluate', { expression: READ_PET, returnByValue: true });
      if (result.result?.value) { this.pet = result.result.value; this.petObservedAt = Date.now(); }
    } catch { if (Date.now() - this.petObservedAt > 1200) this.pet = null; }
  }
  close() { for (const c of this.connections.values()) c.close(); this.connections.clear(); }
}
