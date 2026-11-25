namespace TextToSpeech
{
    /// <summary>
    /// Remembers whether the app itself brought the window up for the current read (by a hotkey or the
    /// "bring up while playing" setting), so it only minimizes it again when that read finishes if the
    /// user has not taken over the window in between.
    ///
    /// How a manual focus is told apart: the app's own raise causes one activation right away, which
    /// is ignored (within <see cref="Grace"/>). Any later activation means the window lost focus and the
    /// user brought it back, and so does a click or key press inside the window. Either clears the flag.
    /// </summary>
    public sealed class RaiseTracker
    {
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(1.5);

        private readonly Func<DateTime> now;
        private bool raised;
        private DateTime raisedAt;

        public RaiseTracker(Func<DateTime> now) => this.now = now;

        /// <summary>True while the app raised the window and the user has not taken over.</summary>
        public bool RaisedByApp => raised;

        /// <summary>Call right before the app brings a hidden or minimized window up.</summary>
        public void AppRaised()
        {
            raised = true;
            raisedAt = now();
        }

        /// <summary>Call whenever the window gets activated.</summary>
        public void Activated()
        {
            if (raised && now() - raisedAt > Grace)
            {
                raised = false; // focus came back by hand
            }
        }

        /// <summary>Call when the user clicks or types in the window.</summary>
        public void UserInteracted() => raised = false;

        /// <summary>At the end of a read: true if the window should be minimized now. Resets the flag.</summary>
        public bool TakeMinimize()
        {
            bool result = raised;
            raised = false;
            return result;
        }
    }
}
