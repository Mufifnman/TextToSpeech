using System.Globalization;
using System.Windows.Data;

namespace TextToSpeech
{
    /// <summary>
    /// OCR engines the user can pick from. The first entry is the default.
    /// </summary>
    public enum OcrOptions
    {
        /// <summary>Built-in Windows OCR (Windows.Media.Ocr). Fast, no extra install, uses the OS language packs.</summary>
        Windows,
        /// <summary>Tesseract 5 LSTM with OpenCV preprocessing. Works offline from the bundled tessdata folder.</summary>
        Tesseract
    }

    public class EnumToEnumerableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not Type enumType || !enumType.IsEnum)
                throw new ArgumentException("Type provided must be an Enum.", nameof(value));

            return Enum.GetValues(enumType);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
