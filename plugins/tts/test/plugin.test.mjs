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

const ALLOW = { mayRead: true, reasons: [], policy: { autoRead: 'off' } };

// `seen` holds the bodies of requests other than the policy query; `urls` holds "METHOD /path" for every request.
async function withServer(fn, policy = ALLOW) {
  const seen = [];
  const urls = [];
  const server = http.createServer((req, res) => {
    let b = ''; req.on('data', (c) => (b += c));
    req.on('end', () => {
      urls.push(`${req.method} ${req.url}`);
      const isPolicy = req.url === '/v1/policy';
      if (!isPolicy) seen.push(JSON.parse(b || '{}'));
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(isPolicy ? policy : {}));
    });
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  try { await fn(`http://127.0.0.1:${server.address().port}`, seen, urls); } finally { server.close(); }
}

function runScript(script, env, input) {
  return new Promise((resolve) => {
    const p = spawn(process.execPath, [join(root, script)], { env: { ...process.env, ...env } });
    p.on('close', (code) => resolve(code));
    p.stdin.end(input);
  });
}
const runHook = (env, input) => runScript('hooks/auto-read.mjs', env, input);
const E = (url, extra = {}) => ({ TTS_API_URL: url, TTS_API_TOKEN: 't', TTS_AUTO_READ: '', ...extra });

test('hook is silent when the app setting and the environment both say off', async () => {
  await withServer(async (url, seen) => {
    const code = await runHook(E(url), '{}');
    assert.equal(code, 0); assert.equal(seen.length, 0);
  });
});

test('hook follows the app setting when the environment is unset', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url), JSON.stringify({ last_assistant_message: 'From the app setting.' }));
    assert.equal(seen[0].text, 'From the app setting.');
  }, { mayRead: true, reasons: [], policy: { autoRead: 'reply' } });
});

test('environment off beats the app setting', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url, { TTS_AUTO_READ: 'off' }), JSON.stringify({ last_assistant_message: 'x' }));
    assert.equal(seen.length, 0);
  }, { mayRead: true, reasons: [], policy: { autoRead: 'reply' } });
});

test('hook says nothing when the app says reading is not allowed now', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url, { TTS_AUTO_READ: 'reply' }), JSON.stringify({ last_assistant_message: 'x' }));
    assert.equal(seen.length, 0);
  }, { mayRead: false, reasons: ['Quiet hours (22:00 to 07:00).'], policy: { autoRead: 'reply' } });
});

test('hook reads the last reply when TTS_AUTO_READ=reply', async () => {
  const dir = mkdtempSync(join(tmpdir(), 'tts-'));
  const tp = join(dir, 't.jsonl');
  writeFileSync(tp, JSON.stringify({ type: 'assistant', message: { role: 'assistant', content: [{ type: 'text', text: 'All **done**.' }] } }));
  await withServer(async (url, seen) => {
    const code = await runHook(E(url, { TTS_AUTO_READ: 'reply' }), JSON.stringify({ transcript_path: tp }));
    assert.equal(code, 0);
    assert.equal(seen[0].text, 'All done.'); assert.equal(seen[0].popup, false);
  });
});

test('hook identifies the window as an agent, stable per project folder', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url, { TTS_AUTO_READ: 'announce' }), JSON.stringify({ cwd: '/work/My Project' }));
    await runHook(E(url, { TTS_AUTO_READ: 'announce' }), JSON.stringify({ cwd: '/work/My Project' }));
    await runHook(E(url, { TTS_AUTO_READ: 'announce' }), JSON.stringify({ cwd: '/work/other' }));
    assert.equal(seen[0].agent.id, seen[1].agent.id);
    assert.notEqual(seen[0].agent.id, seen[2].agent.id);
    assert.equal(seen[0].agent.label, 'My Project');
    assert.equal(seen[0].agent.titleHint, 'My Project');
    assert.ok(Number.isInteger(seen[0].agent.pid));
    assert.equal(seen[0].mode, 'if-idle');
  });
});

test('hook exits 0 when the app is not running', async () => {
  const code = await runHook({ TTS_API_URL: 'http://127.0.0.1:1', TTS_API_TOKEN: 't', TTS_AUTO_READ: 'announce' }, '{}');
  assert.equal(code, 0);
});

test('prompt hook tells the app which window the user typed in', async () => {
  await withServer(async (url, seen, urls) => {
    const code = await runScript('hooks/focus.mjs', E(url), JSON.stringify({ cwd: '/work/proj' }));
    assert.equal(code, 0);
    assert.deepEqual(urls, ['POST /v1/agents/prompt']);
    assert.equal(seen[0].label, 'proj');
  });
  const code = await runScript('hooks/focus.mjs', E('http://127.0.0.1:1'), '{}');
  assert.equal(code, 0);
});

test('hooks.json wires Stop and UserPromptSubmit', () => {
  const h = JSON.parse(readFileSync(join(root, 'hooks/hooks.json'), 'utf8')).hooks;
  assert.match(h.Stop[0].hooks[0].command, /auto-read\.mjs/);
  assert.match(h.UserPromptSubmit[0].hooks[0].command, /focus\.mjs/);
});

test('toSpeakable drops tilde, long and unclosed code fences', () => {
  assert.equal(toSpeakable('Before.\n\n~~~\nsecret = 1\n~~~\n\nAfter.'), 'Before.\n\nAfter.');
  assert.equal(toSpeakable('Before.\n\n````md\n```js\nx\n```\ny\n````\n\nAfter.'), 'Before.\n\nAfter.');
  assert.equal(toSpeakable('Before.\n\n```js\nnever closed = 1'), 'Before.');
});

test('hook prefers last_assistant_message over the transcript', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url, { TTS_AUTO_READ: 'reply' }),
      JSON.stringify({ last_assistant_message: 'Direct reply.', transcript_path: '/nonexistent' }));
    assert.equal(seen[0].text, 'Direct reply.');
  });
});
