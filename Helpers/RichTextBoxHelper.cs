using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WpfIndexer.Helpers
{
    public class RichTextBoxHelper : DependencyObject
    {
        public static readonly DependencyProperty BoundDocumentProperty =
            DependencyProperty.RegisterAttached(
                "BoundDocument",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new PropertyMetadata(null, OnBoundDocumentChanged));

        public static FlowDocument GetBoundDocument(DependencyObject obj)
        {
            return (FlowDocument)obj.GetValue(BoundDocumentProperty);
        }

        public static void SetBoundDocument(DependencyObject obj, FlowDocument value)
        {
            obj.SetValue(BoundDocumentProperty, value);
        }

        private static void OnBoundDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox rtb) return;

            // --- SANALLAŞTIRMA DÜZELTMESİ ---
            // Yeni belgeyi atamadan önce, RichTextBox'ın mevcut belgesini serbest bırak.
            rtb.Document = new FlowDocument();
            // --- DÜZELTME SONU ---

            var doc = e.NewValue as FlowDocument;
            rtb.Document = doc; // Bu satır artık güvenlidir

            if (doc == null) return;

            // Aranan kelimeye odaklanma mantığı
            rtb.Dispatcher.BeginInvoke(() =>
            {
                // Vurgu fırçasını dinamik olarak temadan al
                var highlightBrush = (Brush)rtb.FindResource("HighlightBackgroundBrush");

                var firstMatch = doc.Blocks
                    .OfType<Paragraph>()
                    .SelectMany(p => p.Inlines.OfType<Run>())
                    .FirstOrDefault(run => run.Background == highlightBrush);

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