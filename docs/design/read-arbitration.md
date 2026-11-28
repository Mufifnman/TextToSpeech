# Several agents, one voice: arbitration, reading rules and replying by voice (design)

Builds on the local API and always-on design (`local-api-and-always-on.md` in the project files) and the Claude Code plugin.

## 1. What this adds

1. **Turn taking.** Each Claude Code window (or any API client) is an *agent*. One agent holds the turn; others' text waits in the app and is read afterwards. No overlap, nothing lost.
2. **Reading rules** (policy) owned by the user, in their own window (Settings, "Agent reading rules..."), not in the general settings: allow agents at all, headphones only, quiet hours, auto-read mode, what switching windows does, bring the reading window to the front. Agents can read the rules and can only make them stricter (a timed mute).
3. **Focus promotion.** Switching to a window with waiting text moves it to the front of the line (`next`) or interrupts (`now`).
4. **Bring to front** (opt in): when an agent's turn starts, its window is raised so the text can be seen and the keyboard is where the reply goes.
5. **Reply by voice**: designed here, built in a follow-up PR (section 6).

## 2. Model

- Agent = `{ id, label, pid?, titleHint?, priority? }`. The plugin derives `id` from the project folder (stable across hooks and MCP), `pid` from its parent process, `titleHint` from the folder name. Agents are created on first contact (speak, register or focus) and forgotten after 6 h of silence with nothing waiting.
- `POST /v1/speak` gains an optional `agent`. Without it, behavior is exactly as before (window, hotkeys, plain scripts are not arbitrated). With it: policy check, then turn taking. Response adds `decision`: `speaking`, `waiting` (with `queuePosition`, `heldBy`), `skipped` (`if-idle` while another agent reads) or `denied` (with `reason`).
- Waiting text is kept per agent inside the app (default max 5 items per agent, oldest dropped), not in the reading list, so the user's own text and the holder's text are never interleaved with it. Waiting text is dropped as stale in two ways: when the user submits a prompt in that agent's window (`POST /v1/agents/prompt`, called by the plugin's `UserPromptSubmit` hook; setting "Drop waiting text when I type in that window", on by default), and optionally after a configurable time (default 4 hours, can be turned off). It is held in memory only (an app restart loses it), and the policy is checked again when it comes due, not only when it was submitted.
- The turn ends when the reading list runs dry (`finished`). Next agent: window in focus first, then higher priority, then longest waiting. A holder whose reading ended without notice (queue cleared, app restarted) is replaced by the next speaker.
- `focusMode`: `off`; `next` (focus only reorders the line, and starts reading at once if nothing is playing); `now` (the focused agent interrupts: its first text is spoken with `interrupt`, the interrupted remainder stays in the list and is read after, as with any interrupt).
- Clearing the reading list also clears waiting text.

New endpoints (all under the existing token): `GET /v1/policy` (rules plus `mayRead` and reasons), `POST /v1/policy/mute {minutes 1-720}`, `GET|POST /v1/agents`, `POST /v1/agents/focus`, `POST /v1/agents/prompt`, `DELETE /v1/agents/{id}`. The rules themselves cannot be changed over the API.

## 3. Reading rules

| Rule | How it is decided |
|---|---|
| Allow agents | Setting. |
| Headphones only | Default Windows output device (Multimedia role) form factor, via NAudio `MMDevice` property `PKEY_AudioEndpoint_FormFactor`: 3 headphones and 5 headset count; speakers, HDMI and line out do not; unknown denies. Bluetooth earbuds normally report as headphones or headset, but some drivers report them as speakers: if that happens for Kevin's pair, the rule needs a device allow list (follow-up). |
| Quiet hours | Local time, `HH:mm` to `HH:mm`, may cross midnight. |
| Mute | Set by an agent on the user's request or from the app; only extends. |
| Expiry | Optional, default 4 hours, plus drop-on-prompt (section 2). |
| Auto-read | `off`, `announce`, `reply`. The Stop hook asks the app; `TTS_AUTO_READ` overrides per window. |

