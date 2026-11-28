# Auto-scroll that follows the reading

**Goal.** The popup and the main window keep the text being read in view without the person touching the scroll bar, and let go as soon as they scroll themselves.

**Decided with Kevin.**
- The view stays still while the sentence being read is in the middle third. When its middle drifts into the top or bottom third (or out of view) the view moves so the sentence is centered.
- The move is a jump, not a glide.

**Defaults chosen (not discussed, easy to change).**
- A sentence taller than half the view is lined up a quarter from the top, so its start stays visible.
- Scrolling by hand (wheel over a scrollable view, dragging or clicking the scroll bar, touch) turns following off. A "Follow reading" button appears at the bottom right; pressing it scrolls back and follows again.
- Following also turns back on when a new reading starts or when a word is clicked.
- The state is not saved between runs. Pausing does not change it.
- Scrolling does not change how text is sent, shown or spoken.

**Code.** The rule is `TextToSpeech.Core/Shell/FollowScroll.cs` (tested in `FollowScrollTests`); `TextToSpeech/ReadingDisplay.cs` measures the sentence, applies it, and owns the button (an adorner), so both windows get it.

**Hand test (Windows).** Read a long text: the view holds still, then re-centers when the reading reaches the lower third. Scroll with the wheel: the view stops moving and a button appears; press it and the view jumps back to the current sentence. Click a word: following resumes.
