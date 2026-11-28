#!/usr/bin/env node
// UserPromptSubmit hook: tells the app the user just typed in this Claude Code window. The app treats it as the focused
// window and (unless turned off in its settings) drops text that was waiting for it, which is old news now.
// Silent, quick, and does nothing when the app is not running. Exits by itself (no process.exit(), see auto-read.mjs).
import { readFileSync } from 'node:fs';
import { api, agentFromContext } from '../tts-client.mjs';

try {
  const input = JSON.parse(readFileSync(0, 'utf8') || '{}');
  const agent = agentFromContext({ cwd: input.cwd ?? process.cwd() });
  await api.promptAgent(agent);
  // Voice answers nobody collected (the turn had ended before the user answered): hand them over with this prompt.
  const { replies } = await api.takeReplies({ source: agent.label, waitSeconds: 0 });
  if (replies?.length) {
    const lines = replies.map((r) => `- ${r.question ? `Question "${r.question}": ` : ''}${r.text}`);
    console.log(JSON.stringify({ hookSpecificOutput: { hookEventName: 'UserPromptSubmit',
      additionalContext: `The user also answered by voice (Text to Speech reply box, transcribed):\n${lines.join('\n')}` } }));
  }
} catch { /* ignore */ }
process.exitCode = 0;
