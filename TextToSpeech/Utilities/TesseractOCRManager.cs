
using System.Drawing;

using Tesseract;

using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;

namespace TextToSpeech
{
    public class TesseractOCRManager : Singleton<TesseractOCRManager>
    {
        private TesseractEngine tesseractEngine { get; set; }

        private TesseractOCRManager() : base ()
        {
            tesseractEngine = new TesseractEngine(@"./tessdata", "eng", EngineMode.LstmOnly);
            tesseractEngine.DefaultPageSegMode = PageSegMode.Auto;
        }

        public bool GetTextFromImage(Bitmap bitmap, out string readText, out string errorMessage)
        {
            readText = null;

            try
            {
                using (var page = tesseractEngine.Process(PreprocessImageForOCR(bitmap)))
                {
                    readText = page.GetText();
                }
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
                return false;
            }

            errorMessage = null;
            return true;
        }

        public Bitmap PreprocessImageForOCR(Bitmap bitmap)
        {
            // Convert Bitmap to Emgu.CV Image
            Image<Bgr, byte> colorImage = bitmap.ToImage<Bgr, byte>();

            // Convert to grayscale
            Image<Gray, byte> grayImage = colorImage.Convert<Gray, byte>();

            // Apply Gaussian Blur for noise reduction
            grayImage._SmoothGaussian(3);

            // Binarize the image
            grayImage = grayImage.ThresholdBinary(new Gray(128), new Gray(255));

            // Resize the image for better OCR accuracy
            const double bestDPIForOCR = 300.0d;
            grayImage = grayImage.Resize(bestDPIForOCR / bitmap.HorizontalResolution, Inter.Cubic);

            // Optionally, apply other preprocessing steps like deskewing, rotating, etc.
            
            // Return as Bitmap for OCR
            return grayImage.ToBitmap();
        }

        protected override void Dispose(bool disposing)
        {
            tesseractEngine.Dispose();

            base.Dispose(disposing); 
        }
    }
}