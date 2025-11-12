using System;
using System.Globalization;
using System.Windows.Data;

namespace WpfIndexer.Helpers
{
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not long length)
                return "0 KB";

            if (length < 1024)
                return $"{length} B";

            if (length < 1024 * 1024)
                return $"{Math.Round((double)length / 1024, 2)} KB";

            if (length < 1024 * 1024 * 1024)
                return $"{Math.Round((double)length / (1024 * 1024), 2)} MB";

            return $"{Math.Round((double)length / (1024 * 1024 * 1024), 2)} GB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}