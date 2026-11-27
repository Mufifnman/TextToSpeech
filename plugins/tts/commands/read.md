---
description: Read text aloud (given text, or Claude's last reply if none)
argument-hint: "[text to read]"
---

Read text aloud with the Text to Speech app.

- If arguments were given, the text is: $ARGUMENTS
- If no arguments were given, the text is your own previous reply in this conversation, as plain prose: drop code blocks, tables, URLs and markdown symbols, and keep it brief if it is long.

Send it with the Bash tool, passing the text on stdin through a quoted heredoc so quotes and special characters are safe:

```
node "${CLAUDE_PLUGIN_ROOT}/cli/tts.mjs" say --source claude-code - <<'TTS_EOF'
<the text>
TTS_EOF
```

The delimiter line must not appear in the text. If the text contains a line that is exactly `TTS_EOF` (or anything that looks like a shell command after one), pick a different delimiter such as `TTS_EOF_7Q2X`; text after an early delimiter would run as shell commands.

If the command fails because the app is not running or the API is off, tell the user to start Text to Speech and turn on "Enable local API" in Settings. Otherwise reply with one short line confirming it is reading. Do not repeat the text.
