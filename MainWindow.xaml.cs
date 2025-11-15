using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using WpfIndexer.Models;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Linq;
using System;
using WpfIndexer.ViewModels;

namespace WpfIndexer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // --- ÇİFT TIKLAMA METODU (GÜNCELLENDİ) ---
        private void SearchResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // ===== DEĞİŞİKLİK BURADA =====
            // 'ListView lv' -> 'DataGrid dg' olarak değiştirildi.
            if (sender is not DataGrid dg || dg.SelectedItem is not SearchResult selected)
                return;
            // =============================

            string? path = selected.Path;

            try
            {
                // Durum 1: Arşiv içi dosya
                if (path.Contains("|"))
                {
                    var parts = path.Split(new[] { '|' }, 2);
                    string archivePath = parts[0];
                    string entryPath = parts[1];
                    string tempFile = string.Empty;

                    if (!File.Exists(archivePath))
                    {
                        MessageBox.Show($"Arşiv dosyası bulunamadı: {archivePath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string tempDir = Path.Combine(Path.GetTempPath(), "WpfIndexerPreview");
                    Directory.CreateDirectory(tempDir);

                    using (var archive = ArchiveFactory.Open(archivePath))
                    {
                        var entry = archive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace("\\", "/") == entryPath.Replace("\\", "/"));
                        if (entry == null)
                        {
                            MessageBox.Show($"Arşiv içinde dosya bulunamadı: {entryPath}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        tempFile = Path.Combine(tempDir, Path.GetFileName(entryPath));

                        // Dosya zaten varsa ve kilitsizse, üzerine yaz
                        if (File.Exists(tempFile))
                        {
                            try
                            {
                                using (FileStream fs = File.Open(tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
                                {
                                    // Dosya erişilebilir, kilitsiz. Üzerine yazılabilir.
                                }
                            }
                            catch (IOException)
                            {
                                // Dosya kilitli (muhtemelen açık). Açmayı dene.
                                Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                                return;
                            }
                        }

                        entry.WriteToFile(tempFile, new ExtractionOptions() { Overwrite = true });
                    }
                    Process.Start(new ProcessStartInfo(tempFile) { UseShellExecute = true });
                }
                // Durum 2: Normal dosya
                else
                {
                    if (!File.Exists(path))
                    {
                        MessageBox.Show($"Dosya bulunamadı: {path}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya açılamadı:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                if (DataContext is MainViewModel vm)
                {
                    vm.StatusMessage = $"Hata: {ex.Message}";
                }
            }
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
        private void SuggestionPopup_Closed(object sender, EventArgs e)
        {
            // Popup (dışarı tıklayarak vs.) kapandığında,
            // ViewModel'deki IsSuggestionsOpen özelliğini manuel olarak false yap.
            if (DataContext is WpfIndexer.ViewModels.MainViewModel vm && vm.IsSuggestionsOpen)
            {
                vm.IsSuggestionsOpen = false;
            }
        }
    }
}