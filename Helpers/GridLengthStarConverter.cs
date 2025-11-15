using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WpfIndexer.Helpers
{
    /// <summary>
    /// Bir 'double' oranı (örn: 0.7) '7*' ve '3*' gibi Grid.ColumnDefinition Width değerlerine çevirir.
    /// ConverterParameter="Results" (oranı) veya "Preview" (1-oranı) alır.
    /// </summary>
    public class GridLengthStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not double ratio)
                ratio = 0.5; // Varsayılan

            string part = parameter?.ToString() ?? "Results";

            if (part == "Preview")
            {
                // Önizleme paneli için (1.0 - oran)
                return new GridLength(1.0 - ratio, GridUnitType.Star);
            }

            // Sonuçlar paneli için (oran)
            return new GridLength(ratio, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Bu, MainWindow'da TwoWay bağlandığı için GEREKLİDİR.
            // GridSplitter tarafından değiştirilen GridLength'i geri 'double' oranına çevirir.
            if (value is not GridLength length || !length.IsStar)
                return 0.5;

            string part = parameter?.ToString() ?? "Results";

            // Not: Bu kısım, her iki sütunun da aynı anda güncellenmesine bağlı
            // olduğu için tam bir 'ConvertBack' sağlamak karmaşıktır.
            // Genellikle 'ConvertBack' sadece BİR sütun için (örn. "Results") uygulanır.
            // Şimdilik, 'Results' paneli değiştiğinde oranı geri döndürelim.
            if (part == "Results")
            {
                return length.Value;
            }
            else
            {
                return 1.0 - length.Value;
            }
            // İdeal bir senaryo için bu mantığın ViewModel'de olması gerekir,
            // ancak TwoWay bağlama için bu basit 'ConvertBack' yeterli olacaktır.
        }
    }
}