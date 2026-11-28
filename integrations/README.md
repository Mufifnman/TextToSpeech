# Using Text to Speech from other programs

Everything here talks to the app's local API (127.0.0.1 only, token protected). Switch it on first:
**Settings, Tray and background, "Enable local API"**. While it is on, the app writes
`%LocalAppData%\TextToSpeech\api.json` with the port and a token; the token changes every time the app starts, so these
tools read the file on every call. Needs Node 18 or newer, nothing to install.

## Command line

```
node integrations/cli/tts.mjs say "Build finished"
node integrations/cli/tts.mjs say --mode if-idle --quiet "Tests passed"
echo "Long text..." | node integrations/cli/tts.mjs say -
node integrations/cli/tts.mjs status
node integrations/cli/tts.mjs take current            # unread text + handoffId
node integrations/cli/tts.mjs replace <session> --handoff <id> --text "New rest of the text"
```

`tts.mjs` with no arguments prints every command.

## Claude Code hook (announce when work finishes)

Copy the `hooks` from `claude-code/settings.example.json` into your Claude Code settings and fix the path. `Stop` says
"Claude Code finished" when Claude is done; `Notification` says when it needs you. `--mode if-idle` means it never
cuts off something you are listening to.

## Claude Code MCP server (read aloud, hand back the rest)

```
claude mcp add text-to-speech -- node C:/Git/TextToSpeech/integrations/mcp/tts-mcp.mjs
```

Tools: `speak`, `pause`, `resume`, `skip`, `back`, `stop`, `clear`, `status`, `list_voices`, `set_voice`, `set_rate`,
`set_volume`, `take_remainder`, `replace_remainder`, `release_remainder`, `peek_session`.

## Environment variables

| Variable | Meaning |
|---|---|
| `TTS_API_FILE` | Path to a different `api.json`. |
| `TTS_API_URL` and `TTS_API_TOKEN` | Skip the file and connect to this address (for testing). |

## Tests

`node --test "integrations/test/*.test.mjs"` (uses a fake local server; the app does not need to run).

## Claude Code plugin

`plugins/tts` packages the above (slash commands, a read-aloud skill, the MCP server and an optional auto-read hook) as an installable Claude Code plugin. See `plugins/tts/README.md`.
