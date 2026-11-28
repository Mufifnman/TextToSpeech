// Tiny client for the Text to Speech local API (see TextToSpeech.Api). No dependencies; needs Node 18 or newer.
//
// It finds the running app through api.json (port, token), which the app writes while the local API is enabled.
// The token changes every time the app starts, so it is read again for every call.
import { readFileSync } from 'node:fs';
import { basename, join } from 'node:path';
import { homedir } from 'node:os';
import { createHash } from 'node:crypto';

export class TtsError extends Error {
  constructor(message, { status = 0, code = 'error' } = {}) {
    super(message);
    this.name = 'TtsError';
    this.status = status;
    this.code = code;
  }
}

export function connectionFilePath(env = process.env) {
  if (env.TTS_API_FILE) return env.TTS_API_FILE;
  const local = env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local');
  return join(local, 'TextToSpeech', 'api.json');
}

/** True unless the process is known to be gone (a process we may not signal still counts as running). */
export function pidAlive(pid) {
  try {
    process.kill(pid, 0);
    return true;
  } catch (e) {
    return e?.code === 'EPERM';
  }
}

/**
 * { baseUrl, token } from TTS_API_URL + TTS_API_TOKEN, or from api.json.
 * api.json is deleted when the app stops cleanly but survives a crash; when it names a pid that is no longer
 * running, the file is stale and we report "not running" instead of a confusing connection error.
 */
/**
 * The check findConnection uses by default. Process ids only mean something on the machine that wrote api.json, so
 * the check runs only where the app runs (Windows); a client in WSL or a container cannot see Windows process ids
 * and would wrongly report a running app as closed.
 */
export const defaultPidCheck = (pid) => process.platform !== 'win32' || pidAlive(pid);

export function findConnection(env = process.env, readFile = readFileSync, isAlive = defaultPidCheck) {
  if (env.TTS_API_URL && env.TTS_API_TOKEN) {
    return { baseUrl: env.TTS_API_URL.replace(/\/+$/, ''), token: env.TTS_API_TOKEN };
  }
  let info;
  try {
    info = JSON.parse(readFile(connectionFilePath(env), 'utf8'));
  } catch {
    throw new TtsError(
      'Text to Speech is not running, or its local API is off. Start the app and switch on "Enable local API" in Settings.',
      { code: 'not_running' });
  }
  const port = info.port ?? info.Port;
  const token = info.token ?? info.Token;
  if (!port || !token) throw new TtsError('api.json is incomplete.', { code: 'not_running' });
  const pid = info.pid ?? info.Pid;
  if (Number.isInteger(pid) && pid > 0 && !isAlive(pid)) {
    throw new TtsError('Text to Speech is not running (api.json is left over from a closed app).', { code: 'not_running' });
  }
  return { baseUrl: `http://127.0.0.1:${port}`, token };
}

/** Calls the API. Returns the parsed JSON body; throws TtsError (with the server's error code) on a failure. */
export async function call(method, path, body, { env = process.env, fetchImpl = fetch, readFile = readFileSync } = {}) {
  const { baseUrl, token } = findConnection(env, readFile);
  let res;
  try {
    res = await fetchImpl(baseUrl + path, {
      method,
      headers: { Authorization: `Bearer ${token}`, Connection: 'close', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (e) {
    throw new TtsError(`Could not reach Text to Speech at ${baseUrl}: ${e.message}`, { code: 'unreachable' });
  }
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* leave null */ }
  if (!res.ok) {
    const err = json?.error;
    throw new TtsError(err?.message ?? `HTTP ${res.status}`, { status: res.status, code: err?.code ?? 'http_error' });
  }
  return json;
}

/**
 * How this Claude Code window names itself to the app's reading arbiter. The id comes from the project folder so the
 * hooks, the MCP server and the CLI of one window agree on it (windows on the same folder share one identity).
 * `pid` is this process's parent (Claude Code or a shell under it), which lets the app find the window it runs in.
 */
export function agentFromContext({ cwd = process.cwd(), pid = process.ppid, label } = {}) {
  const folder = basename(cwd) || 'claude';
  const id = `claude-${createHash('sha1').update(cwd.toLowerCase()).digest('hex').slice(0, 10)}`;
  return { id, label: label ?? folder, pid, titleHint: folder };
}

// Named operations, shared by the CLI and the MCP server.
export const api = {
  status: (o) => call('GET', '/v1/status', undefined, o),
  voices: (o) => call('GET', '/v1/voices', undefined, o),
  speak: (args, o) => call('POST', '/v1/speak', {
    text: args.text,
    source: args.source,
    mode: args.mode,
    applyFilters: args.applyFilters,
    popup: args.popup,
    agent: args.agent,
    questions: args.questions,
    pauses: args.pauses,
    playAll: args.playAll,
  }, o),
  next: (o) => call('POST', '/v1/next', undefined, o),
  // Prewarm: tell the app text you are about to ask it to read, so the audio is made in advance. One hint per agent id;
  // a newer hint replaces it, `speak` with the final text confirms or replaces it, cancelPrewarm withdraws it.
  prewarm: (args, o) => call('POST', '/v1/prewarm', { text: args.text, agent: args.agent, applyFilters: args.applyFilters }, o),
  cancelPrewarm: (agent, o) => call('POST', '/v1/prewarm/cancel', agent ? { agent } : {}, o),
  prewarmStats: (o) => call('GET', '/v1/prewarm', undefined, o),
  answered: (id, sessionId, o) => call('POST', `/v1/pauses/${encodeURIComponent(id)}/answered`, sessionId ? { sessionId } : {}, o),
  policy: (o) => call('GET', '/v1/policy', undefined, o),
  mute: (minutes, o) => call('POST', '/v1/policy/mute', { minutes }, o),
  agents: (o) => call('GET', '/v1/agents', undefined, o),
  registerAgent: (agent, o) => call('POST', '/v1/agents', agent, o),
  promptAgent: (agent, o) => call('POST', '/v1/agents/prompt', agent, o),
  focusAgent: (agent, o) => call('POST', '/v1/agents/focus', agent, o),
  unregisterAgent: (id, o) => call('DELETE', `/v1/agents/${encodeURIComponent(id)}`, undefined, o),
  control: (action, o) => call('POST', `/v1/${action}`, undefined, o),
  jump: (args, o) => call('POST', '/v1/jump', args, o),
  setVoice: (id, o) => call('PUT', '/v1/voice', { id }, o),
  setRate: (rate, o) => call('PUT', '/v1/rate', { rate }, o),
  setVolume: (volume, o) => call('PUT', '/v1/volume', { volume }, o),
  peek: (session, o) => call('GET', `/v1/sessions/${encodeURIComponent(session)}`, undefined, o),
  take: (session, args, o) => call('POST', `/v1/sessions/${encodeURIComponent(session)}/take`, args ?? {}, o),
  replace: (session, args, o) => call('POST', `/v1/sessions/${encodeURIComponent(session)}/replace`, args, o),
  release: (session, args, o) => call('POST', `/v1/sessions/${encodeURIComponent(session)}/release`, args ?? {}, o),
  patch: (session, args, o) => call('PATCH', `/v1/sessions/${encodeURIComponent(session)}`, args, o),
};
