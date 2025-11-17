using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfIndexer.ViewModels;

namespace WpfIndexer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        
        private void Suggestion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && DataContext is MainViewModel vm)
            {
                vm.SelectSuggestionCommand.Execute(item.DataContext);

                // YENİ: Hangi arama kutusunun (öncesi/sonrası) aktif olduğuna
                // IsSearchPerformed durumuna göre karar ver
                if (vm.IsSearchPerformed)
                {
                    SearchTextBoxPost.Focus(); // Arama sonrası kutusu
                }
                else
                {
                    SearchTextBoxPre.Focus(); // Arama öncesi (merkezi) kutu
                }
            }
        }

        private void Suggestion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ListBoxItem item && DataContext is MainViewModel vm)
            {
                vm.SelectSuggestionCommand.Execute(item.DataContext);

                // YENİ: Hangi arama kutusunun (öncesi/sonrası) aktif olduğuna
                // IsSearchPerformed durumuna göre karar ver
                if (vm.IsSearchPerformed)
                {
                    SearchTextBoxPost.Focus(); // Arama sonrası kutusu
                }
                else
                {
                    SearchTextBoxPre.Focus(); // Arama öncesi (merkezi) kutu
                }
            }
        }
        // MainWindow.xaml.cs

        private void SuggestionPopup_Closed(object sender, EventArgs e)
        {
            if (DataContext is not WpfIndexer.ViewModels.MainViewModel vm)
                return;

            // Eğer suggestions listesi zaten kod tarafından (örn: arama yaparak) 
            // kapatıldıysa (IsSuggestionsOpen zaten false ise), tekrar false yapmaya gerek yok.
            if (!vm.IsSuggestionsOpen)
                return;

            // --- DEĞİŞİKLİK BURADA BAŞLIYOR ---
            // Sadece "o anda aktif olması gereken" popup'tan gelen 'Closed' olayını
            // dikkate al. Diğer popup'ın (pasif olanın) trigger'ı değiştiği için
            // tetiklenen 'Closed' olayını görmezden gel.

            if (sender == SuggestionPopupPre && !vm.IsSearchPerformed)
            {
                // Arama ÖNCESİ (Pre) popup kapandı (ve durum hala Arama ÖNCESİ)
                vm.IsSuggestionsOpen = false;
            }
            else if (sender == SuggestionPopupPost && vm.IsSearchPerformed)
            {
                // Arama SONRASI (Post) popup kapandı (ve durum hala Arama SONRASI)
                vm.IsSuggestionsOpen = false;
            }

        }

    }
}