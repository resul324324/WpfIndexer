using System.Windows;
using WpfIndexer.ViewModels;

namespace WpfIndexer.Views
{
    public partial class IndexCreationWindow : Window
    {
        public IndexCreationWindow()
        {
            InitializeComponent();
            Loaded += IndexCreationWindow_Loaded;
        }

        private void IndexCreationWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is IndexCreationViewModel vm)
            {
                // vm.RequestClose, penceredeki "Kapat" veya "İptal" butonları
                // (veya işlemin kendi kendini kapatması) için kullanılır.
                vm.RequestClose = (bool result) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // ******** DÜZELTME: BU SATIR KALDIRILDI ********
                        // 'this.DialogResult = result;'
                        // Pencere Show() ile (non-modal) açıldığı için DialogResult ayarlanamaz.
                        // Ana ViewModel'i bilgilendirme işini zaten 'IndexingCompletedCallback' yapıyor.
                        // ***********************************************

                        try { this.Close(); } catch { /* ignore */ }
                    });
                };
            }
        }

        public IndexCreationViewModel ViewModel
        {
            get => (IndexCreationViewModel)DataContext!;
            set => DataContext = value;
        }
    }
}