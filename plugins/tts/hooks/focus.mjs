#!/usr/bin/env node
// UserPromptSubmit hook: tells the app the user just typed in this Claude Code window. The app treats it as the focused
// window and (unless turned off in its settings) drops text that was waiting for it, which is old news now. Silent, instant, and does nothing when the app is not running.
import { readFileSync } from 'node:fs';
import { api, agentFromContext } from '../tts-client.mjs';

try {
  const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
  await api.promptAgent(agentFromContext({ cwd: input.cwd ?? process.cwd() }));
} catch { /* ignore */ }
process.exit(0);
