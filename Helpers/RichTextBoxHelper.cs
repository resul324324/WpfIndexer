using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfIndexer.Helpers
{
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty BoundDocumentProperty =
            DependencyProperty.RegisterAttached(
                "BoundDocument",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new PropertyMetadata(null, OnBoundDocumentChanged));

        public static void SetBoundDocument(DependencyObject d, FlowDocument value)
        {
            d.SetValue(BoundDocumentProperty, value);
        }

        public static FlowDocument GetBoundDocument(DependencyObject d)
        {
            return (FlowDocument)d.GetValue(BoundDocumentProperty);
        }

        private static void OnBoundDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox rtb) return;

            var doc = e.NewValue as FlowDocument;
            rtb.Document = doc;

            if (doc == null) return;

            // Aranan kelimeye odaklanma mantığı
            rtb.Dispatcher.BeginInvoke(() =>
            {
                // YENİ: Vurgu fırçasını dinamik olarak temadan al
                var highlightBrush = (Brush)rtb.FindResource("HighlightBackgroundBrush");

                var firstMatch = doc.Blocks
                    .OfType<Paragraph>()
                    .SelectMany(p => p.Inlines.OfType<Run>())
                    // ESKİ: .FirstOrDefault(run => run.Background == Brushes.Yellow);
                    .FirstOrDefault(run => run.Background == highlightBrush); // YENİ

                if (firstMatch != null)
                {
                    firstMatch.BringIntoView();
                }
                else
                {
                    rtb.ScrollToHome();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}