using Microsoft.Office.Interop.OneNote;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TextToSpeech
{
    public class OneNoteOCRManager : Singleton<OneNoteOCRManager>
    {
        private string strID, strXML, notebookXml;
        private string pageToBeChange = "SandboxPage";

        Microsoft.Office.Interop.OneNote.Application app;

        private string? existingPageId;
        XNamespace ns;

        public OneNoteOCRManager() : base()
        {
            app = new Microsoft.Office.Interop.OneNote.Application();


            //app.OpenHierarchy(@"C:\Users\kjlue_000\Documents\OneNote Notebooks\OCRSandbox\Ocr.one",
            //    System.String.Empty, out strID, CreateFileType.cftNone);
            app.GetHierarchy(null, HierarchyScope.hsPages, out notebookXml);
            var doc = XDocument.Parse(notebookXml);
            ns = doc.Root.Name.Namespace;
            var pageNode = doc.Descendants(ns + "Page").Where(n => n.Attribute("name").Value == pageToBeChange).FirstOrDefault();
            existingPageId = pageNode.Attribute("ID").Value;
        }

        public bool GetTextFromImage(Bitmap bitmap, out string readText, out string errorMessage)
        {
            MemoryStream stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Jpeg);
            string fileString = Convert.ToBase64String(stream.ToArray());

            String strImportXML;

            strImportXML = "<?xml version=\"1.0\"?>" +
            "<one:Page xmlns:one=\"http://schemas.microsoft.com/office/onenote/2013/onenote\" ID=\"" + existingPageId + "\">" + //{D2954871-2111-06B9-1AB9-882CD62848AA}{1}{E1833485368852652557020163191444754720811741}\">" +
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
            XDocument doc = XDocument.Parse(strXML);
            int timeoutCounter = 0;
            while (doc.Descendants(ns + "OCRText").FirstOrDefault() == null)
            {
                System.Threading.Thread.Sleep(200);
                app.GetPageContent(existingPageId, out strXML);
                doc = XDocument.Parse(strXML);
                timeoutCounter++;
                if (timeoutCounter > 30)
                {
                    readText = null;
                    errorMessage = "OneNote timed out texify-ing image! try again? maybe?...";
                    return false;
                }
            }
            readText = doc.Descendants(ns + "OCRText").FirstOrDefault().Value;

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

            errorMessage = null;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            app = null;

            base.Dispose(disposing);
        }
    }
}
