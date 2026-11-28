#!/usr/bin/env node
// tts: command line for the Text to Speech local API.   node tts.mjs <command> [options]
import { readFileSync } from 'node:fs';
import { api, TtsError } from '../tts-client.mjs';

const USAGE = `Usage: tts <command> [options]

  say <text|->        Read text aloud ("-" reads stdin).
                        --mode queue|interrupt|if-idle   (default queue)
                        --source <name>                  label shown in the popup (default "cli")
                        --quiet                          no reading popup
                        --filters                        run the app's text filters
                        --question <text>                a question to wait on after the text (repeat for several);
                                                         reading stops after each until you press play or run "next"
                        --play-all                       read through the questions without stopping
  prewarm <text|-> [--agent <id>] [--filters]   Tell the app text that will probably be read soon, so it makes the audio now.
  prewarm-cancel [--agent <id>]                 Withdraw the hint.   prewarm-stats   Hints and how often audio was ready.
  next                Continue after a question the app is waiting at (marks it answered).
  answered <id> [--session <id>]   Mark a question answered (q1, q2, ...); continues if reading waits at it.
  pause | resume | toggle | skip | back | stop | clear
  status | voices
  voice <id> | rate <-10..10> | volume <0..120>
  peek <session|current>
  take <session|current> [--from word|sentence] [--lease <seconds>] [--if-revision <n>]
  replace <session> --handoff <id> (--text <text> | --file <path>) [--start-at <n>] [--placement front|end] [--no-resume]
  release <session> [--handoff <id>]

Prints JSON. Exit code 0 on success, 1 on an error (message on stderr).`;

function parse(argv) {
  const positional = [];
  const flags = {};
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const name = a.slice(2);
      if (['quiet', 'filters', 'no-resume', 'play-all'].includes(name)) flags[name] = true;
      else if (name === 'question') (flags.question ??= []).push(argv[++i]);
      else flags[name] = argv[++i];
    } else positional.push(a);
  }
  return { positional, flags };
}

const num = (v) => (v === undefined ? undefined : Number(v));

export async function run(argv, { stdin = () => readFileSync(0, 'utf8'), out = console.log } = {}) {
  const { positional, flags } = parse(argv);
  const [command, arg] = positional;
  const print = (v) => out(JSON.stringify(v, null, 2));

  switch (command) {
    case 'say': {
      const text = arg === '-' ? stdin() : positional.slice(1).join(' ');
      if (!text.trim()) throw new TtsError('Nothing to say.', { code: 'usage' });
      return print(await api.speak({
        text, source: flags.source ?? 'cli', mode: flags.mode,
        applyFilters: flags.filters ? true : undefined, popup: flags.quiet ? false : undefined,
        questions: flags.question, playAll: flags['play-all'] ? true : undefined,
      }));
    }
    case 'pause': case 'resume': case 'toggle': case 'skip': case 'back': case 'stop': case 'clear':
      return print(await api.control(command));
    case 'next': return print(await api.next());
    case 'prewarm': {
      const text = arg === '-' ? stdin() : positional.slice(1).join(' ');
      if (!text.trim()) throw new TtsError('Nothing to prewarm.', { code: 'usage' });
      return print(await api.prewarm({ text, agent: flags.agent ? { id: flags.agent } : undefined, applyFilters: flags.filters ? true : undefined }));
    }
    case 'prewarm-cancel': return print(await api.cancelPrewarm(flags.agent ? { id: flags.agent } : undefined));
    case 'prewarm-stats': return print(await api.prewarmStats());
    case 'answered':
      if (!arg) throw new TtsError('answered needs a pause id such as q1.', { code: 'usage' });
      return print(await api.answered(arg, flags.session));
    case 'status': return print(await api.status());
    case 'voices': return print(await api.voices());
    case 'voice': return print(await api.setVoice(arg));
    case 'rate': return print(await api.setRate(num(arg)));
    case 'volume': return print(await api.setVolume(num(arg)));
    case 'peek': return print(await api.peek(arg ?? 'current'));
    case 'take':
      return print(await api.take(arg ?? 'current', {
        from: flags.from, leaseSeconds: num(flags.lease), ifRevision: num(flags['if-revision']),
      }));
    case 'replace': {
      const text = flags.file ? readFileSync(flags.file, 'utf8') : flags.text;
      if (text === undefined || !flags.handoff) throw new TtsError('replace needs --handoff and --text or --file.', { code: 'usage' });
      return print(await api.replace(arg ?? 'current', {
        handoffId: flags.handoff, text, startAt: num(flags['start-at']), placement: flags.placement,
        autoResume: flags['no-resume'] ? false : undefined,
      }));
    }
    case 'release': return print(await api.release(arg ?? 'current', { handoffId: flags.handoff }));
    default:
      throw new TtsError(USAGE, { code: 'usage' });
  }
}

if (import.meta.url === `file://${process.argv[1].replace(/\\/g, '/')}` || process.argv[1]?.endsWith('tts.mjs')) {
  run(process.argv.slice(2)).catch((e) => {
    console.error(e instanceof TtsError ? e.message : e);
    process.exit(1);
  });
}
