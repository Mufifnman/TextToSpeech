#!/usr/bin/env node
// MCP server (stdio) for the Text to Speech local API, so Claude can read text aloud, control reading, and hand the
// unread remainder back and forth. Newline-delimited JSON-RPC, no dependencies.
import { createInterface } from 'node:readline';
import { api, agentFromContext, TtsError } from '../tts-client.mjs';

const SERVER = { name: 'text-to-speech', version: '1.0.0' };

const str = (description) => ({ type: 'string', description });
const num = (description) => ({ type: 'number', description });

export const TOOLS = [
  { name: 'speak', description: 'Read text aloud with the Text to Speech app. Use mode "if-idle" for short announcements that should not interrupt anything. Several Claude windows take turns: the answer says decision "speaking", "waiting" (another window is reading; this text is read when it is done or when the user switches to this window), "skipped" or "denied" (the user\'s rules, such as headphones or quiet hours, say no: do not retry).',
    inputSchema: { type: 'object', required: ['text'], properties: {
      text: str('The text to read.'),
      mode: { type: 'string', enum: ['queue', 'interrupt', 'if-idle'], description: 'queue (default), interrupt (speak now, keep the rest for later) or if-idle (skip if anything is playing or waiting).' },
      source: str('Label shown in the popup. Defaults to "claude".'),
      quiet: { type: 'boolean', description: 'true reads without showing the reading popup.' },
      questions: { type: 'array', items: { type: 'string' }, description: 'Questions for the user, in the order to ask them. They are added after the text as a numbered list and reading stops after each one so the user can answer before the next is read. The app never decides what is a question: pass them here. Markdown in text (headings, lists) is laid out in the popup and the markup is never spoken.' },
      playAll: { type: 'boolean', description: 'true reads through the questions without stopping (the user can also press Play all).' },
    } },
    run: (a) => api.speak({ text: a.text, source: a.source ?? 'claude', mode: a.mode, popup: a.quiet ? false : undefined, agent: agentFromContext(), questions: a.questions, playAll: a.playAll }) },
  { name: 'next_question', description: 'Continue reading after a question the app is waiting at (marks it answered). Use it when the user has already answered by typing, so the next question is read. Fails if reading is not waiting at a question; status shows state "paused_at_pause" and pausedAt when it is.',
    inputSchema: { type: 'object', properties: {} }, run: () => api.next() },
  { name: 'mark_answered', description: 'Mark a question answered by its id (q1, q2, ... in the order given to speak). Reading will not stop there; if reading is waiting at it, it continues.',
    inputSchema: { type: 'object', required: ['id'], properties: { id: str('Question id, such as q1.'), session: str('Session id, only when two sessions have the same id.') } },
    run: (a) => api.answered(a.id, a.session) },
  { name: 'reading_policy', description: 'Ask whether reading aloud is allowed right now and why not (headphones, quiet hours, switched off, muted), plus which Claude windows are reading or waiting. Check this before reading long text.',
    inputSchema: { type: 'object', properties: {} },
    run: async () => ({ policy: await api.policy(), agents: (await api.agents()).agents }) },
  { name: 'mute_reading', description: 'Silence all reading for some minutes (1 to 720) when the user asks you to be quiet. You cannot switch reading back on or loosen the rules; the user does that in the app.',
    inputSchema: { type: 'object', required: ['minutes'], properties: { minutes: num('Minutes to stay quiet.') } }, run: (a) => api.mute(a.minutes) },
  { name: 'pause', description: 'Pause reading (keeps the place).', inputSchema: { type: 'object', properties: {} }, run: () => api.control('pause') },
  { name: 'resume', description: 'Resume reading.', inputSchema: { type: 'object', properties: {} }, run: () => api.control('resume') },
  { name: 'skip', description: 'Skip to the next sentence.', inputSchema: { type: 'object', properties: {} }, run: () => api.control('skip') },
  { name: 'back', description: 'Restart the current sentence, or go back one.', inputSchema: { type: 'object', properties: {} }, run: () => api.control('back') },
  { name: 'stop', description: 'Stop reading (keeps the place).', inputSchema: { type: 'object', properties: {} }, run: () => api.control('stop') },
  { name: 'clear', description: 'Clear everything queued to read.', inputSchema: { type: 'object', properties: {} }, run: () => api.control('clear') },
  { name: 'status', description: 'What is being read, the position, the queue, voice, speed and volume.', inputSchema: { type: 'object', properties: {} }, run: () => api.status() },
  { name: 'list_voices', description: 'The installed voices.', inputSchema: { type: 'object', properties: {} }, run: () => api.voices() },
  { name: 'set_voice', description: 'Choose the voice by id (see list_voices).', inputSchema: { type: 'object', required: ['id'], properties: { id: str('Voice id.') } }, run: (a) => api.setVoice(a.id) },
  { name: 'set_rate', description: 'Speaking speed, -10 (slow) to 10 (fast); the app uses -2 to 10.', inputSchema: { type: 'object', required: ['rate'], properties: { rate: num('Speed.') } }, run: (a) => api.setRate(a.rate) },
  { name: 'set_volume', description: 'Reading volume, 0 to 120 percent.', inputSchema: { type: 'object', required: ['volume'], properties: { volume: num('Percent.') } }, run: (a) => api.setVolume(a.volume) },
  { name: 'take_remainder', description: 'Stop and take back the text that has not been read yet. Returns the part already read, the unread remainder, the position, and a handoffId. Edit the remainder and give it back with replace_remainder (or call release_remainder to undo). If it is not returned within the lease (default 120 s) the original comes back paused.',
    inputSchema: { type: 'object', properties: {
      session: str('Session id from speak, or "current" (default).'),
      from: { type: 'string', enum: ['word', 'sentence'], description: 'Start the remainder at the word being spoken (default) or at its sentence start.' },
      leaseSeconds: num('How long to hold it before it is restored (1 to 3600).'),
    } },
    run: (a) => api.take(a.session ?? 'current', { from: a.from, leaseSeconds: a.leaseSeconds }) },
  { name: 'replace_remainder', description: 'Give back modified text for what take_remainder took. Reading continues with the new text (from startAt, a character offset into it).',
    inputSchema: { type: 'object', required: ['session', 'handoffId', 'text'], properties: {
      session: str('Session id.'), handoffId: str('From take_remainder.'), text: str('The new remainder text (empty drops it).'),
      startAt: num('Character offset in the new text to start from. Default 0.'),
      placement: { type: 'string', enum: ['front', 'end'], description: 'front (default): next to read. end: after everything queued.' },
      autoResume: { type: 'boolean', description: 'false leaves it paused.' },
    } },
    run: (a) => api.replace(a.session, { handoffId: a.handoffId, text: a.text, startAt: a.startAt, placement: a.placement, autoResume: a.autoResume }) },
  { name: 'release_remainder', description: 'Cancel a take_remainder: the original unread text is put back and reading resumes.',
    inputSchema: { type: 'object', required: ['session'], properties: { session: str('Session id.'), handoffId: str('Optional; defaults to the open handoff.') } },
    run: (a) => api.release(a.session, { handoffId: a.handoffId }) },
  { name: 'peek_session', description: 'See a session\'s read and unread text without changing anything.', inputSchema: { type: 'object', properties: { session: str('Session id or "current".') } }, run: (a) => api.peek(a.session ?? 'current') },
];