The policy is evaluated in the app on every agent speak, so the agent and the app can never disagree; the agent can also ask first (`reading_policy` tool) to avoid composing text that will be dropped.

## 4. Which window is focused

Two signals, both best effort:

1. **Explicit (reliable):** the plugin's `UserPromptSubmit` hook calls `POST /v1/agents/focus`. You submitted a prompt there, so that window is the one you are using.
2. **Implicit (polled once a second):** `GetForegroundWindow` gives the process id and title. The app builds the process tree (Toolhelp snapshot) and matches an agent when the focused process is the agent's `pid` or one of its ancestors (the terminal that hosts it). When several agents sit under one terminal process (Windows Terminal tabs, VS Code panes), only a title match on `titleHint` (folder name) separates them; ambiguous means no match. Terminals whose title does not contain the folder name will only get the explicit signal.

Reading-only API calls, never any input injection.

## 5. Bring to front (risk call-out)

Off by default. When on, at the start of an agent's turn the app walks up from the agent's pid to the first process with a main window and calls `ShowWindow(restore)` plus `SetForegroundWindow`. Windows may refuse (foreground lock), in which case nothing happens. It moves what is on the user's screen, so it is a setting the user turns on, it fires once per turn, and it is not covered by cloud tests: **test it by hand, never while away**.

## 6. Reply by voice (separate PR, after this one)

Goal: press a button or hotkey, talk, and the text lands in the Claude Code window that is reading or focused.

- **Recognition:** start with Windows' built-in local recognizer (`System.Speech.Recognition`, dictation grammar; the app already references System.Speech for SAPI). No model download, offline, adequate for sentences. If accuracy disappoints, swap in a local neural recognizer (sherpa-onnx is already a dependency for voices; it ships streaming ASR models) behind the same small `IDictationEngine` interface.
- **Trigger:** push to talk, a global hotkey (default Alt+D, hold or toggle) and a mic button in the popup/tray. Opt in; microphone only opens while pressed, with a visible indicator.
- **Delivery, two ways:**
  1. *Through the Claude Code window (no keyboard injection):* a slash command `/tts:reply` that runs `tts.mjs dictate`, which asks the app to listen (`POST /v1/dictation/start`, the user presses the hotkey or just talks until silence) and returns the recognized text as the command output, so it becomes the prompt. Works in any window, no focus tricks.
  2. *From the app:* the hotkey dictates, then, if `Send dictation to the reading window` is on, pastes the text into the agent window that holds the turn (or is focused) and optionally presses Enter. That needs bring-to-front plus synthesized keystrokes or a clipboard paste, so it is a separate opt in, off by default, with confirmation before the first use, and it never submits without the Enter option.
- **Tie-in with turn taking:** dictation starting pauses reading (so the microphone does not hear the speaker and the user can talk), the target is the holder or the focused agent, and the turn is handed on when the reply has been delivered.

I recommend it as its own PR: it adds a microphone permission, new hotkeys and a keystroke path, which deserve separate review and hand testing from this one.

## 7. Risks and open points

- No .NET SDK in the cloud, so the C# (and its xunit tests) was written without compiling. Kevin builds and runs `dotnet test` locally; the Node side is tested in the cloud.
- NAudio form-factor lookup and the Toolhelp/user32 P/Invokes are the least certain code; each fails safe (unknown headphones denies when the rule is on; focus unknown just means no promotion).
- Windows with several agents in one terminal tab group depend on title hints (section 4).
- Agents on the same project folder share an identity and take turns as one. (Using the hook's session id would split them, but the MCP server has no session id, so one window would then compete with itself.)
- `pid` and `titleHint` are client supplied and used by bring-to-front. Any local process with the token could ask the app to raise some window. Follow-up: derive the caller's pid from the loopback socket (GetExtendedTcpTable) and only raise windows of its ancestors.
- `now` focus mode interrupts speech the user may be listening to; default is `next`.
