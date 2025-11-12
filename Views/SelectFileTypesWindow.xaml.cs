using System.Windows;

namespace WpfIndexer.Views
{
    public partial class SelectFileTypesWindow : Window
    {
        public SelectFileTypesWindow()
        {
            InitializeComponent();
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            // Devam'a basıldığında DialogResult'u true yap
            // Bu, MainViewModel'in seçimin onaylandığını anlamasını sağlar
            this.DialogResult = true;
        }
    }
}