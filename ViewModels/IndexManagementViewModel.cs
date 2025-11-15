using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WpfIndexer.Helpers;
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.Views;
using System.Threading.Tasks; // YENİ

namespace WpfIndexer.ViewModels
{
    public class IndexManagementViewModel : INotifyPropertyChanged
    {
        private readonly IIndexService _indexService;
        private readonly IndexSettingsService _settingsService;
        private readonly ILogger<IndexManagementViewModel> _logger;
        private readonly ILogger<IndexUpdateViewModel> _updateLogger;

        public IndexSettingsService SettingsService => _settingsService;
        public IndexDefinition? SelectedIndex { get; set; }

        public ICommand CreateIndexCommand { get; }
        public ICommand AddExistingIndexCommand { get; }
        public ICommand DeleteIndexCommand { get; }
        public ICommand UpdateIndexCommand { get; }

        // YENİ: Yükleme durumunu göstermek için
        private bool _isLoadingMetadata;
        public bool IsLoadingMetadata
        {
            get => _isLoadingMetadata;
            set { _isLoadingMetadata = value; OnPropertyChanged(); }
        }

        public IndexManagementViewModel(
            IIndexService indexService,
            IndexSettingsService settingsService,
            ILogger<IndexManagementViewModel> logger,
            ILogger<IndexUpdateViewModel> updateLogger)
        {
            _indexService = indexService;
            _settingsService = settingsService;
            _logger = logger;
            _updateLogger = updateLogger;

            CreateIndexCommand = new RelayCommand(_ => CreateIndex(), _ => true);
            AddExistingIndexCommand = new RelayCommand(_ => AddExistingIndexAsync(), _ => true);
            DeleteIndexCommand = new RelayCommand(_ => DeleteIndexAsync(), _ => SettingsService.Indexes.Any(i => i.IsSelected));
            UpdateIndexCommand = new RelayCommand(async _ => await UpdateIndexAsync(), _ => SettingsService.Indexes.Any(i => i.IsSelected && !string.IsNullOrEmpty(i.SourcePath)));

            // YENİ: Constructor'da async metot çağrısı (ateşle ve unut)
            // Bu, UI'ı kilitlemeden meta verileri arka planda yükler
            _ = LoadIndexMetadataAsync();
        }

        // YENİ: Meta verileri yükleyen metot
        public async Task LoadIndexMetadataAsync()
        {
            IsLoadingMetadata = true;
            try
            {
                // UI thread'ini tıkamamak için Task.Run kullan
                await Task.Run(() =>
                {
                    foreach (var index in SettingsService.Indexes)
                    {
                        try
                        {
                            var storedMetadata = _indexService.GetIndexMetadata(index.IndexPath);

                            // UI thread'ine geri dönerek ObservableCollection'ı güncelle
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                index.CreationDate = storedMetadata?.CreationDate;
                                index.LastUpdateDate = storedMetadata?.LastUpdateDate;
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{IndexName} için meta veri okunamadı.", index.Name);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Meta veriler yüklenirken genel bir hata oluştu.");
            }
            finally
            {
                IsLoadingMetadata = false;
            }
        }

        private void CreateIndex()
        {
            try
            {
                var win = App.ServiceProvider.GetRequiredService<IndexCreationWindow>();
                if (win.ViewModel != null)
                {
                    // YENİ: Oluşturma başarılı olursa, meta verileri yeniden yükle
                    win.ViewModel.IndexingCompletedCallback = (bool success) =>
                    {
                        Application.Current.Dispatcher.Invoke(async () =>
                        {
                            if (success)
                            {
                                _logger.LogInformation("Yeni indeks başarıyla eklendi. Meta veriler yenileniyor.");
                                await LoadIndexMetadataAsync(); // Listeyi yenile
                            }
                        });
                    };
                }
                win.Owner = Application.Current.MainWindow;
                win.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IndexCreationWindow açılamadı.");
            }
        }

        private async Task UpdateIndexAsync()
        {
            var indexesToUpdate = SettingsService.Indexes.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.SourcePath)).ToList();
            if (!indexesToUpdate.Any())
            {
                MessageBox.Show("Güncellenecek (kaynak yolu bilinen) seçili indeks yok.", "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(
                $"{indexesToUpdate.Count} adet indeks güncellenecek.\nDevam etmek istiyor musunuz?",
                "İndeksleri Güncelle", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            bool metadataNeedsRefresh = false;
            foreach (var index in indexesToUpdate)
            {
                try
                {
                    var updateVM = new IndexUpdateViewModel(_indexService, _settingsService, index, _updateLogger);
                    var updateWin = new IndexUpdateWindow
                    {
                        ViewModel = updateVM,
                        Owner = Application.Current.MainWindow
                    };
                    updateWin.ShowDialog();
                    if (updateVM.OperationSucceeded)
                    {
                        metadataNeedsRefresh = true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{IndexName} için güncelleme penceresi açılırken hata.", index.Name);
                    MessageBox.Show($"'{index.Name}' güncellenemedi (Kilitli olabilir mi?):\n{ex.Message}", "Güncelleme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // YENİ: En az bir güncelleme başarılı olduysa listeyi yenile
            if (metadataNeedsRefresh)
            {
                await LoadIndexMetadataAsync();
            }
        }

        private void AddExistingIndexAsync()
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Var olan bir Lucene İndeks klasörünü seçin" };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _settingsService.AddExistingIndex(dialog.SelectedPath);
                    _ = LoadIndexMetadataAsync(); // Listeyi yenile
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mevcut indeks eklenirken hata.");
                MessageBox.Show($"İndeks eklenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteIndexAsync()
        {
            var indexesToDelete = SettingsService.Indexes.Where(i => i.IsSelected).ToList();
            if (!indexesToDelete.Any()) return;

            var result = MessageBox.Show(
                $"Seçili {indexesToDelete.Count} adet indeksi listeden kaldırmak istediğinize emin misiniz?\n\nBu işlem, diskteki indeks dosyalarını SİLMEZ, sadece listeden kaldırır.",
                "İndeksleri Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _settingsService.RemoveIndexes(indexesToDelete);
            }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}