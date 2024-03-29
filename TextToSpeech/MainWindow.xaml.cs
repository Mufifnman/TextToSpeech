﻿

using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Speech.Synthesis;
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
        SpeechSynthesizer reader = null;
        Queue<string> readList = new Queue<string>();
        Object readLock = new Object();
        int rate = 4;
        string savedEnd = null;
        StringBuilder sb = new StringBuilder();

        ReadingTextViewModel readingTextView = new ReadingTextViewModel();

        public MainWindow()
        {
            this.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
            DataContext = readingTextView;
            InitializeComponent();
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
                if (reader == null) // if we aren't already reading
                {
                    ReadList();
                }
            }
            UpdateTextBlock();
        }

        /// <summary>
        /// This is a recursive call
        /// </summary>
        /// <param name="sender">
        /// Must be null on first call for it to function properly
        /// todo: /\ chnge that and arguments /\
        /// currently garenteed to be called on main thread
        /// </param>
        /// <param name="e"></param>
        private void ReadList(object sender = null, SpeakCompletedEventArgs e = null)
        {
            lock (readLock)
            {
                if (sender != null) // first call
                {
                    if (0 < readList.Count)
                    {
                        readList.Dequeue();
                    }
                }
                else
                {
                    CreateReader();
                }
                if (0 < readList.Count && reader != null) // one because not dequeued until after this
                {
                    wordCount = 0;
                    if (shouldUnderlineWord)
                    {
                        StringToXamlConverter.currentWordIndex = wordCount;
                    }
                    else
                    {
                        StringToXamlConverter.currentWordIndex = -1;
                    }
                    reader.SpeakAsync(readList.Peek());
                    UpdateTextBlock();
                    if (1 == readList.Count)
                    {
                        savedEnd = readList.Peek();
                    }
                }
                else
                {
                    if (reader != null)
                    {
                        reader.Dispose();
                        reader = null; // to keep track of the fact that we aren't currently readings
                    }

                    // Maximize on done so next image can be scanned or line can be coppied
                    if (this.WindowState == System.Windows.WindowState.Minimized)
                    {
                        this.WindowState = System.Windows.WindowState.Normal;
                    }
                }
            }
        }

        int wordCount = 0;
        private void reader_SpeakProgress(object sender, SpeakProgressEventArgs e)
        {
            wordCount++;
            if (shouldUnderlineWord)
            {
                StringToXamlConverter.currentWordIndex = wordCount;
                readingTextView.Text = sb.ToString();
                //readingTextView.NotifyPropertyChanged();
                //UpdateTextBlock();
            }
        }

        /// <summary>
        ///  clears the list of items to be read
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
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
            // Initialize new Reader
            lock (readLock)
            {
                if (reader != null)
                {
                    reader.Pause();
                    reader.Dispose();
                    reader = null;
                }
            }
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

        private void CreateReader()
        {
            reader = new SpeechSynthesizer();
            reader.Rate = rate;
            reader.SpeakCompleted += 
                (sender, e) =>
                {
                    Dispatcher.Invoke(() => ReadList(sender, e));
                };
            reader.SpeakProgress += reader_SpeakProgress;
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

                if (reader != null)
                {
                    reader.Rate = (int)e.NewValue;
                }
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

        // TODO: make this actually work whenever you press [space] or find another solution.
        private void TogglePlayPause()
        {
            lock (readLock)
            {
                if (reader != null)
                {
                    if (reader.State == SynthesizerState.Speaking)
                    {
                        reader.Pause();
                    }
                    else if (reader.State == SynthesizerState.Paused)
                    {
                        reader.Resume();
                    }
                }
            }
        }
    }
}
