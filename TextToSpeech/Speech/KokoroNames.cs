using System.Text.RegularExpressions;

namespace TextToSpeech.Speech
{
    /// <summary>
    /// Kokoro names its voices with short ids like "af_bella": the first letter is the accent (a = American,
    /// b = British), the second is the gender (f / m), then the voice name. The picker shows them readably.
    /// </summary>
    public static class KokoroNames
    {
        private static readonly Regex Id = new(@"^([ab])([fm])(?:_(.+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>"af_bella" becomes "Bella (US, female)"; "af" becomes "Default (US, female)". Other text is left alone.</summary>
        public static string Friendly(string id)
        {
            var match = Id.Match(id.Trim());
            if (!match.Success) return id;

            string accent = match.Groups[1].Value.ToLowerInvariant() == "a" ? "US" : "UK";
            string gender = match.Groups[2].Value.ToLowerInvariant() == "f" ? "female" : "male";
            string name = match.Groups[3].Success
                ? char.ToUpperInvariant(match.Groups[3].Value[0]) + match.Groups[3].Value.Substring(1)
                : "Default";
            return $"{name} ({accent}, {gender})";
        }
    }
}
