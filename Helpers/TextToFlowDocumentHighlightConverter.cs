using System;
using System.Globalization;
using System.Windows; // YENİ
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfIndexer.Helpers
{
    // DİKKAT: Artık IMultiValueConverter değil, IValueConverter
    public class TextToFlowDocumentHighlightConverter : IValueConverter
    {
        // Lucene Highlighter'ın kullandığı (veya bizim ayarladığımız) etiketler
        private const string HighlightStartTag = "<B>";
        private const string HighlightEndTag = "</B>";

        // KALDIRILDI: Vurgu renkleri kaldırıldı, artık XAML'dan geliyor.
        // private static readonly SolidColorBrush HighlightBackground = Brushes.Yellow;
        // private static readonly SolidColorBrush HighlightForeground = Brushes.Black;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var taggedText = value as string;

            var doc = new FlowDocument();
            var para = new Paragraph();

            // DİKKAT: RichTextBox'ın varsayılan yazı rengini temadan alması için
            // FlowDocument'un kendisine Foreground ataması yapmalıyız.
            // Bu, 'Brushes.Black' kullandığımızda bile normal metnin
            // tema renginde (örn. beyaz) kalmasını sağlar.
            doc.SetResourceReference(FlowDocument.ForegroundProperty, "TextColor");

            doc.Blocks.Add(para);

            if (string.IsNullOrEmpty(taggedText))
            {
                return doc;
            }

            int currentIndex = 0;
            try
            {
                while (currentIndex < taggedText.Length)
                {
                    // 1. Vurgulama başı ara (<B>)
                    int startIndex = taggedText.IndexOf(HighlightStartTag, currentIndex, StringComparison.OrdinalIgnoreCase);

                    if (startIndex == -1)
                    {
                        // Tag yok, kalan metni normal ekle ve bitir
                        para.Inlines.Add(new Run(taggedText.Substring(currentIndex)));
                        break;
                    }

                    // 2. Tag'den önceki normal metni ekle
                    string normalText = taggedText.Substring(currentIndex, startIndex - currentIndex);
                    if (!string.IsNullOrEmpty(normalText))
                    {
                        para.Inlines.Add(new Run(normalText));
                    }

                    // 3. Vurgulama sonu ara (</B>)
                    int endIndex = taggedText.IndexOf(HighlightEndTag, startIndex + HighlightStartTag.Length, StringComparison.OrdinalIgnoreCase);
                    if (endIndex == -1)
                    {
                        // Başlangıç var ama son yok (hata), kalanı normal ekle ve bitir
                        para.Inlines.Add(new Run(taggedText.Substring(startIndex)));
                        break;
                    }

                    // 4. Vurgulanacak metni al (tag'ler arası)
                    string highlightedText = taggedText.Substring(
                        startIndex + HighlightStartTag.Length,
                        endIndex - (startIndex + HighlightStartTag.Length));

                    // 5. Vurgulu metni ekle
                    para.Inlines.Add(new Run(highlightedText)
                    {
                        // YENİ: Renkleri XAML'dan dinamik olarak al
                        Background = (Brush)Application.Current.FindResource("HighlightBackgroundBrush"),
                        FontWeight = System.Windows.FontWeights.Bold,
                        Foreground = (Brush)Application.Current.FindResource("HighlightForegroundBrush")
                    });

                    // 6. İndeksi güncelle
                    currentIndex = endIndex + HighlightEndTag.Length;
                }
            }
            catch (Exception)
            {
                // Parser'da bir hata olursa, en azından çökmesin, düz metni göstersin
                para.Inlines.Clear();
                para.Inlines.Add(new Run(taggedText));
            }

            return doc;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}