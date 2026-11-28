#!/usr/bin/env node
// Stop hook. What it reads comes from the app's agent settings (Agent reading rules window, Auto-read replies):
// off, announce ("Claude Code finished") or reply (Claude's last reply as plain prose, trimmed).
// The environment variable TTS_AUTO_READ=off|announce|reply overrides the app for this window.
// The app decides whether reading is allowed now (headphones, quiet hours, switched off) and in whose turn; several
// windows never overlap, a window that has to wait is read when the others are done or when you switch to it.
// Never blocks Claude and never prints errors (the app may simply not be running).
//
// It ends by letting Node exit on its own, not with process.exit(): on Windows, process.exit() while a network socket
// is still closing aborts Node (exit code 0xC0000409). The client sends "Connection: close" so nothing stays open.
import { readFileSync } from 'node:fs';
import { api, agentFromContext } from '../tts-client.mjs';
import { toSpeakable, lastAssistantText } from '../lib/speakable.mjs';

async function main() {
  const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
  const agent = agentFromContext({ cwd: input.cwd ?? process.cwd() });
  const policy = await api.policy();
  const env = (process.env.TTS_AUTO_READ ?? '').toLowerCase();
  const mode = ['off', 'announce', 'reply'].includes(env) ? env : (policy.policy?.autoRead ?? 'off');
  if (mode === 'off' || !policy.mayRead) return;

  let text = 'Claude Code finished';
  if (mode === 'reply') {
    // Newer Claude Code passes the reply itself; older versions only a transcript path.
    const reply = typeof input.last_assistant_message === 'string' && input.last_assistant_message.trim()
      ? input.last_assistant_message
      : lastAssistantText(readFileSync(input.transcript_path, 'utf8'));
    const spoken = toSpeakable(reply);
    if (spoken) text = spoken;
  }
  await api.speak({ text, source: 'claude-code', mode: mode === 'reply' ? 'queue' : 'if-idle', popup: false, agent });
}

try { await main(); } catch { /* app not running: stay silent */ }
process.exitCode = 0;
