using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfIndexer.Helpers
{
    public class TextToFlowDocumentHighlightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not string content || string.IsNullOrWhiteSpace(content))
                return new FlowDocument(new Paragraph(new Run("")));

            string? keyword = parameter as string;

            // FlowDocument oluştur
            FlowDocument doc = new FlowDocument
            {
                PagePadding = new Thickness(10),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 14
            };

            Paragraph p = new Paragraph();
            doc.Blocks.Add(p);

            // 1) Tüm metni TEK RUN olarak ekle
            p.Inlines.Add(new Run(content));

            if (string.IsNullOrWhiteSpace(keyword))
                return doc;

            // 2) Highlight işlemini ARKA PLANDA yap
            Task.Run(() =>
            {
                HighlightAsync(doc, keyword);
            });

            return doc;
        }

        private void HighlightAsync(FlowDocument doc, string keyword)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                TextRange range = new TextRange(doc.ContentStart, doc.ContentEnd);

                // Küçük/büyük harf duyarsız
                string text = range.Text;
                int index = 0;

                while ((index = text.IndexOf(keyword, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    TextPointer start = GetTextPointerAtOffset(doc.ContentStart, index);
                    TextPointer end = GetTextPointerAtOffset(doc.ContentStart, index + keyword.Length);

                    if (start != null && end != null)
                    {
                        var selection = new TextRange(start, end);
                        selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Yellow);
                        selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
                    }

                    index += keyword.Length;
                }
            });
        }

        // Offset destekleyici
        private TextPointer GetTextPointerAtOffset(TextPointer start, int offset)
        {
            TextPointer current = start;
            int cnt = 0;

            while (current != null)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    string textRun = current.GetTextInRun(LogicalDirection.Forward);
                    if (cnt + textRun.Length >= offset)
                        return current.GetPositionAtOffset(offset - cnt);

                    cnt += textRun.Length;
                }

                current = current.GetNextContextPosition(LogicalDirection.Forward);
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
