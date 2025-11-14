using System.Windows;
using WpfIndexer.ViewModels; // <-- BU SATIRI EKLEYİN
using WpfIndexer.Models;
using WpfIndexer.Services;

namespace WpfIndexer.Views
{
    public partial class SettingsWindow : Window
    {
        // Constructor'ı (yapıcı metot) ViewModel alacak şekilde güncelleyin
        public SettingsWindow(SettingsViewModel viewModel)
        {
            InitializeComponent();

            // Pencerenin DataContext'ini gelen viewModel olarak ayarlayın
            this.DataContext = viewModel;
        }
    }
}