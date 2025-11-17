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
                // Pencere non-modal açıldığından DialogResult kullanılmaz; kapanma isteği pencereyi kapatır.
                vm.RequestClose = (bool result) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
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