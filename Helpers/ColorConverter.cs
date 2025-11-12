using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfIndexer.Helpers
{
    public class ColorConverter : IValueConverter
    {
        public static ColorConverter Instance = new ColorConverter();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                Color color = brush.Color;
                float factor = float.Parse(parameter?.ToString() ?? "0", CultureInfo.InvariantCulture);

                // Rengi koyulaştır (factor < 0) veya aç (factor > 0)
                float r = (float)color.R;
                float g = (float)color.G;
                float b = (float)color.B;

                if (factor < 0)
                {
                    factor = 1 + factor;
                    r *= factor;
                    g *= factor;
                    b *= factor;
                }
                else
                {
                    r = (255 - r) * factor + r;
                    g = (255 - g) * factor + g;
                    b = (255 - b) * factor + b;
                }

                return new SolidColorBrush(Color.FromRgb((byte)r, (byte)g, (byte)b));
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}