using System.Globalization;

namespace TextToSpeech.Api.Arbitration
{
    /// <summary>
    /// The user's rules for whether agents may read aloud at all. Lives in the app settings; agents can read it
    /// (and ask for a mute) but cannot loosen it.
    /// </summary>
    public sealed record ReadPolicy
    {
        /// <summary>Master switch. False: nothing from agents is spoken.</summary>
        public bool Enabled { get; init; } = true;

        /// <summary>What the plugin's Stop hook does when no environment variable says otherwise: off, announce or reply.</summary>
        public string AutoRead { get; init; } = "off";

        /// <summary>Only read while headphones or a headset are the audio output. Unknown counts as "no".</summary>
        public bool RequireHeadphones { get; init; }

        /// <summary>Quiet hours as "HH:mm" (local time). Both must be set; the range may cross midnight.</summary>
        public string? QuietFrom { get; init; }
        public string? QuietTo { get; init; }

        /// <summary>What switching to a window with waiting text does: off, next (read it as soon as the current turn ends) or now (interrupt).</summary>
        public string FocusMode { get; init; } = "next";

        /// <summary>Bring the reading agent's window to the front when its turn starts. Off by default (acts on the user's screen).</summary>
        public bool BringToFront { get; init; }

        /// <summary>Waiting reads kept per agent; the oldest are dropped past this.</summary>
        public int MaxPendingPerAgent { get; init; } = 5;
    }

    /// <summary>What the policy is judged against right now.</summary>
    public sealed record PolicyContext(bool? HeadphonesConnected, TimeSpan TimeOfDay, DateTimeOffset Now, DateTimeOffset? MutedUntil);

    public sealed record PolicyDecision(bool MayRead, IReadOnlyList<string> Reasons);

    public static class PolicyEvaluator
    {
        public static PolicyDecision Evaluate(ReadPolicy policy, PolicyContext ctx)
        {
            var why = new List<string>();
            if (!policy.Enabled)
            {
                why.Add("Reading is switched off in settings.");
            }
            if (ctx.MutedUntil.HasValue && ctx.MutedUntil.Value > ctx.Now)
            {
                why.Add("Muted until " + ctx.MutedUntil.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture) + ".");
            }
            if (policy.RequireHeadphones && ctx.HeadphonesConnected != true)
            {
                why.Add(ctx.HeadphonesConnected == null
                    ? "Headphones are required and the app cannot tell what the audio output is."
                    : "Headphones are required and the audio output is not headphones.");
            }
            if (InQuietHours(policy, ctx.TimeOfDay))
            {
                why.Add("Quiet hours (" + policy.QuietFrom + " to " + policy.QuietTo + ").");
            }
            return new PolicyDecision(why.Count == 0, why);
        }

        public static bool InQuietHours(ReadPolicy policy, TimeSpan t)
        {
            if (!TryParse(policy.QuietFrom, out var from) || !TryParse(policy.QuietTo, out var to) || from == to)
            {
                return false;
            }
            return from < to ? t >= from && t < to : t >= from || t < to;
        }

        private static bool TryParse(string? s, out TimeSpan value)
        {
            value = default;
            return !string.IsNullOrWhiteSpace(s)
                && TimeSpan.TryParseExact(s.Trim(), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out value)
                && value >= TimeSpan.Zero && value < TimeSpan.FromHours(24);
        }
    }
}
