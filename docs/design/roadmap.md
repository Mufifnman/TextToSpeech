# Roadmap proposal (draft, last reviewed 2026-11-28)

This is a proposal, not a commitment. It ranks what the work so far suggests Kevin will want next, and lists the
"grill-me" questions to ask before any of it is built (the project rule: no new feature starts without a goal record).
Nothing here has been started. Rank is value for the stated goal (a reading aid that is always there, fast, natural
sounding and safe) divided by effort and risk. See [architecture-review.md](architecture-review.md) for the reasoning.

## Foundations (not features, but they gate the features)

These are small, mostly invisible, and make everything below cheaper. They need no grill-me stage because they change
no behaviour; each is its own small PR.

1. Run every test project in CI, fetch LFS in checkout, upload only the app as an artifact (after PR #18 merges).
2. Linear-time filter loops with timeouts (`Filters.cs`), once the filter tests run in CI.
3. Pin SHA-256 for downloaded voice models; size cap on extraction.
4. `AppSettings` and `ConnectionInfo` on `AtomicFile`; then a `JsonFileStore<T>` with a version field and ".bad" copies.
5. A rolling log file with an "Open log folder" menu item.
6. Written API compatibility rules and an error-code table, plus a pid check for `api.json` in the Node clients and a
   test that keeps the plugin's copy of the client in sync.
7. Monthly grouped Dependabot (limit 3), merged after the CI work.

## Features, ranked

### 1. Gapless, faster-starting audio
One long-lived output device, streaming synthesis so speech starts before the sentence is finished, real word timings where
the model gives them. Pays off in every reading, and it is the cut point for any later platform work.
Grill-me questions:
- What is the longest gap between sentences you notice today, and is it between sentences or at the very start?
- Is a slightly rougher first second of audio acceptable if speech starts sooner?
- Should highlighting follow the audio exactly, or is the current estimate good enough?
- Do you ever use Bluetooth or switch output devices mid-reading (this decides how careful device handling must be)?

### 2. Voice replies to Claude (push to talk) and dictation
Already designed (`design/read-arbitration-and-dictation.md` in the project files), not built. Biggest day-to-day change
for the Claude Code use.
Grill-me questions:
- Is speaking a reply into Claude Code a daily need or an occasional one? Is Handy staying your dictation tool, or should this replace it?
- Push-to-talk key or toggle? Auto-stop on silence, on a timeout, or both?
- Should the app only return text (`/tts:reply` fills the prompt) or ever type into a window? (Recommendation: text only.)
- What must the microphone indicator look like so you always know it is listening?
- Which recognizer is acceptable: Windows built-in (offline, often weaker) or a downloaded sherpa-onnx model (bigger, better)?
- Is any audio allowed to leave the machine, ever?

### 3. Pronunciation and replacement dictionary
Names, acronyms, code identifiers and file paths are where neural voices stumble. A user-editable list ("kubectl" says
"kube control") that applies before synthesis, separate from the clean-up filters.
Grill-me questions:
- Which words go wrong most for you today (give five)?
- Should it be per voice, per source (Claude vs clipboard) or global?
- Edit in a table in the app, in a text file, or by right-clicking a word in the popup while it is read wrong?
- Should the dictionary be shareable like filters?

### 4. Better and more voices
Kokoro v0.19 is old; newer Kokoro releases add many voices and languages, and other small models exist. Keep the downloader
but make the catalog data-driven and verified. Voice quality matters more to the core audience than any other setting.
Grill-me questions:
- Which languages and accents do you actually need (only US English, or more)?
- Is a 300 MB download per voice fine? Is there a disk budget?
- Do you want the app to pick the voice by language of the text automatically?
- Would you ever turn on an optional cloud voice for quality, with text leaving the machine? (Default: no.)
- Is there a voice you disliked that should be removed from the catalog?

### 5. Read whole documents and web pages
Today the input is clipboard, selection and a screen region. Next natural inputs: a PDF or EPUB file, the current browser
page (via a small browser extension or a "reader mode" fetch), drag a file onto the tray icon.
Grill-me questions:
- What do you read most that is awkward today (PDF papers, web articles, email, books)?
- Should it remember where you stopped in each document and resume later?
- Is a browser extension acceptable, or must everything work from copy and paste?
- How should tables, code blocks, footnotes and headings be read (skipped, announced, read plainly)?

### 6. A reading-friendly popup and window
Markdown rendering and question pause points are in progress. Beyond that: dyslexia-friendly fonts and spacing, line
focus, theme-aware highlight colours, large-text mode, a one-line "why am I not hearing anything" status.
Grill-me questions:
- Which of font, spacing, line focus and colour matter to the people you build this for? Have any of them tried it?
- Is the popup the main view or the full window?
- Does the popup need to work with a screen reader or high-contrast themes?

### 7. Per-source profiles
Different voice, speed and filters for Claude replies, clipboard, screen region and each app.
Grill-me questions:
- Which two sources would you set up differently first?
- Per app (window class) or only per source type?
- Where does the setting live: in the filter editor, the settings window, or the plugin?

### 8. Read later inbox and saved audio
Queue text from anywhere ("send to TTS"), listen later, export to an audio file.
Grill-me questions:
- Do you want audio files, or only the live reading?
- Where do items come from (hotkey, share, API, email)?
- How long should the queue be kept, and is it private (deleted on exit)?

### 9. Installer, updates and signing
Self-contained single-file publish, MSIX or Velopack installer, per-release changelog, optional auto-update, code signing.
Grill-me questions:
- Who else will install this, and how (friends, public download, store)?
- Is an automatic update acceptable, or must updates be manual?
- Is a signing certificate (about a hundred dollars a year) worth it to avoid the SmartScreen warning?
- Keep Tesseract and Emgu (large) or drop them now that Windows OCR is the default?

### 10. Open-sourcing PlainRules and the plugin
PlainRules has its own solution, tests and README already; the Claude Code plugin is a separate marketplace entry.
Grill-me questions:
- Which license and which name on the copyright line (the file has a placeholder)?
- Separate repository, or stay in this one?
- Is anything Kevin-specific (the AHIT and psychology filters) allowed to be public?

### 11. Other platforms
Likely route: a tray-only core with a browser UI served from the local API, then native shells. Not before item 1 and the
platform interfaces for hotkeys, selection capture and OCR exist.
Grill-me questions:
- Which platform first (macOS, Linux, a phone), and for whom?
- Is a browser UI acceptable, or must it feel native?
- Which features are essential on day one there (probably just clipboard reading and the API)?

### 12. Multiple agents and agent-aware reading
Arbitration is open in PR #19. Anticipated follow-ups: per-agent voices, per-agent tokens and scopes, reading only the
final answer or summaries of long answers, a spoken "summary then details?" mode.
Grill-me questions:
- Do you run several agents at once regularly? Should each have its own voice?
- Should long answers be read in full, truncated, or summarized first (by whom)?
- Which agent tools besides Claude Code matter (other CLIs, editors)?

## Suggested order

Foundations 1 to 5, then features 1, 3, 4 (quality of the core experience), then 2 (voice replies), 6, 5, 9. Items 7, 8, 10, 11,
12 wait for Kevin's answers. Foundations 6 and 7 can go any time after 1.
