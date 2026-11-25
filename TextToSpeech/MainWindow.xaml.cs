using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Speech.Synthesis;
using TextToSpeech.Core.Reading;

using System.Windows.Controls;


namespace TextToSpeech
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// 
    /// The text being read lives in <see cref="ReadingDocument"/> (TextToSpeech.Core); this
    /// class only turns document positions into speech and user input into document moves.
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        // objects
        SpeechSynthesizer? reader = null;
        readonly ReadingDocument document = new ReadingDocument();
        int rate = 4;

        // Characters at the start of the current sentence that were not sent to the engine
        // (when starting from the middle of a sentence). Engine positions are relative to what we sent.
        int spokenOffset = 0;

        // Incremented for every utterance; events from cancelled utterances are ignored.
        int speakGeneration = 0;

        // True after the user paused or stopped; nothing speaks until they resume or click a word.
        bool paused = false;
        FilterSettings filterSettings = FilterSettingsStore.Load();

        public MainWindow()
        {
            this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            InitializeComponent();
            __ReadListDisplay__.Document = document;
            __ReadListDisplay__.WordClicked += JumpTo;

            wikipediaFilterItem.IsChecked = filterSettings.WikipediaCitations;
            psychologyFilterItem.IsChecked = filterSettings.PsychologyCitations;
            governmentFilterItem.IsChecked = filterSettings.GovernmentCitations;
            ahitFilterItem.IsChecked = filterSettings.AhitStarredQuestions;
        }

        private void FilterMenuItem_Click(object sender, RoutedEventArgs e)
        {
            filterSettings.WikipediaCitations = wikipediaFilterItem.IsChecked;
            filterSettings.PsychologyCitations = psychologyFilterItem.IsChecked;
            filterSettings.GovernmentCitations = governmentFilterItem.IsChecked;
            filterSettings.AhitStarredQuestions = ahitFilterItem.IsChecked;
            FilterSettingsStore.Save(filterSettings);
        }

        private void ReadText(object sender, RoutedEventArgs e)
        {
            if (this.textbox.Text != "")
            {
                string temp = textbox.Text;
                Filters.CombineLines(ref temp);
                // cut out "read more at"
                document.Append(temp, s => !s.ToLower().Contains("read more at"));
                textbox.Text = "";
            }
            paused = false;
            if (reader == null) // if we aren't already reading
            {
                SpeakCurrent();
            }
        }

        /// <summary>
        /// Speaks the document from its current position (sentence and word), cancelling
        /// anything already being spoken. Calls itself when a sentence completes. UI thread only.
        /// </summary>
        private void SpeakCurrent()
        {
            CancelSpeech();

            string? text = document.GetTextToSpeak(out spokenOffset);
            if (text == null)
            {
                // Done: maximize so next image can be scanned or line can be copied
                if (this.WindowState == System.Windows.WindowState.Minimized)
                {
                    this.WindowState = System.Windows.WindowState.Normal;
                }
                UpdatePauseButton();
                return;
            }

            int generation = ++speakGeneration;
            var synth = new SpeechSynthesizer { Rate = rate };
            reader = synth;
            synth.SpeakProgress += (s, e) =>
            {
                // SAPI raises this off the UI thread
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation == speakGeneration)
                    {
                        document.ReportSpokenCharacter(spokenOffset + e.CharacterPosition);
                    }
                });
            };
            synth.SpeakCompleted += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation != speakGeneration || e.Cancelled)
                    {
                        return;
                    }
                    document.CompleteSentence();
                    SpeakCurrent();
                });
            };
            synth.SpeakAsync(text);
            UpdatePauseButton();
        }

        /// <summary>Stops speaking without moving the reading position.</summary>
        private void CancelSpeech()
        {
            speakGeneration++; // ignore events from the utterance being cancelled
            if (reader != null)
            {
                reader.Dispose();
                reader = null;
            }
        }

        /// <summary>After the position moved: keep reading from there, unless the user is paused.</summary>
        private void AfterMove()
        {
            if (paused)
            {
                CancelSpeech();
                UpdatePauseButton();
            }
            else
            {
                SpeakCurrent();
            }
        }

        private void JumpTo(int sentence, int word)
        {
            document.JumpTo(sentence, word);
            // Clicking a word means "read from here", even when paused or stopped.
            paused = false;
            SpeakCurrent();
        }

        private void Rewind_Click(object sender, RoutedEventArgs e) => Rewind();

        private void Skip_Click(object sender, RoutedEventArgs e) => Skip();

        private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void Rewind()
        {
            document.Rewind();
            AfterMove();
        }

        private void Skip()
        {
            if (document.Skip())
            {
                AfterMove();
            }
        }

        /// <summary>
        ///  clears the list of items to be read
        /// </summary>
        private void ClearList(object? sender = null, RoutedEventArgs? e = null)
        {
            CancelSpeech();
            document.Clear();
            paused = false;
            UpdatePauseButton();
        }

        /// <summary>Stops speaking. The position is kept, so Pause/Play (or clicking a word) continues from here.</summary>
        private void StopRead(object? sender = null, RoutedEventArgs? e = null)
        {
            Pause();
        }

        private void Pause()
        {
            paused = true;
            CancelSpeech();
            document.MarkStoppedAtSpokenWord(); // resume from the word we were on
            UpdatePauseButton();
        }

        private void TogglePlayPause()
        {
            if (reader != null && !paused)
            {
                Pause();
            }
            else if (!document.IsAtEnd)
            {
                paused = false;
                SpeakCurrent();
            }
        }

        private void UpdatePauseButton()
        {
            ___PauseButton_.Content = (reader != null && !paused) ? "\u23F8 Pause" : "\u25B6 Play";
        }

        private void Window_GotFocus_1(object sender, RoutedEventArgs e)
        {
            // Retrieves data
            IDataObject iData = Clipboard.GetDataObject();
            // Is Data Text?
            if (iData.GetDataPresent(DataFormats.Text))
            {
                string[] textList = textbox.Text.Split('\n');
                string copyData = GetLongest((String)iData.GetData(DataFormats.Text));

                if (!(textList.Contains(copyData) || document.Contains(copyData)))
                {
                    string preFiltered = (String)iData.GetData(DataFormats.Text) + "\n";

                    Filters.ApplyClipboardFilters(ref preFiltered, filterSettings);

                    textbox.Text = textbox.Text + "\n" + preFiltered;
                    Clipboard.Clear();
                }
            }
        }

        /// <summary>
        /// Current repeat prevention technique (not great).
        /// More complex logic needed i.e. for each split if not in list insert (but where? after one that was?)
        /// </summary>
        private string GetLongest(string p)
        {
            string longest = "";
            foreach (string item in SentenceSplitter.Split(p))
            {
                if (item.Length > longest.Length)
                {
                    longest = item;
                }
            }
            return longest;
        }

        private void SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue == e.OldValue)
            {
                return;
            }
            rate = (int)e.NewValue;

            // GUESS: SAPI applies the new rate to the next sentence, not mid-sentence.
            if (reader != null)
            {
                reader.Rate = rate;
            }
        }

        private OcrOptions selectedOCROption = new OcrOptions();

        private void OcrOptionsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var comboBox = sender as ComboBox;
            var selectedItem = comboBox.SelectedItem;

            if (selectedItem != null)
            {
                selectedOCROption = (OcrOptions)comboBox.SelectedItem;
            }
        }

        private void ImageRead(object sender, RoutedEventArgs e)
        {
            Bitmap bitmap = CaptureScreenBehindWinodw();

            string readText, errorMessage;

            bool ocrTextReadSucceded = false;
            switch (selectedOCROption)
            {
                case OcrOptions.OneNote:
                    ocrTextReadSucceded = OneNoteOCRManager.Instance.GetTextFromImage(bitmap, out readText, out errorMessage);
                    break;
                case OcrOptions.Tesseract:
                    ocrTextReadSucceded = TesseractOCRManager.Instance.GetTextFromImage(bitmap, out readText, out errorMessage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!ocrTextReadSucceded)
            {
                textbox.Text = errorMessage;
                return;
            }

            // A sentence cut off at the bottom of the last capture continues at the top of this one.
            // GUESS: only joins when we are on or past the last sentence, as the old savedEnd did.
            string? tail = document.TakeIncompleteTail();
            if (tail != null)
            {
                readText = tail + " " + readText;
            }

            Filters.CombineLines(ref readText);
            readText = readText.Replace('\u00A1', 'i');

            Filters.ApplyOcrFilters(ref readText, filterSettings);

            textbox.Text = readText;

            //Minimize then read
            this.WindowState = System.Windows.WindowState.Minimized;
            this.ReadText(sender, e);
        }

        private Bitmap CaptureScreenBehindWinodw()
        {
            System.Drawing.Size size = new System.Drawing.Size((int)this.ActualWidth, (int)this.ActualHeight);
            System.Drawing.Point origin = new System.Drawing.Point((int)this.Left, (int)this.Top);

            // move window up for capture
            this.Top = this.Top + size.Height;

            var bitmap = Utilities.CaptureScreenInArea(origin, size);

            // move window back down
            this.Top = this.Top - size.Height;
            return bitmap;
        }

        /// <summary>
        /// Keyboard shortcuts. Handled in the preview (tunnelling) event so a focused button
        /// never also "clicks" on Space, which is what stopped the old hotkey from working.
        ///   Space                  play / pause (unless typing in the text box)
        ///   Ctrl+Space             play / pause, even while typing
        ///   Left / Right           rewind / skip (unless typing in the text box)
        ///   Ctrl+Left / Ctrl+Right rewind / skip, even while typing
        /// </summary>
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool typing = Keyboard.FocusedElement is TextBox && !ctrl;
            if (typing)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Left:
                    Rewind();
                    e.Handled = true;
                    break;
                case Key.Right:
                    Skip();
                    e.Handled = true;
                    break;
            }
        }
    }
}
