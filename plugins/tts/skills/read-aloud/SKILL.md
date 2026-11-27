---
name: read-aloud
description: Read text aloud through the user's Text to Speech app. Use when the user asks you to read something to them, read your answer aloud, speak or announce something, or to pause, stop, skip, or change the voice, speed or volume of reading.
---

# Read aloud

The `text-to-speech` MCP server (from this plugin) talks to the user's Text to Speech app. Prefer its tools; if they are unavailable, use `node "${CLAUDE_PLUGIN_ROOT}/cli/tts.mjs"` (`tts.mjs` with no arguments lists commands).

- **Read text**: call `speak` with `text`. Send plain prose: remove code blocks, tables, URLs, file paths and markdown symbols; summarize long or technical material instead of reading it verbatim unless the user asked for the exact text. Use `mode: "if-idle"` for short announcements so you never cut off something they are listening to; the default queues.
- **Read a file or long text**: read it, then pass it in chunks of a few paragraphs to `speak` (queue mode).
- **Controls**: `pause`, `resume`, `skip`, `back`, `stop`, `clear`, `status`, `list_voices`, `set_voice`, `set_rate` (-10 to 10), `set_volume` (0 to 120).
- **Edit the unread part**: `take_remainder`, change the text, `replace_remainder` (or `release_remainder` to undo).

Several Claude windows share the voice. `speak` answers with a decision: `speaking`, `waiting` (another window is reading; it will be read afterwards or when the user switches here, so don't resend), `skipped`, or `denied` (the user's rules say no, e.g. headphones off or quiet hours: don't retry or work around it). Use `reading_policy` to check first and `mute_reading` if the user asks you to be quiet for a while. You cannot loosen their rules.

If a call fails with "not running", tell the user to start the app and enable "Enable local API" in Settings. Don't retry in a loop. Keep your chat reply short; don't repeat the text you sent to be read.