const byName = new Map(TOOLS.map((t) => [t.name, t]));

export async function handle(msg) {
  const { id, method, params } = msg;
  const reply = (result) => ({ jsonrpc: '2.0', id, result });
  const fail = (code, message) => ({ jsonrpc: '2.0', id, error: { code, message } });

  switch (method) {
    case 'initialize':
      return reply({ protocolVersion: params?.protocolVersion ?? '2024-11-05', capabilities: { tools: {} }, serverInfo: SERVER });
    case 'ping':
      return reply({});
    case 'tools/list':
      return reply({ tools: TOOLS.map(({ name, description, inputSchema }) => ({ name, description, inputSchema })) });
    case 'tools/call': {
      const tool = byName.get(params?.name);
      if (!tool) return fail(-32602, `Unknown tool ${params?.name}`);
      try {
        const result = await tool.run(params.arguments ?? {});
        return reply({ content: [{ type: 'text', text: JSON.stringify(result, null, 2) }] });
      } catch (e) {
        const text = e instanceof TtsError ? `${e.message}${e.code ? ` (${e.code})` : ''}` : String(e);
        return reply({ isError: true, content: [{ type: 'text', text }] });
      }
    }
    default:
      // notifications (no id) get no reply
      return id === undefined ? null : fail(-32601, `Method not found: ${method}`);
  }
}

if (process.argv[1]?.endsWith('tts-mcp.mjs')) {
  const rl = createInterface({ input: process.stdin });
  rl.on('line', async (line) => {
    if (!line.trim()) return;
    let msg;
    try { msg = JSON.parse(line); } catch { return; }
    const response = await handle(msg);
    if (response) process.stdout.write(JSON.stringify(response) + '\n');
  });
}
