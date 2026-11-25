namespace TextToSpeech.Core.Shell
{
    /// <summary>Command-line options. <c>--background</c> starts hidden in the tray (what "Start with Windows" uses).</summary>
    public sealed record LaunchOptions(bool Background)
    {
        public const string BackgroundFlag = "--background";

        public static LaunchOptions Parse(IEnumerable<string>? args) =>
            new(args?.Any(a => string.Equals(a, BackgroundFlag, StringComparison.OrdinalIgnoreCase)) == true);
    }

    /// <summary>The value written to the Windows "Run" registry key for Start with Windows.</summary>
    public static class StartupCommand
    {
        public const string ValueName = "TextToSpeech";

        /// <summary>The exe path in quotes (paths can contain spaces) plus <c>--background</c>.</summary>
        public static string Build(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("exePath is required", nameof(exePath));
            }
            return "\"" + exePath.Trim().Trim('"') + "\" " + LaunchOptions.BackgroundFlag;
        }

        /// <summary>True when the stored command already launches <paramref name="exePath"/> (so a moved exe gets re-written).</summary>
        public static bool Matches(string? stored, string exePath) =>
            stored != null && string.Equals(stored.Trim(), Build(exePath), StringComparison.OrdinalIgnoreCase);
    }
}
