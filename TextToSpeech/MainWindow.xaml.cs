

using System.Drawing;
using System.Windows;
using System.Windows.Input;
using TextToSpeech.Speech;
using System.Text;

using System.Windows.Controls;


namespace TextToSpeech
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : System.Windows.Window
    {
        // objects
        VoiceCatalog catalog = new VoiceCatalog();
        AppSettings settings = AppSettings.Load();
        VoiceInfo selectedVoice = null;
        CancellationTokenSource readCts = null;
        bool isReading = false;
        Queue<string> readList = new Queue<string>();
        Object readLock = new Object();
        int rate = 4;
        bool loadingVoices = false;
        string savedEnd = null;
        StringBuilder sb = new StringBuilder();

        ReadingTextViewModel readingTextView = new ReadingTextViewModel();

        public MainWindow()
        {
            this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            DataContext = readingTextView;
            int savedRate = settings.Rate;
            InitializeComponent();
            __Speed__.Value = savedRate; // the XAML default fires SpeedChanged first, so set it after
            RefreshVoices();
            Closed += (s, e) => { StopRead(); catalog.Dispose(); };
        }

        /// <summary>Rebuilds the voice picker from what is installed, keeping the current choice if it still exists.</summary>
        private void RefreshVoices()
        {
            loadingVoices = true;
            try
            {
                catalog.Refresh();
                string wanted = selectedVoice?.Id ?? settings.VoiceId;
                voiceComboBox.ItemsSource = catalog.Voices;
                selectedVoice = catalog.PickInitial(wanted);
                voiceComboBox.SelectedItem = selectedVoice;
                if (catalog.LastError != null) voiceComboBox.ToolTip = catalog.LastError;
            }
            finally
            {
                loadingVoices = false;
            }
        }

        private void VoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (loadingVoices) return;
            if (voiceComboBox.SelectedItem is VoiceInfo v)
            {
                selectedVoice = v;
                settings.VoiceId = v.Id;
                settings.Save();
                // takes effect from the next sentence
            }
        }

        private void MoreVoices_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VoiceDownloadWindow { Owner = this };
            dialog.ShowDialog();
            RefreshVoices();
        }

        private void ReadText(object sender, RoutedEventArgs e)
        {
            lock (readLock)
            {
                if (this.textbox.Text != "")
                {
                    string temp = textbox.Text;
                    Filters.CombineLines(ref temp);
                    foreach (string item in temp.Split('\n','.')) // TODO: think about what to do about '?','!'
                    {
                        if (!item.ToLower().Contains("read more at")) // cut out read more at
                        {
                            readList.Enqueue(item);
                        }
                    }
                    textbox.Text = "";
                }
                if (!isReading) // if we aren't already reading
                {
                    StartReading();
                }
            }
            UpdateTextBlock();
        }

        /// <summary>
        /// Speaks the queue one sentence at a time until it is empty or stopped. Runs on the UI thread between awaits.
        /// A sentence stays at the head of the queue until it has finished, so Stop then Go repeats it.
        /// </summary>
        private async void StartReading()
        {
            var cts = new CancellationTokenSource();
            readCts = cts;
            isReading = true;
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    string line;
                    lock (readLock)
                    {
                        if (readList.Count == 0) break;
                        line = readList.Peek();
                        if (1 == readList.Count)
                        {
                            savedEnd = line;
                        }
                    }

                    StringToXamlConverter.currentWordIndex = shouldUnderlineWord ? 0 : -1;
                    UpdateTextBlock();

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        await SpeakWithFallback(line, cts.Token);
                        if (cts.IsCancellationRequested) return; // stopped: keep the line queued
                    }

                    lock (readLock)
                    {
                        if (readList.Count > 0) readList.Dequeue();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (readCts == cts)
                {
                    isReading = false;
                    readCts = null;
                }
            }

            if (!cts.IsCancellationRequested)
            {
                UpdateTextBlock();
                // Maximize on done so next image can be scanned or line can be coppied
                if (this.WindowState == System.Windows.WindowState.Minimized)
                {
                    this.WindowState = System.Windows.WindowState.Normal;
                }
            }
        }

        /// <summary>Speaks with the chosen voice; if a neural voice fails, falls back to a Windows voice instead of going silent.</summary>
        private async Task SpeakWithFallback(string line, CancellationToken token)
        {
            VoiceInfo voice = selectedVoice;
            try
            {
                await catalog.EngineFor(voice).SpeakAsync(line, voice, rate, offset => OnWord(line, offset), token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                VoiceInfo fallback = catalog.DefaultFallbackVoice;
                if (voice != null && voice.IsNeural && fallback != null)
                {
                    readingTextView.Status = "Neural voice failed (" + ex.Message + "); using " + fallback.DisplayName;
                    selectedVoice = fallback;
                    loadingVoices = true;
                    voiceComboBox.SelectedItem = fallback;
                    loadingVoices = false;
                    await catalog.EngineFor(fallback).SpeakAsync(line, fallback, rate, offset => OnWord(line, offset), token);
                }
                else
                {
                    readingTextView.Status = "Speech failed: " + ex.Message;
                }
            }
        }

        private void OnWord(string line, WordBoundary boundary)
        {
            if (!shouldUnderlineWord) return;
            int index = WordIndexAt(line, boundary.CharOffset);
            Dispatcher.BeginInvoke(() =>
            {
                StringToXamlConverter.currentWordIndex = index;
                readingTextView.Text = sb.ToString();
            });
        }

        /// <summary>Zero based index of the whitespace separated word that starts at (or contains) charOffset.</summary>
        private static int WordIndexAt(string text, int charOffset)
        {
            int index = -1;
            bool inWord = false;
            for (int i = 0; i < text.Length && i <= charOffset; i++)
            {
                bool ws = char.IsWhiteSpace(text[i]);
                if (!ws && !inWord) index++;
                inWord = !ws;
            }
            return Math.Max(index, 0);
        }

        /// <summary>
        ///  clears the list of items to be read
        /// </summary>
        private void ClearList(object sender = null, RoutedEventArgs e = null)
        {
            StopRead();
            lock (readLock)
            {
                readList = new Queue<string>();
                UpdateTextBlock();
            }
            savedEnd = null;
        }

        private void StopRead(object sender = null, RoutedEventArgs e = null)
        {
            var cts = readCts;
            readCts = null;
            isReading = false;
            cts?.Cancel();
        }

        private void Window_GotFocus_1(object sender, RoutedEventArgs e)
        {
            // Retrieves data
            IDataObject iData = Clipboard.GetDataObject();
            // Is Data Text?
            lock (readLock)
            {
                if (iData.GetDataPresent(DataFormats.Text))
                {
                    string[] textList = textbox.Text.Split('\n');
                    string copyData = GetLongest((String)iData.GetData(DataFormats.Text));

                    if (!(textList.Contains(copyData) || readList.Contains(copyData)))
                    {
                        string preFiltered = (String)iData.GetData(DataFormats.Text) + "\n";

                        Filters.WikipediaCitation(ref preFiltered);
                        Filters.GovernmentFilter(ref preFiltered);
                        Filters.AHITStarredQuestion(ref preFiltered);

                        textbox.Text = textbox.Text + "\n" + preFiltered;
                        Clipboard.Clear();
                    }
                }
            }
        }

        /// <summary>
        /// Current repeat prevention technique (not great).
        /// More complex logic needed i.e. for each split if not in list insert (but where? after one that was?)
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private string GetLongest(string p)
        {
            string longest = "";
            foreach (string item in p.Split('\n','.'))
            {
                if (item.Length > longest.Length)
                {
                    longest = item;
                }
            }
            return longest;
        }

        bool shouldUnderlineWord = true;
        // TODO: make program current position knowledgeable, 
        //   so you can save position and move around in large text files
        //   should be possible in the string to xaml converter (maybe make it a string array to xaml converter?
        //   could then have 'buttons' for all the text with alternating gray/white backgrounds 
        //   would need to figure out how to keep to what was being read in the scrolled view
        //      but break this until we hit read again if scrolling happens in that area 
        //
        //   buttons would rewind to that section of the text we want to read
        //   
        //   All this would require a refactor of the reading methods to remove the queue crap
        //   we'd need to like clear whenever we finished reading as well


        /// <summary>
        /// Updates the current textBlock list of things to say
        /// </summary>
        private void UpdateTextBlock()
        {
            lock (readLock)
            {
                if (readList.Count <= 0)
                {
                    // nothing to update
                    readingTextView.Text = string.Empty;
                    return;
                }

                sb.Clear();

                // todo: move this to a faster redraw method (also a string to XAMLConverter todo)
                sb.Append(StringToXamlConverter.BeginHighlightToken);
                string line = readList.Peek();
                sb.Append(line);
                sb.Append(StringToXamlConverter.EndHighlightToken);

                foreach (string item in readList.Skip(1))
                {
                    //sb.AppendLine();
                    sb.Append(item);
                }
                readingTextView.Text = sb.ToString();
            }
        }

        private void SpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue == e.OldValue)
            {
                return;
            }
//            lock (readLock)
  //          {
                rate = (int)e.NewValue;

                catalog.EngineFor(selectedVoice).SetRate(rate);
                settings.Rate = rate;
                settings.Save();
    //        }
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

            if (savedEnd != null)
            {
                readText = savedEnd + " " + readText;
                savedEnd = null;
            }

            Filters.CombineLines(ref readText);
            readText = readText.Replace('¡', 'i');

            Filters.PsychologyFilter(ref readText);

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

        private void Window_KeyDown_1(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                TogglePlayPause();
            }
        }

        private void TogglePlayPause()
        {
            var engine = catalog.EngineFor(selectedVoice);
            if (engine.IsPaused) engine.Resume();
            else engine.Pause();
        }
    }
}
