using System.Windows;
using WpfIndexer.ViewModels;

namespace WpfIndexer.Views
{
    public partial class IndexUpdateWindow : Window
    {
        public IndexUpdateWindow()
        {
            InitializeComponent();
            Loaded += IndexUpdateWindow_Loaded;
        }

        private void IndexUpdateWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is IndexUpdateViewModel vm)
            {
                vm.RequestClose = (bool result) =>
                {
                    Application.Current.Dispatcher.Invoke(() => this.Close());
                };
            }
        }

        public IndexUpdateViewModel ViewModel
        {
            get => (IndexUpdateViewModel)DataContext!;
            set => DataContext = value;
        }
    }
}