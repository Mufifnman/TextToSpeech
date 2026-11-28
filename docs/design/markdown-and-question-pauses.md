# Markdown view and question pauses

## Goal record

**Goal.** Reading text can be laid out in the popup and the window (headings, nested lists, code), with the markup never
shown or spoken, and a caller can mark places where reading stops and waits (typically questions), so a hands-free loop works:
listen to Claude's reply, answer a question by dictation, continue with the next one.

**Requirements Kevin set (hard).**
- Click-to-jump and the word highlight work on the formatted text. Prewarming must keep working with lists, spoken numbers
  and the doubled pause (no gaps or late starts).
- Everything is read, the whole response and then the questions, not only the questions.
- The app never decides what is a question or where to pause. The API caller passes a list of questions (or plain pause
  points); nothing is parsed or guessed.
- Pausing at marked questions is the default. A Play all button in the popup and the window reads through without stopping;
  it resets for every new set of questions (so it must be pressed again), and a setting makes play-all on by default.
- A pause ends when a person presses play (button, tray, hotkey), or when an API caller says so (`next`, `answered`). The API
  reports where reading is waiting. The wait uses the existing waiting-text expiry (4 hours by default, and a submitted prompt
  in that window), no separate timeout.

**Parameters / decisions.**
- Markdown subset: headings, bullet and numbered lists (nested, checkboxes), bold/italic/strike/code/links stripped, fenced
  code (monospace, one sentence per line, no syntax highlighting), block quotes, simple tables (read row by row).
- Numbered items are spoken with their number ("One. ..."), bullets are not announced. The pause after a list item is twice
  the normal sentence pause.
- Scope: everything read through the app (API, plugin, region reads, clipboard and typed text), with a Settings toggle
  ("Show markdown laid out"). Typed or pasted text is only laid out when it has structure (heading, list, code fence,
  quote); plain prose keeps today's handling, including joining of broken lines.
- Side items in this PR: text size setting and Ctrl+wheel in the main window; the popup stays open for the longer of the
  auto-clear delay and a new "Stay open" setting, never hiding before the text clears.

**Non-goals.** Question detection or guessing in the app; images or HTML; editing in the popup; speech recognition in the
app (Handy stays separate); syntax highlighting and themes (later, if cheap).

**Open questions / flagged assumptions.**
- Code blocks are laid out and also read aloud (nothing marks them silent). The plugin's auto-read hook drops them from the
  text it sends; an explicit `speak` keeps them.
- Pause points attach to the paragraph or list item that contains their offset (not to a single sentence).
- A handoff (`take`/`replace`) works on plain text: markup and pause points of the unread part are not kept.
- Dictation: Handy 0.10.1 (Ctrl+Space, now rebound by Kevin). Not integrated.

## API

`POST /v1/speak` gets three optional fields:

```json
{ "text": "markdown...", "source": "claude-code",
  "questions": ["Which branch?", "Add tests?"],
  "pauses": [ { "id": "m1", "label": "optional", "at": 212 } ],
  "playAll": false }
```

- `questions`: a question the text already says (same words, ignoring case, punctuation and markup) is not added again; reading waits there. The rest go after the text as a numbered list, each followed by a pause. Ids `q1`, `q2`, ... (in the order given). A question is shown and read once.
- `pauses`: reading stops after the paragraph or list item containing character offset `at` of `text` (default: the end).
  Not allowed together with `applyFilters` (the filters change the offsets); `questions` is.
- `playAll`: default is the "Play all by default" setting. Applies to this text; the next request with pauses resets it.
- Through the arbiter (`agent`), waiting text keeps its questions and pauses.

Control: `POST /v1/next` continues from the pause reading is waiting at and marks it answered (409 `not_paused_at_pause`
otherwise). `POST /v1/pauses/{id}/answered` (body optional `{ "sessionId": "..." }`) marks one answered; if reading waits
at it, it continues, otherwise reading will not stop there (404 `no_such_pause`).

Status: `state` can be `paused_at_pause`; `pausedAt: { sessionId, pauseId, label, number, of }`;
`pauses: [ { sessionId, id, label, state: pending|waiting|answered } ]`; `playAll`. The SSE stream sends `paused-at-pause`
and `pause-answered` events next to the usual state events.

## Code map

- `TextToSpeech.Core/Reading/MarkdownReader.cs`: blocks with source offsets, inline markup removal.
- `ReadingDocument.InsertMarkdownAt` builds sentences with `SentenceFormat` (kind, level, marker, spoken prefix, gap factor)
  and attaches `PausePoint`s. `Sentence.SpokenText` is what the engine speaks and what prewarm is given; it only adds the
  number prefix, which is outside the words (negative offset in `GetTextToSpeak`).
- `ReadingController`: stops after a sentence that has an unanswered pause (unless `PlayAll`), `PausedAt`, `ContinueFromPause`,
  `MarkAnswered`; the gap after a list item is doubled.
- `TextToSpeech.Api`: request fields, `next`, `answered`, status additions, arbiter carries the extras.
- `TextToSpeech/ReadingDisplay.cs` lays the sentences out; `PopupPolicy.Linger`; settings in `AppSettings`.
