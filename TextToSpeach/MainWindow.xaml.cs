using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Input;
using System.Speech.Synthesis;
using Microsoft.Office.Interop.OneNote;


namespace TextToSpeach
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

        public MainWindow()
        {
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
                    reader.SpeakProgress += reader_SpeakProgress;
                    reader.SpeakAsync(readList.Peek());
                    Dispatcher.Invoke(UpdateTextBlock);
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
            UpdateTextBlock();
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

        /// <summary>
        /// Updates the current textBlock list of things to say
        /// </summary>
        private void UpdateTextBlock()
        {
            lock (readLock)
            {
                __ReadListDisplay__.Text = "";
                foreach (string item in readList)
                {
                    __ReadListDisplay__.Text = __ReadListDisplay__.Text + item + "\n";
                }
            }
        }

        private void CreateReader()
        {
            reader = new SpeechSynthesizer();
            reader.Rate = rate;
            reader.SpeakCompleted += ReadList;
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

        private void ImageRead(object sender, RoutedEventArgs e)
        {
            string strID, strXML, notebookXml;
            string pageToBeChange = "SandboxPage";
            Microsoft.Office.Interop.OneNote.Application app = new Microsoft.Office.Interop.OneNote.Application();
            //app.OpenHierarchy(@"C:\Users\kjlue_000\Documents\OneNote Notebooks\OCRSandbox\Ocr.one",
            //    System.String.Empty, out strID, CreateFileType.cftNone);
            app.GetHierarchy(null, HierarchyScope.hsPages, out notebookXml);
            var doc = XDocument.Parse(notebookXml);
            var ns = doc.Root.Name.Namespace;
            var pageNode = doc.Descendants(ns + "Page").Where(n => n.Attribute("name").Value == pageToBeChange).FirstOrDefault();
            var existingPageId = pageNode.Attribute("ID").Value;

            Bitmap bitmap = ScreenCapture();

            MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            string fileString = Convert.ToBase64String(stream.ToArray());

            String strImportXML;

            strImportXML = "<?xml version=\"1.0\"?>" +
            "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\""+existingPageId+"\">"+ //{D2954871-2111-06B9-1AB9-882CD62848AA}{1}{E1833485368852652557020163191444754720811741}\">" +
            "    <one:PageSettings RTL=\"false\" color=\"automatic\">" +
            "        <one:PageSize>" +
            "            <one:Automatic/>" +
            "        </one:PageSize>" +
            "        <one:RuleLines visible=\"false\"/>" +
            "    </one:PageSettings>" +
            "    <one:Title style=\"font-family:Calibri;font-size:17.0pt\" lang=\"en-US\">" +
            "        <one:OE alignment=\"left\">" +
            "            <one:T>" +
            "                <![CDATA[SandboxPage]]>" +
            "            </one:T>" +
            "        </one:OE>" +
            "    </one:Title>" +
            "    <one:Outline >" +
            "        <one:Position x=\"20\" y=\"50\"/>" +
            "        <one:Size width=\"" + bitmap.Width + "\" height=\"" + bitmap.Height + "\"  isSetByUser=\"true\"/>" +
            "        <one:OEChildren>" +
            "            <one:OE alignment=\"left\">" +
            //"                <one:T>" +
            "    <one:Image> <one:Data>" + fileString + "</one:Data></one:Image>" +
                //"                    <![CDATA[Sample Text]]>" +
            //"                </one:T>" +
            "            </one:OE>" +
            "        </one:OEChildren>" +
            "    </one:Outline>" +
            "</one:Page>";
            app.UpdatePageContent(strImportXML);

            //app.SyncHierarchy(strID);

            //Give one note some time to ocr the texts
            app.GetPageContent(existingPageId, out strXML);
            doc = XDocument.Parse(strXML);
            int timeoutCounter = 0;
            while (doc.Descendants(ns + "OCRText").FirstOrDefault() == null)
            {
                System.Threading.Thread.Sleep(200);
                app.GetPageContent(existingPageId, out strXML);
                doc = XDocument.Parse(strXML);
                timeoutCounter++;
                if (timeoutCounter > 30)
                {
                    textbox.Text = "OneNote timed out texify-ing image! try again? maybe?...";
                    return;
                }
            }
            string readText = doc.Descendants(ns + "OCRText").FirstOrDefault().Value;

            if (savedEnd != null)
            {
                readText = savedEnd + " " + readText;
                savedEnd = null;
            }

            Filters.CombineLines(ref readText);
            readText = readText.Replace('¡', 'i');

            Filters.PsychologyFilter(ref readText);

            textbox.Text = readText;

            //Empty Page (I.E. Cleanup)
            doc = XDocument.Parse(strXML);
            var imageXML = doc.Descendants(ns + "Outline");
            foreach (var item in imageXML)
            {
                string outlineID = item.Attribute("objectID").Value;
                if (outlineID != null)
                {
                    app.DeletePageContent(existingPageId, outlineID);
                }
            }

            //Minimize then read
            this.WindowState = System.Windows.WindowState.Minimized;
            this.ReadText(sender, e);

        }

        private Bitmap ScreenCapture()
        {
            System.Drawing.Size size = new System.Drawing.Size((int)this.ActualWidth, (int)this.ActualHeight);

            //System.Windows.Point pointOrigin = this.PointToScreen(new System.Windows.Point(0, 0));
            System.Drawing.Point dOrigin = new System.Drawing.Point((int)this.Left, (int)this.Top);

            //this.WindowState = System.Windows.WindowState.Minimized;
            this.Top = this.Top + size.Height;
            
            Bitmap bitmap = new Bitmap(size.Width, size.Height);
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(dOrigin, new System.Drawing.Point(0, 0), size);
                }
            }
            //System.IO.FileStream fileStream = new System.IO.FileStream(@"C:\Users\kjlue_000\Desktop\Scratch\clip.jpg", System.IO.FileMode.Create);
            //if (fileStream != null)
            //{
            //    bitmap.Save(fileStream, System.Drawing.Imaging.ImageFormat.Jpeg);
            //    fileStream.Close();
            //}

            //this.WindowState = System.Windows.WindowState.Normal;
            this.Top = this.Top - size.Height;
            return bitmap;
        }

        // TODO: make program current position knowledgeable, 
        //   so you can save position and move around in large text files


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
