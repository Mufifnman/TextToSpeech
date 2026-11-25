using System.Text.RegularExpressions;

namespace TextToSpeech
{
    /// <summary>Signature shared by every text clean-up filter.</summary>
    internal delegate void TextFilter(ref string text);

    /// <summary>Where a filter runs: on pasted/copied text, on OCR output, or both.</summary>
    [Flags]
    internal enum FilterStage
    {
        Clipboard = 1,
        Ocr = 2,
    }

    /// <summary>One clean-up filter as shown in the Filters menu. Group becomes a submenu.</summary>
    internal sealed record FilterDefinition(string Id, string Group, string Label, FilterStage Stages, TextFilter Apply);

    /// <summary>
    /// The list of all filters, in the order they run. The Filters menu, the on/off settings and
    /// the clipboard/OCR pipelines are all driven from this list, so adding a filter means writing
    /// its method and adding one line to <see cref="All"/>.
    /// </summary>
    internal static class FilterRegistry
    {
        private static readonly FilterDefinition[] BuiltIn = new FilterDefinition[]
        {
            new("wikipedia", "Citations", "Wikipedia citations ([1], [edit])", FilterStage.Clipboard, Filters.WikipediaCitation),
            new("author-year", "Citations", "Author-year citations (Smith, 2004)", FilterStage.Clipboard | FilterStage.Ocr, Filters.PsychologyFilter),
            new("page-citations", "Citations", "Page citations, lifetimes (p. 12)", FilterStage.Clipboard, Filters.GovernmentFilter),
            new("ahit", "Quiz material", "AHIT starred questions / blanks", FilterStage.Clipboard, Filters.AHITStarredQuestion),
        };

        /// <summary>Built-in filters followed by the user's custom filters (see <see cref="SetCustom"/>).</summary>
        public static IReadOnlyList<FilterDefinition> All { get; private set; } = BuiltIn;

        /// <summary>Replaces the custom filters that follow the built-in ones. Call on the UI thread.</summary>
        internal static void SetCustom(IEnumerable<FilterDefinition> custom) =>
            All = BuiltIn.Concat(custom).ToArray();

        /// <summary>Distinct group names in first-seen order (one submenu each).</summary>
        public static IEnumerable<string> Groups => All.Select(f => f.Group).Distinct();
    }

    /// <summary>
    /// Which filters are switched off. Everything is on by default (including filters added later),
    /// so only the ids the user turned off are stored.
    /// </summary>
    public class FilterSettings
    {
        public HashSet<string> Disabled { get; set; } = new();

        internal bool IsEnabled(string id) => !Disabled.Contains(id);

        internal void SetEnabled(string id, bool enabled)
        {
            if (enabled) Disabled.Remove(id); else Disabled.Add(id);
        }
    }

    static internal class Filters
    {
        /// <summary>
        /// Runs every enabled filter over text pasted from the clipboard, in registry order.
        /// </summary>
        internal static void ApplyClipboardFilters(ref string text, FilterSettings settings) =>
            Apply(ref text, settings, FilterStage.Clipboard);

        /// <summary>
        /// Runs the enabled filters that apply to OCR output (after lines have been combined).
        /// </summary>
        internal static void ApplyOcrFilters(ref string text, FilterSettings settings) =>
            Apply(ref text, settings, FilterStage.Ocr);

        private static void Apply(ref string text, FilterSettings settings, FilterStage stage)
        {
            foreach (var filter in FilterRegistry.All)
            {
                if ((filter.Stages & stage) != 0 && settings.IsEnabled(filter.Id))
                {
                    filter.Apply(ref text);
                }
            }
        }

        internal static void PsychologyFilter(ref string readText)
        {
            // Psycology Site removal (up to 10 citations removes)
            string extra = @"[a-zA-Z,&\.\-\n\r ,]+[0-9]{4}a?b?;";
            string finish = @"[a-zA-Z,&\.\-\n\r ,]+[0-9]{4}a?b?\)";
            for (int i = 0; i < 10; i++)
            {
                RemoveTargets(ref readText, @"\(" + finish);
                finish = extra + finish;
            }
        }

