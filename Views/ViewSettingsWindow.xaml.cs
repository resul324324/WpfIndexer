using System.Windows;
using WpfIndexer.ViewModels; // <-- BU SATIRI EKLEYİN

namespace WpfIndexer.Views
{
    /// <summary>
    /// Interaction logic for ViewSettingsWindow.xaml
    /// </summary>
    public partial class ViewSettingsWindow : Window
    {
        // Constructor'ı (yapıcı metot) ViewModel alacak şekilde güncelleyin
        public ViewSettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();

            // Pencerenin DataContext'ini gelen viewModel olarak ayarlayın
            this.DataContext = viewModel;
        }
    }
}