import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawnSync, spawn } from 'node:child_process';
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { toSpeakable, lastAssistantText } from '../lib/speakable.mjs';

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, '..');
const repo = join(root, '..', '..');

test('bundled scripts match integrations/ (copy them again if this fails)', () => {
  for (const [a, b] of [['tts-client.mjs', 'tts-client.mjs'], ['cli/tts.mjs', 'cli/tts.mjs'], ['mcp/tts-mcp.mjs', 'mcp/tts-mcp.mjs']]) {
    assert.equal(readFileSync(join(root, a), 'utf8'), readFileSync(join(repo, 'integrations', b), 'utf8'), a);
  }
});

test('manifests are valid JSON', () => {
  for (const f of ['.claude-plugin/plugin.json', '.mcp.json', 'hooks/hooks.json']) JSON.parse(readFileSync(join(root, f), 'utf8'));
  JSON.parse(readFileSync(join(repo, '.claude-plugin/marketplace.json'), 'utf8'));
});

test('toSpeakable strips markdown and code', () => {
  const t = toSpeakable('# Title\n\nUse `foo` and [the docs](https://x.io).\n\n```js\nconst a = 1;\n```\n\n- one\n- **two**\n');
  assert.equal(t, 'Title\n\nUse foo and the docs.\n\none\ntwo');
});

test('toSpeakable trims long text at a sentence', () => {
  const t = toSpeakable('Hello there. '.repeat(300), 100);
  assert.ok(t.length <= 100 && t.endsWith('.'));
});

test('lastAssistantText finds the last assistant text block', () => {
  const j = [
    { type: 'assistant', message: { role: 'assistant', content: [{ type: 'text', text: 'old' }] } },
    { type: 'user', message: { role: 'user', content: 'q' } },
    { type: 'assistant', message: { role: 'assistant', content: [{ type: 'tool_use' }, { type: 'text', text: 'new' }] } },
  ].map((x) => JSON.stringify(x)).join('\n');
  assert.equal(lastAssistantText(j), 'new');
});

async function withServer(fn) {
  const seen = [];
  const server = http.createServer((req, res) => {
    let b = ''; req.on('data', (c) => (b += c));
    req.on('end', () => { seen.push(JSON.parse(b || '{}')); res.writeHead(200, { 'Content-Type': 'application/json' }); res.end('{}'); });
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  try { await fn(`http://127.0.0.1:${server.address().port}`, seen); } finally { server.close(); }
}

function runHook(env, input) {
  return new Promise((resolve) => {
    const p = spawn(process.execPath, [join(root, 'hooks/auto-read.mjs')], { env: { ...process.env, ...env } });
    p.on('close', (code) => resolve(code));
    p.stdin.end(input);
  });
}

test('hook is silent unless TTS_AUTO_READ is set', async () => {
  await withServer(async (url, seen) => {
    const code = await runHook({ TTS_API_URL: url, TTS_API_TOKEN: 't', TTS_AUTO_READ: '' }, '{}');
    assert.equal(code, 0); assert.equal(seen.length, 0);
  });
});

test('hook reads the last reply when TTS_AUTO_READ=reply', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'tts-'));
  const tp = join(dir, 't.jsonl');
  writeFileSync(tp, JSON.stringify({ type: 'assistant', message: { role: 'assistant', content: [{ type: 'text', text: 'All **done**.' }] } }));
  await withServer(async (url, seen) => {
    const code = await runHook({ TTS_API_URL: url, TTS_API_TOKEN: 't', TTS_AUTO_READ: 'reply' }, JSON.stringify({ transcript_path: tp }));
    assert.equal(code, 0);
    assert.equal(seen[0].text, 'All done.'); assert.equal(seen[0].popup, false);
  });
});

test('hook exits 0 when the app is not running', async () => {
  const code = await runHook({ TTS_API_URL: 'http://127.0.0.1:1', TTS_API_TOKEN: 't', TTS_AUTO_READ: 'announce' }, '{}');
  assert.equal(code, 0);
});

test('toSpeakable drops tilde, long and unclosed code fences', () => {
  assert.equal(toSpeakable('Before.\n\n~~~\nsecret = 1\n~~~\n\nAfter.'), 'Before.\n\nAfter.');
  assert.equal(toSpeakable('Before.\n\n````md\n```js\nx\n```\ny\n````\n\nAfter.'), 'Before.\n\nAfter.');
  assert.equal(toSpeakable('Before.\n\n```js\nnever closed = 1'), 'Before.');
});

test('hook prefers last_assistant_message over the transcript', async () => {
  await withServer(async (url, seen) => {
    await runHook({ TTS_API_URL: url, TTS_API_TOKEN: 't', TTS_AUTO_READ: 'reply' },
      JSON.stringify({ last_assistant_message: 'Direct reply.', transcript_path: '/nonexistent' }));
    assert.equal(seen[0].text, 'Direct reply.');
  });
});