        internal static void AHITStarredQuestion(ref string readText)
        {
            RemoveTargets(ref readText, @"Unset starred question");
            ReplaceTargets(ref readText, @"_____", @" blank ");
        }

        internal static void WikipediaCitation(ref string preFiltered)
        {
            RemoveTargets(ref preFiltered, @"\[edit\]");

            RemoveTargets(ref preFiltered, @"\[[0-9]+\]");
        }

        /// <summary>
        /// Removes page-number style citations, lifetimes and the book title.
        /// Note: used to call PsychologyFilter itself; that is now its own toggle and
        /// ApplyClipboardFilters runs it first, so the combined result is unchanged.
        /// </summary>
        internal static void GovernmentFilter(ref string text)
        {
            string citation = @"([a-z,A-Z\. ]+, )*([0-9]{4}b?, )?(pp?\. )([0-9, \-xiv]+(ff)?)";
            string buildup = citation;
            for (int i = 0; i < 6; i++)
            {
                RemoveTargets(ref text, @"\(" + buildup + @"\)");
                buildup += "; " + citation;
            }

            buildup = citation;
            for (int i = 0; i < 5; i++)
            {
                RemoveTargets(ref text, @"\{" + buildup + @"\)");
                buildup += "; " + citation;
            }

            // book title

            string title = @"CLASSIC MODELS";
            RemoveTargets(ref text, title);

            //Lifetimes
            string lifetime = @"\([0-9]{4}-[0-9]{4}\)";
            RemoveTargets(ref text, lifetime);

            //Regex Scratch Area
            //TODO: figure out how to make | work within other contexts

            //string form2 = @"\(" + @"(pp\. ((([0-9])+\-([0-9])+)|(([0-9])+)),?)+" + @"\)";
            //string formExact = @"\(" + @"([a-z,A-Z]+, )*(pp\. )(([0-9]+-[0-9]+,?)|([0-9]+,?))+" + @"\)";
            //string form = @"\(" + @"([a-z,A-Z]+, )*(pp?\. )([0-9, -]+)" + @"\)";
            //RemoveTargets(ref text, form);
        }

        internal static void CombineLines(ref string text)
        {
            // Split after section titles by adding a period
            string target = @"[^.]\n[A-Z]";
            while (Regex.IsMatch(text, target))
            {
                var match = Regex.Match(text, target);
                text = text.Insert(match.Index + 1, ".");
            }

            // get rid of breaks
            text = text.Replace("-\n\r", "");
            text = text.Replace("-\n", "");
            text = text.Replace('\n', ' ');

            // stop splitting on things like "Section 3.3"
            target = @"[0-9]\.[0-9]";
            while (Regex.IsMatch(text, target))
            {
                var match = Regex.Match(text, target);
                text = text.Remove(match.Index + 1, 1);
                text = text.Insert(match.Index + 1, " point ");
            }

            // Don't break when not a new sentence (remove '.')
            target = @"\.( )*[a-z]";
            while (Regex.IsMatch(text, target))
            {
                var match = Regex.Match(text, target);
                text = text.Remove(match.Index, 1);
                text = text.Insert(match.Index, " ");
            }
        }

        //Helper(s)

        private static void RemoveTargets(ref string text, string target)
        {
            while (Regex.IsMatch(text, target))
            {
                var match = Regex.Match(text, target);
                text = text.Remove(match.Index, match.Length);
            }
        }

        private static void ReplaceTargets(ref string text, string target, string replacemnt)
        {
            while (Regex.IsMatch(text, target))
            {
                var match = Regex.Match(text, target);
                text = text.Remove(match.Index, match.Length);
                text = text.Insert(match.Index, replacemnt);
            }
        }
    }
}
