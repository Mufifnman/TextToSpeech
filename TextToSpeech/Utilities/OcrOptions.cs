using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace TextToSpeech
{
    public enum OcrOptions
    {
        OneNote,
        Tesseract
    }

    public class EnumToEnumerableConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Type enumType = value as Type;
            if (enumType == null || !enumType.IsEnum)
                throw new ArgumentException("Type provided must be an Enum.", nameof(enumType));

            var values = Enum.GetValues(enumType);
            return values;
        }

        //public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        //{
        //    return Enum.GetValues(value.GetType());
        //}

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
