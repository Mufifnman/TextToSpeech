using TextToSpeech.Core.CustomFilters;

namespace TextToSpeech
{
    /// <summary>
    /// Glue between the custom filter files (TextToSpeech.Core / PlainRules) and <see cref="FilterRegistry"/>.
    /// Reload re-reads the folder; usable filters join the registry after the built-in ones,
    /// files with problems are listed in <see cref="Problems"/> and skipped.
    /// </summary>
    internal static class CustomFilterManager
    {
        internal const string DefaultGroup = "My filters";

        internal static CustomFilterLibrary Library { get; } = new(CustomFilterLibrary.DefaultFolder);

        internal static IReadOnlyList<CustomFilterEntry> Entries { get; private set; } = Array.Empty<CustomFilterEntry>();

        internal static IEnumerable<CustomFilterEntry> Problems => Entries.Where(e => !e.IsUsable);

        /// <summary>Re-reads the filters folder and updates <see cref="FilterRegistry"/>. Never throws.</summary>
        internal static void Reload()
        {
            Entries = Library.Load();
            FilterRegistry.SetCustom(Entries.Where(e => e.IsUsable).Select(ToDefinition).ToArray());
        }

        internal static CustomFilterEntry? FindById(string id) => Entries.FirstOrDefault(e => e.Id == id);

        private static FilterDefinition ToDefinition(CustomFilterEntry entry)
        {
            var compiled = entry.Compiled!;
            string group = string.IsNullOrWhiteSpace(entry.Set?.Group) ? DefaultGroup : entry.Set!.Group!.Trim();
            // Custom filters run on both copied text and OCR output.
            return new FilterDefinition(entry.Id, group, entry.DisplayName, FilterStage.Clipboard | FilterStage.Ocr,
                (ref string text) => { text = compiled.Apply(text); });
        }
    }

    /// <summary>Turns "Web/Clean links" into a menu path ("Web") and a name ("Clean links"). / and \ both separate.</summary>
    internal static class FilterPath
    {
        internal static (string? Group, string Name) Split(string text)
        {
            var parts = text.Split(new[] { '/', '\\' }).Select(p => p.Trim()).ToList();
            string name = parts[^1];
            var group = parts.Take(parts.Count - 1).Where(p => p.Length > 0).ToList();
            return (group.Count == 0 ? null : string.Join("/", group), name);
        }
    }
}
