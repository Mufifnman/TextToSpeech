# Architecture review

Written 2026-11-28 against master at `55c6d4e`, plus a read of the open PR branches (#16 to #20). Nothing here could be
compiled or run in the review environment (no .NET SDK); every statement comes from reading code. Items that need a hand
check on Windows are marked **[test]**.

## Shape of the app

Four layers, and the seams are mostly in the right places:

| Layer | Project | Depends on | Notes |
| --- | --- | --- | --- |
| Rules | `PlainRules` | nothing | Regex/plain-language rule engine with match timeouts (300 ms). Cleanly separable. |
| Core | `TextToSpeech.Core` (net8.0) | PlainRules | Reading list, controller, sentence splitter, filter file library, `ISpeechEngine`. No UI, unit tested. |
| API | `TextToSpeech.Api` (net8.0) | Core | Router, security, sessions, `ReadingHost`, loopback server. No WPF. |
| App | `TextToSpeech` (net8.0-windows) | all | WPF windows, tray, hotkeys, OCR, NAudio playback, SAPI and sherpa-onnx engines. |

`ReadingController` is the centre: window, hotkeys, popup and API all drive one instance, and it is tested with a fake engine.
That is the strongest part of the design. The weakest part is that the App layer is still one large code-behind
(`MainWindow.xaml.cs`, about 1,040 lines, 78 members) that wires everything together.

## Weaknesses, ranked

### 1. Downloaded voice models are not verified (security)
`VoiceDownloader` fetches `*.tar.bz2` from a GitHub *release* URL (mutable assets), extracts it and loads the ONNX model
into a native runtime, with no checksum and no size cap. .NET's tar extraction rejects path traversal, which is good, but a
swapped or truncated archive would be loaded as is. Fix: pin a SHA-256 per catalog entry, check it before extracting, cap
decompressed size. Also the catalog is marked `GUESS`; Kokoro v0.19 is an old release (see the roadmap).

### 2. CI runs far less than the repo tests
`.github/workflows/dotnet-desktop.yml` builds the solution and runs only `TextToSpeech.Core.Tests`. Not run anywhere:
`TextToSpeech.Api.Tests`, `TextToSpeech.Tests` (filters), `PlainRules.Tests` (PR #18 adds this one), the Node tests under
`integrations/` and `plugins/`, and `TextToSpeech.Windows.Tests`. Also, `*.traineddata` files are Git LFS pointers (132-byte
text files) and the checkout step has no `lfs: true`, so CI-built output ships pointer text as OCR data. The artifact step
uploads every `.exe`/`.dll` in every `bin\Release`, test projects included. Fix after PR #18 merges (it edits the same file).

### 3. The filter engine in `Filters.cs` is quadratic
`RemoveTargets`, `ReplaceTargets` and `CombineLines` loop `while (Regex.IsMatch(...)) { Regex.Match(...); text = text.Remove/Insert }`.
Each iteration rescans from the start and reallocates the string, so cost grows with matches times text length, on the UI
thread, and with no regex timeout. Invisible on a paragraph; **[test]** try pasting a 200 KB PDF dump with many line breaks.
Fix: one `Regex.Replace` per pass until the text stops changing, with a timeout, covered by the existing 26 filter tests plus a
long-input test. Deferred only because `TextToSpeech.Tests` does not run in CI yet (see 2).

### 4. Settings and file formats have no versions and inconsistent safety
Four JSON files under `%LocalAppData%\TextToSpeech`: `settings.json`, `filters.json`, the hotkeys file, `api.json`, plus
one file per custom filter. Only the custom filter files are going to carry a version (PR #18). The rest: no version field,
a corrupt file silently resets to defaults and the next save overwrites the evidence, and until this review three of them
wrote non-atomically (fixed in the AtomicFile PR; `AppSettings` and `ConnectionInfo` follow once PR #19 merges). Fix
direction: one small `JsonFileStore<T>` in Core with `version`, atomic save, and "keep the corrupt file as `.bad`".

### 5. Audio pipeline: gaps by construction
`PcmPlayer.PlayAsync` creates and tears down a `WaveOutEvent` per sentence, so every sentence pays device open/close, and a
whole sentence is synthesized before any audio plays (`OfflineTts.Generate` is not streamed). The synthesis lock
(`modelGate`) is process-wide, and work dropped from the look-ahead cache keeps running while holding it, so the sentence you
need next can queue behind sentences you no longer want. PR #20 (in-order queue, deeper look-ahead) addresses the third;
the first two are the next step: one long-lived output with a mixing provider, and sherpa-onnx's callback API to start
playing before the sentence finishes. `SetRate` is a no-op for the neural engine, so a speed change waits for the next
sentence. Word highlighting is estimated from text (`WordTimingEstimator`), not from the model, so it drifts on long sentences.

### 6. The local API as a long-lived contract
Good: loopback only, per-start random 256-bit token, constant-time compare, Host check (DNS rebinding), Origin rejection
(browsers), body cap, SSE client cap, internal errors not leaked, validation in a transport-free router with tests.
Gaps:
- **Versioning is a constant.** `ApiVersion = 1` appears in `/v1/status` only. There is no written compatibility rule. Write
  it down: additive changes only inside `/v1` (new optional fields, new routes, new error codes); unknown request fields are
  already ignored; clients must ignore unknown response fields; breaking changes get `/v2` served alongside.
- **Error codes are part of the contract** (`unsupported_override`, `forbidden_origin`, ...) but are not listed anywhere.
  Generate an OpenAPI or a plain table from the router and test it.
- **One token for everything.** PR #19 adds client-claimed agent identity, which the earlier design review already flagged
  (bring-to-front trusts a claimed pid). Per-agent tokens or scopes are the long-term answer.
- **`api.json` is trusted blindly by clients.** The `pid` it contains is never checked, so a stale file after a crash points
  clients at a dead port (harmless) or at whatever process reused it (the token is then sent to it). Clients should check the
  pid is alive and the process is TextToSpeech.
- **The accept loop never restarts**: if `GetContextAsync` throws once, the API silently stops while the UI says it is on.
- The preferred port is fixed (47821) with a free-port fallback; fine, because `api.json` is the source of truth.

### 7. No logging
There is no log of any kind (`catch (Exception) { }` appears in dozens of places, including the API request handler).
When something fails on Kevin's PC the only evidence is a status-line message. Add a tiny rolling file logger (Core
interface, App implementation) before more background features (voice input, arbitration) make failures harder to see.

### 8. Threading model
The rule is sound and documented: controller, document, host and router are single-threaded on the UI thread; engines report
words from any thread and the controller posts them back; the API hops to the UI thread per request. Risks: every API
request runs on the UI thread, so a slow filter run (item 3) or a 1 MB body freezes the window; `SpeakCurrent` recursion relies
on a synchronization context to stay on the UI thread (true in WPF, not in plain tests); `ReadingHost.ReleaseAllHandoffs`
blocks on `GetAwaiter().GetResult()` on the UI thread (safe today because the tasks are already complete, a trap if that changes).

### 9. Cross-platform readiness
Better than it looks. Core, Api and PlainRules are plain net8.0 and already tested on Linux. What is Windows-only: WPF
windows, tray (H.NotifyIcon), global hotkeys, `SendInput` selection capture, Windows OCR, NAudio `WaveOutEvent`, SAPI, Run-key
startup, single-instance mutex. sherpa-onnx itself is cross-platform. A second platform needs: a UI toolkit decision
(Avalonia is the usual WPF-adjacent choice; a tray-only core with a browser UI served by the existing API is the cheaper
experiment), an audio output abstraction (the current `PcmPlayer` is the only thing to replace), and platform interfaces for
hotkeys, selection capture, OCR and startup. Do not start before the engine/audio interfaces are cut (item 5).

### 10. Packaging and updates
Master has no installer (the WiX branch is from 2023), no versioned releases, no signing (SmartScreen warning), no update
path. Models are downloaded at runtime, Tesseract data and Emgu.CV (large native runtime, used only for OCR preprocessing)
ship in every build. Options in the roadmap: self-contained single-file publish plus MSIX or Velopack, and dropping
Tesseract and Emgu now that Windows OCR is the default.

### 11. Plugin and agent integration
Three copies of the Node client (`integrations/tts-client.mjs`, `plugins/tts/tts-client.mjs`, and the MCP and CLI wrappers)
exist because the plugin must be self-contained. Keep that, but add a test that fails when the copies differ, or generate the
plugin copy from one source. Hooks that auto-read replies depend on the user-prompt and stop events; those are Claude Code
behaviours that can change, so keep the plugin version independent of the app version and report the API version it needs.

### 12. Tidy-ups (done in the hygiene PR)
Dead files (`pingme.txt`, `XMLFile1.xml`, a committed OneNote interop package), the obsolete
`System.Text.RegularExpressions 4.3.1` package reference, and a 3-line README with typos.

## Refactors considered, and how they were judged

Each proposal went through two adversarial reviews (an experienced .NET developer, a long-term maintainer of a solo project).

| Proposal | Verdict | Outcome |
| --- | --- | --- |
| Delete dead files, drop obsolete package reference | Approved with changes: split into two commits, remove the whole empty Solution Items block | Done (PR "Remove dead files") |
| `AtomicFile` helper, used by filters.json, hotkeys, custom filters | Approved with changes: unique temp name, same folder, no BOM, cleanup on failure, no swallowed exceptions, tests in Core.Tests | Done (PR "Save ... atomically") |
| README rewrite | Approved, keep it factual | Done |
| Architecture review and roadmap docs | Approved with changes: tie to code, mark proposals as proposals; maintainer asked that the roadmap not rot in master | Roadmap is a separate PR Kevin can close |
| `dependabot.yml` | Approved only monthly, grouped, limit 3, merged after PR #18; native packages cannot be verified by CI | Not opened; listed in the roadmap |
| Linear-time filter loops (item 3) | Deferred: needs `TextToSpeech.Tests` in CI first | Plan |
| CI runs all tests, `lfs: true` | Blocked by PR #18 editing the workflow | Plan |
| Split `MainWindow.xaml.cs` | Rejected for now: conflicts with every in-flight PR | Plan |
| `AppSettings` and `ConnectionInfo` use `AtomicFile` | Deferred until PR #19 merges | Plan |

Files deliberately not touched because open PRs or threads own them: `.github/workflows/dotnet-desktop.yml` (#18),
`TextToSpeech/AppSettings.cs`, `LocalApiService.cs`, `SettingsWindow*`, `MainWindow.xaml.cs` and everything under
`TextToSpeech.Api/` (#19), `FilterEditorWindow*` and `PlainRules/` (#16, #18), `plugins/` and `integrations/` (#17, #19),
`ReadingController`, `ISpeechEngine`, `NeuralSpeechEngine`, `LookAheadCache` (#20), and the popup files (markdown thread).

## What to test by hand after merging

- Save a setting, a hotkey change and a filter edit, restart, confirm they stuck (AtomicFile PR). **[test]**
- Open the solution in Visual Studio: no "Solution Items" folder, it builds (hygiene PR). **[test]**
