


using System;
using System.Drawing;

using Patagames.Ocr;
using Patagames.Ocr.Enums;

namespace TextToSpeech
{
    public class OCRManager : Singleton<OCRManager>
    {
        private OcrApi ocrApi { get; set; }

        private OCRManager() : base ()
        {
            ocrApi = OcrApi.Create();
            ocrApi.Init(Languages.English);
        }

        public bool GetTextFromImage(Bitmap bitmap, out string readText, out string errorMessage)
        {
            readText = null;

            try
            {
                readText = ocrApi.GetTextFromImage(bitmap);
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                return false;
            }

            errorMessage = null;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            ocrApi.Dispose();

            base.Dispose(disposing); 
        }
    }
}