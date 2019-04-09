using System;
using System.Globalization;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace TextToSpeach
{
    //TODO: move to local change model 
    // https://stackoverflow.com/questions/751741/wpf-textblock-highlight-certain-parts-based-on-search-condition
    class StringToXamlConverter : IValueConverter
    {
        public const string BeginHighlightToken = "|~S~|";
        public const string EndHighlightToken = "|~E~|";
        public Brush HighlightBrush = Brushes.LightGoldenrodYellow;

        // total hack, but faster so :)
        public static int currentWordIndex = -1;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string input = value as string;
            if (input != null)
            {
                var textBlock = new TextBlock();
                textBlock.TextWrapping = TextWrapping.Wrap;
                //textBlock.FontFamily = new FontFamily("Segoe UI");
                string escapedXml = SecurityElement.Escape(input);

                while (escapedXml.IndexOf(BeginHighlightToken) != -1)
                {
                    //up to |~S~| is normal
                    textBlock.Inlines.Add(new Run(escapedXml.Substring(0, escapedXml.IndexOf(BeginHighlightToken))));

                    //between |~S~| and |~E~| is highlighted
                    int beginHighlight = escapedXml.IndexOf(BeginHighlightToken) + BeginHighlightToken.Length;
                    string highlihgtedSubstring = escapedXml.Substring(beginHighlight,
                                                  escapedXml.IndexOf(EndHighlightToken) - (escapedXml.IndexOf(BeginHighlightToken) + BeginHighlightToken.Length));
                    if (currentWordIndex != -1)
                    {
                        int startUnderline = GetNthIndex(highlihgtedSubstring, ' ', currentWordIndex);
                        if (startUnderline == highlihgtedSubstring.Length 
                            || startUnderline == -1 
                            || highlihgtedSubstring.Length == 0) // do not hilight at all after finishing the last word
                        {
                            textBlock.Inlines.Add(new Run(highlihgtedSubstring));
                        }
                        else
                        { 
                            int endUnderline = GetNthIndex(highlihgtedSubstring, ' ', currentWordIndex + 1);

                            textBlock.Inlines.Add(new Run(highlihgtedSubstring.Substring(0, startUnderline + 1)) // 1 for the space itself
                            { Background = HighlightBrush });
                            textBlock.Inlines.Add(new Run(highlihgtedSubstring.Substring(startUnderline + 1, 
                                endUnderline != 0 ? endUnderline - (startUnderline + 1) : 0))
                            { Background = HighlightBrush, TextDecorations = TextDecorations.Underline });
                            if (endUnderline != highlihgtedSubstring.Length)
                            {
                                textBlock.Inlines.Add(new Run(highlihgtedSubstring.Substring(endUnderline, highlihgtedSubstring.Length - endUnderline))
                                { Background = HighlightBrush });
                            }
                        }
                    }
                    else
                    {
                        textBlock.Inlines.Add(new Run(highlihgtedSubstring)
                        { Background = HighlightBrush });
                    }

                    //the rest of the string (after the |~E~|)
                    escapedXml = escapedXml.Substring(escapedXml.IndexOf(EndHighlightToken) + EndHighlightToken.Length);
                }

                if (escapedXml.Length > 0)
                {
                    textBlock.Inlines.Add(new Run(escapedXml));
                }
                return textBlock;
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("This converter cannot be used in two-way binding.");
        }

        /// <summary>
        /// Returns 0 on 0th index, and last char when there is exactly one fewer
        /// </summary>
        /// <param name="s"></param>
        /// <param name="t"></param>
        /// <param name="n"></param>
        /// <returns></returns>
        public static int GetNthIndex(string s, char t, int n)
        {
            if (n == 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == t)
                {
                    ++count;
                    if (count == n)
                    {
                        return i;
                    }
                }
            }
            if (count == n - 1)
            {
                return s.Length;
            }

            return -1;
        }
    }
}
