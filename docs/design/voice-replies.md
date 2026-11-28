# Voice replies back to the calling app

## Goal record

**Goal.** A program that asks the user a question through the app can get the answer back. The user answers by voice
without typing and without the app typing into any other window. Claude Code (the `tts` plugin) is the first caller, but
nothing in the API is Claude Code specific: any caller says it expects a reply, and the app hands the reply to that caller.

**Decisions Kevin made (grill-me, 2026-11-28).**
- Replies go back to the calling app through the API, not by typing into windows.
- Dictation stays Handy (local Whisper, push to talk) for now; no built-in recognizer in the first version.
- Clipboard handoff was dropped (it does not work well). The first version uses a reply box in the popup that takes the
  keyboard focus, so Handy pastes into it. It is fine that the box takes focus.
- One hotkey (default Ctrl+Alt+V) opens the box and starts Handy's recording with `handy --toggle-transcription`; the same
  key again stops the recording and Handy pastes into the box. Fallback if Handy is not found: press Handy's own key.
- Each question that is read is a pause point passed in by the caller. The app never guesses questions. Expiry, queueing
  and pause behaviour stay as in the arbitration and question-pause designs.
- Settings and speed work live inside the app; callers send whole text.
- Enter sends the reply; "Send voice replies automatically" is a setting (off by default; 2 seconds after the words stop).

**Boundaries.** The app never injects keystrokes into another window. The microphone is Handy's, so Handy's own indicator
shows it. Replies are in memory only (at most 50, until taken). Answers go only to the caller whose `source` matches.

**Non-goals (later).** A built-in recognizer (sherpa-onnx with Whisper or Parakeet models, Apache-2.0, has .NET support;
Handy itself is MIT but is a Rust/Tauri app, not a library); a key that captures a reply without ever showing the app
(the earlier option c); an iPhone companion; replying to a question before it is read.

**Open questions.** Where Handy.exe lives on Kevin's PC (looked up in the usual places, or `HandyPath` in settings); whether
Handy's `--toggle-transcription` works as hoped while another app has focus (to be tried on his PC); the popup takes focus
and gives it back to the window that had it when the box closes.

## API

`POST /v1/speak` gets `expectsReply` (needs `questions` or `pauses`, else 400 `reply_needs_pause`).

While reading waits at such a question, `GET /v1/status` has
`replyWaiting: { sessionId, source, pauseId, question, number, of }`.

- `POST /v1/replies` `{ "text": "...", "sessionId"?: "...", "pauseId"?: "q2" }` gives an answer. Without `pauseId` it answers
  the question reading is waiting at (409 `no_reply_expected` if none); with it, a later question that has not been read
  yet. The question is marked answered and reading carries on. 202 with the reply.
- `POST /v1/replies/take` `{ "source"?: "...", "sessionId"?: "...", "waitSeconds"?: 0..120 }` returns and removes the
  matching replies (`{ replies: [ { seq, sessionId, source, pauseId, question, text, at } ] }`); it waits up to
  `waitSeconds` for one.
- `GET /v1/replies` lists the replies not yet taken, without removing them.
- The SSE stream sends a `reply` event (detail: the pause id).

`source` is the label the text was spoken under (for agents, the window's label), so two windows never get each other's answers.

## Plugin

- MCP `speak` takes `expectsReply`; new tool `wait_for_reply` (up to 120 s per call, call again to keep waiting).
- The auto-read hook passes `expectsReply` with the `tts-questions` block. The turn is over by then, so the answer waits;
  the UserPromptSubmit hook adds waiting voice answers to the next prompt as context.
- CLI: `tts say --expects-reply ...`, `tts reply <text>`, `tts replies [--source s] [--wait n]`.

## App

`ReadingHost` keeps the replies and which sessions expect them. The popup has the reply box (`OpenReply` takes the
keyboard focus and `CloseReply` gives it back); `PopupPolicy.KeepForReply` keeps the popup up while it is open; the
hotkey is `HotkeySettings.ReplyByVoice`; Handy is started with `--toggle-transcription` (path from `HandyPath` or the usual
install folders).
