# tts: Claude Code plugin for Text to Speech

Lets Claude Code read text aloud through the Text to Speech app's local API.

## Before you start

1. Run the Text to Speech app and switch on **Settings, Tray and background, "Enable local API"**.
2. Node 18 or newer must be on your PATH (nothing to install).

## Install

In Claude Code:

```
/plugin marketplace add Mufifnman/TextToSpeech
/plugin install tts@text-to-speech
```

Or from a local checkout: `/plugin marketplace add C:/Git/TextToSpeech`. To try without installing:
`claude --plugin-dir C:/Git/TextToSpeech/plugins/tts`.

## What you get

| | |
|---|---|
| `/tts:read [text]` | Read the text aloud; with no text, reads Claude's last reply as plain prose |
| `/tts:pause`, `/tts:resume`, `/tts:skip`, `/tts:stop`, `/tts:status` | Control reading |
| `read-aloud` skill | Lets you just say "read that to me" or "read this file aloud" |
| `text-to-speech` MCP server | Tools `speak`, `pause`, `set_voice`, `set_rate`, `take_remainder` and more (see `integrations/README.md`) |
| Stop hook (off by default) | Reads each reply aloud when it finishes, if the app's rules allow |
| UserPromptSubmit hook | Tells the app you typed in this window (focus; drops its stale waiting text) |

## Several windows, one voice

Every Claude Code window (identified by its project folder) is an "agent" of the app. Agents take turns: while one is
reading, text from another waits in the app and is read when the first is done, or sooner when you switch to that
window (the plugin tells the app when you submit a prompt there, and the app also notices window focus). Text still waiting for a window is dropped when you type in that window, and optionally after a set time (default 4 hours). The app
decides whether reading is allowed at all, with rules you set in **Settings, Agent reading rules...** (its own window):
allow agents, headphones only, quiet hours, what switching windows does, and whether the reading window is brought to
the front (off by default). Claude can ask with the `reading_policy` tool, and can mute itself with `mute_reading`,
but cannot loosen your rules.

## Questions that wait for your answer

Give Claude's questions to the app instead of leaving them buried in the reply, and reading stops after each one so you can
answer (for example by dictating into the window) before the next is read. The app never decides what is a question: the
caller passes them in.

- MCP `speak` takes `questions` (a list); CLI: `tts say --question "Which branch?" --question "Add tests?" "Done with the parser."`.
- With the auto-read hook, Claude ends a reply with a fenced block that has the info string `tts-questions`, one question per
  line. The hook takes the block out of the text and passes it as the questions, and shows the popup.
- Reading stops after each question (it shows as a numbered list, read as "One. ..."). Continue with Play in the popup or
  window, or from Claude: `next_question` (continue), `mark_answered` with the id `q1`, `q2`, ... (skip a question you
  already answered). The Play all button in the popup and window reads straight through without stopping, for the current
  text; Settings can make that the default.

Paste this into your CLAUDE.md so Claude does it every time:

````
When you finish a reply and need answers from me, end the reply with a fenced block tagged tts-questions, one question
per line, in the order to ask (no other text in the block). Use headings and lists in the reply freely; they are laid out in the reading popup
and never spoken. Keep each question short enough to be understood when heard on its own.
````

## Auto-read replies (off by default)

Choose **Auto-read replies** in that window: off, "Say that it finished", or "Read the reply" (code,
tables and links removed, trimmed to about 1500 characters; headings and lists kept, since the app shows them laid out). For one window only, the environment variable
`TTS_AUTO_READ=off|announce|reply` (before starting Claude Code, or in `env` in your Claude Code settings) overrides it.
If the app is not running, or the rules say no, the hook stays silent.

## Notes

- `tts-client.mjs`, `cli/` and `mcp/` here are copies of `integrations/`, because an installed plugin only gets its own folder. A test fails if they drift; copy them again after changing `integrations/`.
- Tests: `node --test "plugins/tts/test/*.test.mjs"`.

## Prewarm (experimental proof of concept)

Tools `prewarm_reading`, `cancel_prewarm` and `prewarm_stats` (and `tts prewarm`, `prewarm-cancel`, `prewarm-stats` on the CLI) tell the app text that will probably be read soon so it makes the audio in advance. One hint per window: a newer hint replaces it; `speak` with the final text confirms it (same sentences) or replaces it (different text, shared sentences are reused); `cancel_prewarm` withdraws it; an unread hint is dropped after 2 minutes. `prewarm_stats` shows how each hint ended and how often audio was ready in time, so we can tell whether it helps. A reply is only known once it is written, so this is mainly useful when text is ready a little before it is spoken.
