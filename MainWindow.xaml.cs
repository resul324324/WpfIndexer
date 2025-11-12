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

        // --- ÇİFT TIKLAMA METODU (Orijinal kodunuzdan) ---
        private void SearchResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListView lv || lv.SelectedItem is not SearchResult selected)
                return;

            string? path = selected.Path;

            try
            {
                // Durum 1: Arşiv içi dosya (örn: "C:\arsiv.zip|metin.txt")
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

                    // Geçici klasör oluştur
                    string tempDir = Path.Combine(Path.GetTempPath(), "WpfIndexerPreview");
                    Directory.CreateDirectory(tempDir);
                    tempFile = Path.Combine(tempDir, Path.GetFileName(entryPath));

                    // Arşivi aç ve dosyayı çıkar
                    using (var archive = ArchiveFactory.Open(archivePath))
                    {
                        var entry = archive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace("\\", "/") == entryPath.Replace("\\", "/"));
                        if (entry != null)
                        {
                            entry.WriteToFile(tempFile, new ExtractionOptions() { Overwrite = true });
                            path = tempFile;
                        }
                        else
                        {
                            MessageBox.Show($"'{entryPath}' dosyası '{archivePath}' içinde bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }

                // Durum 2: Normal dosya
                if (File.Exists(path))
                {
                    // Dosyayı varsayılan uygulama ile aç
                    Process.Start(new ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show($"Dosya bulunamadı: {path}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya açılırken bir hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- ARAMA ÖNERİLERİ İÇİN EVENT HANDLER'LAR ---

        private void Suggestion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && DataContext is MainViewModel vm)
            {
                vm.SelectSuggestionCommand.Execute(item.DataContext);
                SearchTextBox.Focus();
            }
        }

        private void Suggestion_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is ListBoxItem item && DataContext is MainViewModel vm)
            {
                vm.SelectSuggestionCommand.Execute(item.DataContext);
                SearchTextBox.Focus();
            }
        }
    }
}