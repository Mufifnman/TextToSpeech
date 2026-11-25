using System.Text.RegularExpressions;

namespace TextToSpeech
{
    /// <summary>
    /// Which clipboard/OCR clean-up filters are switched on. Each filter can be toggled from the
    /// Filters menu in the main window; all are on by default to match the original behaviour.
    /// </summary>
    public class FilterSettings
    {
        public bool WikipediaCitations { get; set; } = true;
        public bool PsychologyCitations { get; set; } = true;
        public bool GovernmentCitations { get; set; } = true;
        public bool AhitStarredQuestions { get; set; } = true;
    }

    static internal class Filters
    {
        /// <summary>
        /// Runs every enabled filter over text pasted from the clipboard, in the original order.
        /// </summary>
        internal static void ApplyClipboardFilters(ref string text, FilterSettings settings)
        {
            if (settings.WikipediaCitations) WikipediaCitation(ref text);
            if (settings.PsychologyCitations) PsychologyFilter(ref text);
            if (settings.GovernmentCitations) GovernmentFilter(ref text);
            if (settings.AhitStarredQuestions) AHITStarredQuestion(ref text);
        }

        /// <summary>
        /// Runs the enabled filters that apply to OCR output (after lines have been combined).
        /// </summary>
        internal static void ApplyOcrFilters(ref string text, FilterSettings settings)
        {
            if (settings.PsychologyCitations) PsychologyFilter(ref text);
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
