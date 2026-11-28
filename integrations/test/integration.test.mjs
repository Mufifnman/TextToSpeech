import { test, before, after } from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import { readFileSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { findConnection, call, TtsError, pidAlive } from '../tts-client.mjs';
import { run } from '../cli/tts.mjs';
import { handle, TOOLS } from '../mcp/tts-mcp.mjs';

const TOKEN = 'secret-token';
let server;
let seen = [];

before(async () => {
  server = http.createServer((req, res) => {
    let body = '';
    req.on('data', (c) => (body += c));
    req.on('end', () => {
      seen.push({ method: req.method, url: req.url, auth: req.headers.authorization, body: body ? JSON.parse(body) : undefined });
      if (req.headers.authorization !== `Bearer ${TOKEN}`) {
        res.writeHead(401, { 'Content-Type': 'application/json' });
        return res.end(JSON.stringify({ error: { code: 'unauthorized', message: 'bad token' } }));
      }
      if (req.url === '/v1/conflict') {
        res.writeHead(409, { 'Content-Type': 'application/json' });
        return res.end(JSON.stringify({ error: { code: 'handoff_closed', message: 'closed' } }));
      }
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ ok: true, url: req.url }));
    });
  });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  process.env.TTS_API_URL = `http://127.0.0.1:${server.address().port}/`;
  process.env.TTS_API_TOKEN = TOKEN;
});

after(() => server.close());

test('findConnection prefers environment variables and trims the slash', () => {
  const c = findConnection({ TTS_API_URL: 'http://127.0.0.1:5/', TTS_API_TOKEN: 't' });
  assert.deepEqual(c, { baseUrl: 'http://127.0.0.1:5', token: 't' });
});

test('findConnection reads api.json', () => {
  const c = findConnection({ TTS_API_FILE: 'x' }, () => JSON.stringify({ port: 1234, token: 'abc' }));
  assert.deepEqual(c, { baseUrl: 'http://127.0.0.1:1234', token: 'abc' });
});

test('findConnection reports a missing or incomplete file', () => {
  assert.throws(() => findConnection({ TTS_API_FILE: 'x' }, () => { throw new Error('nope'); }), { code: 'not_running' });
  assert.throws(() => findConnection({ TTS_API_FILE: 'x' }, () => '{}'), { code: 'not_running' });
});

test('findConnection reports a left-over api.json whose pid is gone', () => {
  const file = () => JSON.stringify({ port: 1234, token: 'abc', pid: 4242 });
  assert.throws(() => findConnection({ TTS_API_FILE: 'x' }, file, () => false), { code: 'not_running' });
  assert.deepEqual(findConnection({ TTS_API_FILE: 'x' }, file, () => true), { baseUrl: 'http://127.0.0.1:1234', token: 'abc' });
});

test('findConnection ignores a missing or invalid pid, and the env override skips the check', () => {
  const never = () => { throw new Error('should not be asked'); };
  assert.ok(findConnection({ TTS_API_FILE: 'x' }, () => JSON.stringify({ port: 1, token: 't' }), never));
  assert.ok(findConnection({ TTS_API_FILE: 'x' }, () => JSON.stringify({ port: 1, token: 't', pid: 'x' }), never));
  assert.ok(findConnection({ TTS_API_URL: 'http://127.0.0.1:5', TTS_API_TOKEN: 't' }, never, never));
});

test('pidAlive: this process is alive, an impossible pid is not', () => {
  assert.equal(pidAlive(process.pid), true);
  assert.equal(pidAlive(2 ** 22 + 12345), false);
});

