namespace TextToSpeech.Core.Shell
{
    /// <summary>
    /// Decides when the small reading popup (above the tray) is showing. The window feeds it reading changes and
    /// asks <see cref="IsVisible"/>. No UI in here.
    ///
    /// Shows while reading is playing, while it is paused or stopped with text left (if
    /// <see cref="KeepOpenWhenPaused"/>), and while an API client holds the remainder. When everything is read it
    /// goes after <see cref="Grace"/> (if <see cref="AutoHide"/>), otherwise it stays until dismissed. It never
    /// shows while the full window is up, when disabled, or after the user dismissed it, until reading starts again.
    /// </summary>
    public sealed class PopupPolicy
    {
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5);

        private readonly Func<DateTime> now;
        private bool started;      // reading has begun since the last clear or dismiss
        private bool dismissed;
        private bool wasPlaying;
        private DateTime? hideAt;

        public PopupPolicy(Func<DateTime>? now = null) => this.now = now ?? (() => DateTime.UtcNow);

        /// <summary>The "Show reading popup" setting.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The "Hide popup when reading finishes" setting.</summary>
        public bool AutoHide { get; set; } = true;

        /// <summary>The "Keep popup open when paused" setting.</summary>
        public bool KeepOpenWhenPaused { get; set; } = true;

        /// <summary>The full window is on screen (not hidden to the tray, not minimized).</summary>
        public bool FullWindowShown { get; set; }

        /// <summary>The "Show popup while the window is open but not in focus" setting.</summary>
        public bool ShowWhenWindowUnfocused { get; set; }

        /// <summary>The full window has the focus (only matters with <see cref="ShowWhenWindowUnfocused"/>).</summary>
        public bool FullWindowFocused { get; set; }

        /// <summary>The Settings window is open: keep the popup up so changes can be seen as they are made.</summary>
        public bool KeepForSettings { get; set; }

        /// <summary>The popup is being resized (or just was): keep it up until the wait after the resize is over.</summary>
        public bool KeepForResize { get; set; }

        /// <summary>An API client has taken the remainder and not returned it yet.</summary>
        public bool HandoffOpen { get; set; }

        /// <summary>Call whenever reading starts, pauses, moves on or finishes.</summary>
        public void ReadingChanged(bool isPlaying, bool hasUnread)
        {
            if (isPlaying)
            {
                if (!wasPlaying)
                {
                    dismissed = false;   // a new start brings the popup back
                }
                started = true;
                hideAt = null;
            }
            else if (hasUnread)
            {
                hideAt = null;           // paused: stay (or not) according to the setting
            }
            else if (started && hideAt == null)
            {
                hideAt = now() + Grace;  // everything read: linger a moment
            }
            wasPlaying = isPlaying;
        }

        /// <summary>The list was cleared: nothing to show any more.</summary>
        public void Cleared()
        {
            started = false;
            hideAt = null;
            dismissed = false;
            wasPlaying = false;
        }

        /// <summary>The user closed the popup. It stays away until reading starts again.</summary>
        public void Dismiss() => dismissed = true;

        public bool IsVisible(bool isPlaying, bool hasUnread)
        {
            if (KeepForSettings || KeepForResize)
            {
                return Enabled;
            }
            bool windowCovers = FullWindowShown && (!ShowWhenWindowUnfocused || FullWindowFocused);
            if (!Enabled || windowCovers || dismissed || !started)
            {
                return false;
            }
            if (isPlaying)
            {
                return true;
            }
            if (hasUnread)
            {
                return KeepOpenWhenPaused;
            }
            if (HandoffOpen)
            {
                return true;
            }
            return !AutoHide || (hideAt.HasValue && now() < hideAt.Value);
        }

        /// <summary>How long until a lingering popup should be re-checked (null when no timer is needed).</summary>
        public TimeSpan? TimeUntilRecheck()
        {
            if (hideAt is { } at && now() < at)
            {
                return at - now();
            }
            return null;
        }
    }
}
