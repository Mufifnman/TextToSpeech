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
| Stop hook (off by default) | Reads each reply aloud when it finishes |

## Auto-read replies (optional, off by default)

Set an environment variable before starting Claude Code (or in `env` in your Claude Code settings):

- `TTS_AUTO_READ=reply` reads every reply aloud (code, tables and links removed, trimmed to about 1500 characters).
- `TTS_AUTO_READ=announce` only says "Claude Code finished" (never cuts off current reading).

Unset, the hook does nothing. If the app is not running the hook stays silent.

## Notes

- `tts-client.mjs`, `cli/` and `mcp/` here are copies of `integrations/`, because an installed plugin only gets its own folder. A test fails if they drift; copy them again after changing `integrations/`.
- Tests: `node --test "plugins/tts/test/*.test.mjs"`.
