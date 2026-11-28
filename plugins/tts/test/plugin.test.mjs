import { test } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { spawnSync, spawn } from 'node:child_process';
import { readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { toSpeakable, toReadableMarkdown, extractQuestions, lastAssistantText } from '../lib/speakable.mjs';

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
    assert.equal(seen[0].text, 'All **done**.'); assert.equal(seen[0].popup, false);   // markdown goes on as written
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
    assert.deepEqual(urls, ['POST /v1/agents/prompt', 'POST /v1/replies/take']);
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

test('toReadableMarkdown keeps headings and lists, drops code and links', () => {
  const t = toReadableMarkdown('# Title\n\nSee [docs](https://x.io) or https://y.io.\n\n```js\nconst a = 1;\n```\n\n- one\n- **two**\n');
  assert.equal(t, '# Title\n\nSee docs or\n\n- one\n- **two**');
});

test('extractQuestions takes the tts-questions block out of the reply', () => {
  const r = extractQuestions('All done.\n\n```tts-questions\n1. Which branch?\n- Add tests?\n\n```\n');
  assert.deepEqual(r.questions, ['Which branch?', 'Add tests?']);
  assert.equal(r.text, 'All done.');
  assert.deepEqual(extractQuestions('Nothing to ask?').questions, []);
});

test('hook passes the questions and shows the popup when the reply has them', async () => {
  await withServer(async (url, seen) => {
    await runHook(E(url, { TTS_AUTO_READ: 'reply' }),
      JSON.stringify({ last_assistant_message: 'Parser fixed.\n\n```tts-questions\nWhich branch?\nAdd tests?\n```' }));
    assert.equal(seen[0].text, 'Parser fixed.');
    assert.deepEqual(seen[0].questions, ['Which branch?', 'Add tests?']);
    assert.equal(seen[0].expectsReply, true);
    assert.equal(seen[0].popup, undefined);
  });
});

test('cli say passes repeated --question and next/answered call the right routes', async () => {
  await withServer(async (url, seen, urls) => {
    const env = E(url);
    await new Promise((resolve) => {
      const p = spawn(process.execPath, [join(root, 'cli/tts.mjs'), 'say', '--question', 'A?', '--question', 'B?', 'Hello.'], { env: { ...process.env, ...env } });
      p.on('close', resolve);
    });
    assert.deepEqual(seen[0].questions, ['A?', 'B?']);
    for (const args of [['next'], ['answered', 'q1']]) {
      await new Promise((resolve) => {
        const p = spawn(process.execPath, [join(root, 'cli/tts.mjs'), ...args], { env: { ...process.env, ...env } });
        p.on('close', resolve);
      });
    }
    assert.deepEqual(urls.slice(1), ['POST /v1/next', 'POST /v1/pauses/q1/answered']);
  });
});

// ---- voice replies ----

async function withReplyServer(fn) {
  const seen = [];
  const server = http.createServer((req, res) => {
    let b = ''; req.on('data', (c) => (b += c));
    req.on('end', () => {
      seen.push({ method: req.method, url: req.url, body: JSON.parse(b || '{}') });
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(req.url === '/v1/replies/take'
        ? { replies: [{ seq: 1, pauseId: 'q1', question: 'Which branch?', text: 'use master' }] } : {}));
    });
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  try { await fn(`http://127.0.0.1:${server.address().port}`, seen); } finally { server.close(); }
}

test('the prompt hook hands over voice answers nobody collected', async () => {
  await withReplyServer(async (url, seen) => {
    const out = await new Promise((resolve) => {
      let text = '';
      const p = spawn(process.execPath, [join(root, 'hooks/focus.mjs')], { env: { ...process.env, ...E(url) } });
      p.stdout.on('data', (c) => (text += c));
      p.on('close', () => resolve(text));
      p.stdin.end(JSON.stringify({ cwd: '/work/my-project' }));
    });
    const take = seen.find((c) => c.url === '/v1/replies/take');
    assert.equal(take.body.source, 'my-project');
    assert.equal(take.body.waitSeconds, 0);
    const context = JSON.parse(out).hookSpecificOutput.additionalContext;
    assert.match(context, /Which branch\?.*use master/);
  });
});

test('the MCP wait_for_reply tool waits for this window\'s answers', async () => {
  const { handle } = await import('../mcp/tts-mcp.mjs');
  await withReplyServer(async (url, seen) => {
    Object.assign(process.env, E(url));
    const res = await handle({ jsonrpc: '2.0', id: 1, method: 'tools/call', params: { name: 'wait_for_reply', arguments: { timeoutSeconds: 5 } } });
    const take = seen.find((c) => c.url === '/v1/replies/take');
    assert.equal(take.body.waitSeconds, 5);
    assert.ok(take.body.source);
    assert.match(res.result.content[0].text, /use master/);
  });
});

test('cli reply posts the answer', async () => {
  await withReplyServer(async (url, seen) => {
    await new Promise((resolve) => {
      const p = spawn(process.execPath, [join(root, 'cli/tts.mjs'), 'reply', 'use', 'master'], { env: { ...process.env, ...E(url) } });
      p.on('close', resolve);
    });
    assert.deepEqual(seen[0], { method: 'POST', url: '/v1/replies', body: { text: 'use master' } });
  });
});
