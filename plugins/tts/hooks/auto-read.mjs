#!/usr/bin/env node
// Stop hook. Does nothing unless enabled with the environment variable TTS_AUTO_READ:
//   TTS_AUTO_READ=reply     read Claude's last reply aloud (plain prose, trimmed)
//   TTS_AUTO_READ=announce  just say "Claude Code finished"
// Never blocks Claude and never prints errors (the app may simply not be running).
import { readFileSync } from 'node:fs';
import { api } from '../tts-client.mjs';
import { toSpeakable, lastAssistantText } from '../lib/speakable.mjs';

const mode = (process.env.TTS_AUTO_READ ?? '').toLowerCase();
if (mode !== 'reply' && mode !== 'announce') process.exit(0);
try {
  let text = 'Claude Code finished';
  if (mode === 'reply') {
    const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
    // Newer Claude Code passes the reply itself; older versions only a transcript path.
    const reply = typeof input.last_assistant_message === 'string' && input.last_assistant_message.trim()
      ? input.last_assistant_message
      : lastAssistantText(readFileSync(input.transcript_path, 'utf8'));
    const spoken = toSpeakable(reply);
    if (spoken) text = spoken;
  }
  await api.speak({ text, source: 'claude-code', mode: mode === 'reply' ? 'queue' : 'if-idle', popup: false });
} catch { /* app not running: stay silent */ }
process.exit(0);
