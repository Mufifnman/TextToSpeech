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
| UserPromptSubmit hook | Tells the app this window is the one you are using (moves its waiting text up) |

## Several windows, one voice

Every Claude Code window (identified by its project folder) is an "agent" of the app. Agents take turns: while one is
reading, text from another waits in the app and is read when the first is done, or sooner when you switch to that
window (the plugin tells the app when you submit a prompt there, and the app also notices window focus). The app
decides whether reading is allowed at all, with rules you set in **Settings, Agents (Claude Code) reading rules**:
allow agents, headphones only, quiet hours, what switching windows does, and whether the reading window is brought to
the front (off by default). Claude can ask with the `reading_policy` tool, and can mute itself with `mute_reading`,
but cannot loosen your rules.

## Auto-read replies (off by default)

Choose **Auto-read replies** in the same settings section: off, "Say that it finished", or "Read the reply" (code,
tables and links removed, trimmed to about 1500 characters). For one window only, the environment variable
`TTS_AUTO_READ=off|announce|reply` (before starting Claude Code, or in `env` in your Claude Code settings) overrides it.
If the app is not running, or the rules say no, the hook stays silent.

## Notes

- `tts-client.mjs`, `cli/` and `mcp/` here are copies of `integrations/`, because an installed plugin only gets its own folder. A test fails if they drift; copy them again after changing `integrations/`.
- Tests: `node --test "plugins/tts/test/*.test.mjs"`.
