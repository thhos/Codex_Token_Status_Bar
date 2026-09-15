import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { DatabaseSync } from 'node:sqlite';
import { UsageIndex } from '../src/backend/usage.mjs';

test('task names use the latest renamed title, never the initial message stored in SQLite', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'credits-titles-'));
  try {
    const db = new DatabaseSync(path.join(dir, 'state_5.sqlite'));
    db.exec("CREATE TABLE threads(id TEXT, title TEXT, cwd TEXT); INSERT INTO threads VALUES('task','[$skill](C:/first-prompt)','D:/project')"); db.close();
    fs.writeFileSync(path.join(dir, 'session_index.jsonl'), [
      { id: 'task', thread_name: 'Current task title', updated_at: '2026-09-15T08:00:00Z' },
      { id: 'task', thread_name: 'Old task title', updated_at: '2026-09-15T07:00:00Z' }
    ].map(JSON.stringify).join('\n') + '\n');
    const index = new UsageIndex(dir, { version: 'test' }); index.loadThreadMetadata();
    assert.equal(index.threads.get('task').title, 'Current task title');
    assert.equal(index.threads.get('task').cwd, 'D:/project');
    fs.appendFileSync(path.join(dir, 'session_index.jsonl'), JSON.stringify({ id: 'task', thread_name: 'Renamed again', updated_at: '2026-09-15T09:00:00Z' }) + '\n');
    index.loadThreadMetadata(); assert.equal(index.threads.get('task').title, 'Renamed again');
  } finally {
    if (path.dirname(path.resolve(dir)) !== path.resolve(os.tmpdir()) || !path.basename(dir).startsWith('credits-titles-')) throw new Error('Unsafe cleanup');
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
