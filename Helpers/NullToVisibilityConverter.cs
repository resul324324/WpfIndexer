using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfIndexer.Helpers
{
    /// <summary>
    /// Bir nesnenin 'null' olup olmamasına göre Visibility.Visible veya Visibility.Collapsed döndürür.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isNull = (value == null);
            bool invert = parameter?.ToString()?.Equals("invert", StringComparison.OrdinalIgnoreCase) ?? false;

            if (invert)
            {
                // İnvert: Null ise GÖSTER
                return isNull ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                // Normal: Null ise GİZLE
                return isNull ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}