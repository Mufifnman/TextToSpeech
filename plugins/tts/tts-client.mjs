// Tiny client for the Text to Speech local API (see TextToSpeech.Api). No dependencies; needs Node 18 or newer.
//
// It finds the running app through api.json (port, token), which the app writes while the local API is enabled.
// The token changes every time the app starts, so it is read again for every call.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { homedir } from 'node:os';

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

/** { baseUrl, token } from TTS_API_URL + TTS_API_TOKEN, or from api.json. */
export function findConnection(env = process.env, readFile = readFileSync) {
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
  return { baseUrl: `http://127.0.0.1:${port}`, token };
}

/** Calls the API. Returns the parsed JSON body; throws TtsError (with the server's error code) on a failure. */
export async function call(method, path, body, { env = process.env, fetchImpl = fetch, readFile = readFileSync } = {}) {
  const { baseUrl, token } = findConnection(env, readFile);
  let res;
  try {
    res = await fetchImpl(baseUrl + path, {
      method,
      headers: { Authorization: `Bearer ${token}`, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
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
  }, o),
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
