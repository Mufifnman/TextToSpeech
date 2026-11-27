#!/usr/bin/env node
// UserPromptSubmit hook: tells the app this Claude Code window is the one the user is working in, so text waiting for
// it moves to the front of the line. Silent, instant, and does nothing when the app is not running.
import { readFileSync } from 'node:fs';
import { api, agentFromContext } from '../tts-client.mjs';

try {
  const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
  await api.focusAgent(agentFromContext({ cwd: input.cwd ?? process.cwd() }));
} catch { /* ignore */ }
process.exit(0);
