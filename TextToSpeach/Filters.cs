using System.Text.RegularExpressions;

namespace TextToSpeach
{
    // TODO: Make these check-able
    static internal class Filters
    {
        internal static void PsycologyFilter(ref string readText)
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

        internal static void WikipediaCitation(ref string preFiltered)
        {
            RemoveTargets(ref preFiltered, @"\[edit\]");

            RemoveTargets(ref preFiltered, @"\[[0-9]+\]");
        }

        internal static void GovernmentFilter(ref string text)
        {
            PsycologyFilter(ref text);

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
    }
}