test('error codes in the C# code are documented', () => {
  const here = dirname(fileURLToPath(import.meta.url));
  const repo = join(here, '..', '..');
  const docs = readFileSync(join(repo, 'docs', 'api', 'compatibility.md'), 'utf8');
  const codes = new Set(['bad_request', 'not_found']);
  const dirs = [join(repo, 'TextToSpeech.Api'), join(repo, 'TextToSpeech.Api', 'Arbitration')];
  for (const dir of dirs) {
    for (const f of readdirSync(dir).filter((n) => n.endsWith('.cs'))) {
      const src = readFileSync(join(dir, f), 'utf8');
      for (const m of src.matchAll(/ApiException\.(?:BadRequest|NotFound)\((?:[^;]*?),\s*"([a-z_]+)"\)/g)) codes.add(m[1]);
      for (const m of src.matchAll(/ApiException\.Conflict\("([a-z_]+)"/g)) codes.add(m[1]);
      for (const m of src.matchAll(/(?:ApiSecurity\.)?Error\(\d+,\s*"([a-z_]+)"/g)) codes.add(m[1]);
      for (const m of src.matchAll(/new ApiException\(\d+,\s*"([a-z_]+)"/g)) codes.add(m[1]);
    }
  }
  assert.ok(codes.size > 25, 'the scan found ' + codes.size + ' codes; the pattern probably broke');
  for (const code of codes) assert.ok(docs.includes('`' + code + '`'), code + ' is not in docs/api/compatibility.md');
});

test('call sends the bearer token and JSON body', async () => {
  seen = [];
  const r = await call('POST', '/v1/speak', { text: 'hi' });
  assert.equal(r.ok, true);
  assert.equal(seen[0].auth, `Bearer ${TOKEN}`);
  assert.deepEqual(seen[0].body, { text: 'hi' });
});

test('call maps server errors to TtsError with the code', async () => {
  await assert.rejects(call('GET', '/v1/conflict'), (e) => e instanceof TtsError && e.status === 409 && e.code === 'handoff_closed');
  await assert.rejects(call('GET', '/v1/status', undefined, { env: { TTS_API_URL: process.env.TTS_API_URL, TTS_API_TOKEN: 'wrong' } }),
    (e) => e.status === 401 && e.code === 'unauthorized');
});

test('call reports an unreachable app', async () => {
  await assert.rejects(call('GET', '/v1/status', undefined, { env: { TTS_API_URL: 'http://127.0.0.1:1', TTS_API_TOKEN: 't' } }),
    { code: 'unreachable' });
});

test('CLI say posts the text with mode and quiet', async () => {
  seen = [];
  const out = [];
  await run(['say', 'hello', 'world', '--mode', 'interrupt', '--quiet'], { out: (s) => out.push(s) });
  assert.equal(seen[0].url, '/v1/speak');
  assert.equal(seen[0].body.text, 'hello world');
  assert.equal(seen[0].body.mode, 'interrupt');
  assert.equal(seen[0].body.popup, false);
  assert.equal(seen[0].body.source, 'cli');
  assert.ok(out[0].includes('"ok": true'));
});

test('CLI say reads stdin and refuses empty text', async () => {
  seen = [];
  await run(['say', '-'], { stdin: () => 'from stdin', out: () => {} });
  assert.equal(seen[0].body.text, 'from stdin');
  await assert.rejects(run(['say', '   '], { out: () => {} }), { code: 'usage' });
});

test('CLI controls, settings and handoff commands hit the right routes', async () => {
  seen = [];
  const out = () => {};
  await run(['pause'], { out });
  await run(['rate', '3'], { out });
  await run(['volume', '90'], { out });
  await run(['take', 'current', '--lease', '60'], { out });
  await run(['replace', 's1', '--handoff', 'h1', '--text', 'new', '--no-resume'], { out });
  await run(['release', 's1', '--handoff', 'h1'], { out });
  assert.deepEqual(seen.map((s) => `${s.method} ${s.url}`), [
    'POST /v1/pause', 'PUT /v1/rate', 'PUT /v1/volume',
    'POST /v1/sessions/current/take', 'POST /v1/sessions/s1/replace', 'POST /v1/sessions/s1/release',
  ]);
  assert.equal(seen[1].body.rate, 3);
  assert.equal(seen[3].body.leaseSeconds, 60);
  assert.equal(seen[4].body.autoResume, false);
  assert.equal(seen[4].body.handoffId, 'h1');
});

test('CLI replace needs a handoff id and text; unknown commands show usage', async () => {
  await assert.rejects(run(['replace', 's1', '--text', 'x'], { out: () => {} }), { code: 'usage' });
  await assert.rejects(run(['bogus'], { out: () => {} }), { code: 'usage' });
});

test('MCP handshake and tool list', async () => {
  const init = await handle({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2024-11-05' } });
  assert.equal(init.result.protocolVersion, '2024-11-05');
  const list = await handle({ jsonrpc: '2.0', id: 2, method: 'tools/list' });
  assert.equal(list.result.tools.length, TOOLS.length);
  assert.equal(await handle({ jsonrpc: '2.0', method: 'notifications/initialized' }), null);
  assert.equal((await handle({ jsonrpc: '2.0', id: 3, method: 'nope' })).error.code, -32601);
});

test('MCP tool call returns JSON text, and errors come back as isError', async () => {
  const first = TOOLS.find((t) => t.name.includes('status')) ?? TOOLS[0];
  const ok = await handle({ jsonrpc: '2.0', id: 4, method: 'tools/call', params: { name: first.name, arguments: {} } });
  assert.equal(ok.result.isError, undefined);
  assert.ok(ok.result.content[0].text.includes('ok'));

  const saved = process.env.TTS_API_TOKEN;
  process.env.TTS_API_TOKEN = 'wrong';
  try {
    const bad = await handle({ jsonrpc: '2.0', id: 5, method: 'tools/call', params: { name: first.name, arguments: {} } });
    assert.equal(bad.result.isError, true);
    assert.ok(bad.result.content[0].text.includes('unauthorized'));
  } finally {
    process.env.TTS_API_TOKEN = saved;
  }
  assert.ok((await handle({ jsonrpc: '2.0', id: 6, method: 'tools/call', params: { name: 'zzz' } })).error);
});

test('mcp speak identifies the window as an agent; policy and mute tools exist', async () => {
  seen = [];
  await handle({ jsonrpc: '2.0', id: 7, method: 'tools/call', params: { name: 'speak', arguments: { text: 'hello' } } });
  const spoke = seen.find((s) => s.url === '/v1/speak');
  assert.match(spoke.body.agent.id, /^claude-[0-9a-f]{10}$/);
  assert.ok(spoke.body.agent.label);

  const pol = await handle({ jsonrpc: '2.0', id: 8, method: 'tools/call', params: { name: 'reading_policy', arguments: {} } });
  assert.equal(pol.result.isError, undefined);
  assert.ok(seen.some((s) => s.url === '/v1/policy') && seen.some((s) => s.url === '/v1/agents'));

  await handle({ jsonrpc: '2.0', id: 9, method: 'tools/call', params: { name: 'mute_reading', arguments: { minutes: 30 } } });
  assert.deepEqual(seen.find((s) => s.url === '/v1/policy/mute').body, { minutes: 30 });
});

test('prewarm calls: cli sends a hint, cancel and stats; mcp ties the hint to this window', async () => {
  seen = [];
  await run(['prewarm', 'a', 'long', 'reply', '--agent', 'w1'], { out: () => {} });
  assert.equal(seen[0].url, '/v1/prewarm');
  assert.equal(seen[0].method, 'POST');
  assert.deepEqual(seen[0].body, { text: 'a long reply', agent: { id: 'w1' } });

  await run(['prewarm-cancel'], { out: () => {} });
  assert.equal(seen[1].url, '/v1/prewarm/cancel');
  await run(['prewarm-stats'], { out: () => {} });
  assert.equal(seen[2].method, 'GET');
  assert.equal(seen[2].url, '/v1/prewarm');

  await handle({ jsonrpc: '2.0', id: 9, method: 'tools/call', params: { name: 'prewarm_reading', arguments: { text: 'hello' } } });
  const hint = seen.find((s) => s.url === '/v1/prewarm' && s.method === 'POST' && s.body.text === 'hello');
  assert.match(hint.body.agent.id, /^claude-[0-9a-f]{10}$/);   // same identity as speak, so the real read confirms it
  assert.ok(TOOLS.some((t) => t.name === 'cancel_prewarm') && TOOLS.some((t) => t.name === 'prewarm_stats'));
});
