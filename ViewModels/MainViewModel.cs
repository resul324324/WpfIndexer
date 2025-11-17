using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Store;
// Lucene 'using'
using Lucene.Net.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OpenXmlPowerTools;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
// DÜZELTME: 'Theme' enum'unun bulunduğu namespace'i ekliyoruz.

using System.Collections.Specialized; // Dosya kopyalama (Clipboard) için EKLENDİ
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
// Canlı Mod için 'using'
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows; // Clipboard için EKLENDİ
using System.Windows.Documents; // FlowDocument, Paragraph, Run
using System.Windows.Input;
using System.Windows.Media;     // Brushes
using System.Xml.Linq;
using WpfIndexer.Helpers; // ShellIconHelper için EKLENDİ (zaten vardı ama doğrulayalım)
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.Views;



[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace WpfIndexer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        // Servisler
        private readonly IIndexService _indexService;
        private readonly ILogger<MainViewModel> _logger;
        private readonly SearchHistoryService _searchHistoryService;
        private readonly Serilog.ILogger _searchLogger;
        private readonly CsvExportService _csvExportService;
        private readonly AutoUpdateService _autoUpdateService;

        private Query? _lastExecutedLuceneQuery;
        private CancellationTokenSource? _previewCts;
        private CancellationTokenSource? _suggestionCts;
        private readonly TimeSpan _suggestionDelay = TimeSpan.FromMilliseconds(200);



        // Paylaşılan ayar/veri servisleri
        public IndexSettingsService IndexSettings { get; }
        public UserSettings UserSettings { get; }

        public ObservableCollection<SearchResult> SearchResults { get; } = new();
        private string _query = string.Empty;
        public string Query
        {
            get => _query;
            set
            {
                if (_query == value) return;
                _query = value;
                OnPropertyChanged();

                // Tek debounce mekanizması
                _suggestionCts?.Cancel();
                _suggestionCts = new CancellationTokenSource();
                var token = _suggestionCts.Token;
                string q = value;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(_suggestionDelay, token);
                        if (token.IsCancellationRequested) return;

                        await UpdateSuggestionsAsync(q);
                    }
                    catch { }
                }, token);
            }
        }




        // YENİ: Arayüzün durumunu (arama öncesi/sonrası) belirler
        private bool _isSearchPerformed;
        public bool IsSearchPerformed
        {
            get => _isSearchPerformed;
            set { _isSearchPerformed = value; OnPropertyChanged(); }
        }
        public ObservableCollection<string> Suggestions { get; } = new();
        private bool _isSuggestionsOpen;
        public bool IsSuggestionsOpen
        {
            get => _isSuggestionsOpen;
            set { _isSuggestionsOpen = value; OnPropertyChanged(); }
        }
        private SearchResult? _selectedSearchResult;
        public SearchResult? SelectedSearchResult
        {
            get => _selectedSearchResult;
            set
            {
                _selectedSearchResult = value;
                OnPropertyChanged();
                // NOT: Komutların güncellenmesi OnPropertyChanged'ın
                // sonundaki merkezi mantığa taşındı.
            }
        }
        private string _statusMessage = "Hazır.";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // Rapor Modu: Ham metin
        private string _selectedPreviewContent = string.Empty;
        public string SelectedPreviewContent { get => _selectedPreviewContent; set { _selectedPreviewContent = value; OnPropertyChanged(); } }

        // Rapor Modu: Vurgulanmış metin
        private string _highlightedPreviewContent = string.Empty;
        public string HighlightedPreviewContent
        {
            get => _highlightedPreviewContent;
            set { _highlightedPreviewContent = value; OnPropertyChanged(); }
        }

        // Resim/Video Önizleme (Her iki modda da ortak)
        private string? _selectedPreviewImagePath;
        public string? SelectedPreviewImagePath { get => _selectedPreviewImagePath; set { _selectedPreviewImagePath = value; OnPropertyChanged(); } }
        private Uri? _selectedPreviewVideoPath;
        public Uri? SelectedPreviewVideoPath { get => _selectedPreviewVideoPath; set { _selectedPreviewVideoPath = value; OnPropertyChanged(); } }

        // Canlı Mod: WebView (PDF, HTML, vb.)
        private Uri? _selectedPreviewWebViewUri;
        public Uri? SelectedPreviewWebViewUri
        {
            get => _selectedPreviewWebViewUri;
            set { _selectedPreviewWebViewUri = value; OnPropertyChanged(); }
        }

        private Visibility _webViewPreviewVisibility = Visibility.Collapsed;
        public Visibility WebViewPreviewVisibility
        {
            get => _webViewPreviewVisibility;
            set { _webViewPreviewVisibility = value; OnPropertyChanged(); }
        }

        // Görünürlük Özellikleri
        // MainViewModel.cs (Satır 137-140)

        public Visibility TextPreviewVisibility => UserSettings.EnablePreview &&
                                                  _selectedPreviewContent.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ImagePreviewVisibility => UserSettings.EnablePreview && _selectedPreviewImagePath != null ? Visibility.Visible : Visibility.Collapsed;

        private Visibility _videoPreviewVisibility = Visibility.Collapsed;
        public Visibility VideoPreviewVisibility { get => _videoPreviewVisibility; set { _videoPreviewVisibility = value; OnPropertyChanged(); } }

        public Visibility UnsupportedPreviewVisibility
        {
            get
            {
                if (!UserSettings.EnablePreview) return Visibility.Visible;

                // DÜZELTME: 'PreviewMode' kontrolü kaldırıldı.
                // Eğer metin içeriği yüklendiyse (_selectedPreviewContent > 0),
                // bu panel her zaman gizlenmelidir.
                if (UserSettings.EnablePreview &&
                    _selectedPreviewContent.Length > 0)
                {
                    return Visibility.Collapsed;
                }

                if (UserSettings.EnablePreview && _selectedPreviewImagePath != null)
                {
                    return Visibility.Collapsed;
                }

                if (VideoPreviewVisibility == Visibility.Visible) return Visibility.Collapsed;
                if (WebViewPreviewVisibility == Visibility.Visible) return Visibility.Collapsed;

                return Visibility.Visible;
            }
        }
        private string _unsupportedMessage = "Önizleme için bir dosya seçin.";
        public string UnsupportedMessage
        {
            get
            {
                if (!UserSettings.EnablePreview) return "Önizleme paneli ayarlardan kapatıldı.";
                return _unsupportedMessage;
            }
            set { _unsupportedMessage = value; OnPropertyChanged(); }
        }

        // --- Komutlar ---
        public ICommand SearchCommand { get; }
        public RelayCommand OpenFileLocationCommand { get; }
        public ICommand SelectSuggestionCommand { get; }
        public ICommand OpenIndexManagementCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenAboutCommand { get; }
        public ICommand OpenSystemLogCommand { get; }
        public ICommand OpenSearchLogCommand { get; }
        public ICommand ExportResultsCommand { get; }
        public ICommand ExitApplicationCommand { get; }
        public ICommand OpenViewSettingsCommand { get; }
        public ICommand OpenFileCommand { get; }

       
        public ICommand CopyFileCommand { get; }

        
        public ICommand OpenContainingFolderCommand { get; }

        public ICommand CopyFileFromPreviewCommand { get; }

        // Constructor
        public MainViewModel(
            IIndexService indexService,
            IndexSettingsService indexSettingsService,
            UserSettingsService userSettingsService,
            SearchHistoryService searchHistoryService,
            AutoUpdateService autoUpdateService,
            CsvExportService csvExportService,
            ILogger<MainViewModel> logger,
            Serilog.ILogger searchLogger)
        {
            _indexService = indexService;
            IndexSettings = indexSettingsService;
            UserSettings = userSettingsService.Settings;
            _searchHistoryService = searchHistoryService;
            _autoUpdateService = autoUpdateService;
            _csvExportService = csvExportService;
            _logger = logger;
            _searchLogger = searchLogger;
            IsSearchPerformed = false;

            // --- Komut tanımlamaları ---
            SearchCommand = new RelayCommand(async _ => await SearchAsync(), _ => !string.IsNullOrWhiteSpace(Query) && IndexSettings.Indexes.Any(i => i.IsSelected));
            OpenFileLocationCommand = new RelayCommand(_ => OpenFileLocation(), _ => SelectedSearchResult != null);
            SelectSuggestionCommand = new RelayCommand(SelectSuggestion);
            OpenIndexManagementCommand = new RelayCommand(_ => OpenWindow<IndexManagementWindow>());
            OpenSettingsCommand = new RelayCommand(_ => OpenWindow<SettingsWindow>());
            OpenHelpCommand = new RelayCommand(_ => OpenWindow<HelpWindow>(false));
            OpenAboutCommand = new RelayCommand(_ => OpenWindow<AboutWindow>());
            OpenSystemLogCommand = new RelayCommand(_ => OpenLogFile("system.log"));
            OpenSearchLogCommand = new RelayCommand(_ => OpenLogFile("arama.log"));
            ExportResultsCommand = new RelayCommand(_ => ExportResults(), _ => SearchResults.Any());
            ExitApplicationCommand = new RelayCommand(_ => Application.Current.Shutdown());
            OpenViewSettingsCommand = new RelayCommand(_ => OpenWindow<ViewSettingsWindow>());

            // --- YENİ KOMUTLARIN TANIMLANMASI ---
            // Not: 'CanExecute' (_ => SelectedSearchResult != null) durumu,
            // OnPropertyChanged metodunda (SelectedSearchResult değiştiğinde) güncellenecek.
            CopyFileCommand = new RelayCommand(_ => CopySelectedFile(), _ => SelectedSearchResult != null);

            // Bu komut, zaten var olan OpenFileLocation metodunu yeniden kullanıyor.
            OpenContainingFolderCommand = new RelayCommand(_ => OpenFileLocation(), _ => SelectedSearchResult != null);

            // Bu komut, (isteğin üzerine) listedeki kopyalama ile aynı işi yapıyor.
            CopyFileFromPreviewCommand = new RelayCommand(_ => CopySelectedFile(), _ => SelectedSearchResult != null);
            // --- BİTTİ ---
            OpenFileCommand = new RelayCommand(OpenFile, _ => SelectedSearchResult != null);

            _autoUpdateService.StatusChanged += (status) =>
            {
                Application.Current.Dispatcher.Invoke(() => { StatusMessage = status; });
            };
            _autoUpdateService.Start();
            StatusMessage = "Uygulama başlatıldı. Servisler yükleniyor...";

            // Değişiklik dinleyicileri
            UserSettings.PropertyChanged += OnSettingsChanged;
            IndexSettings.Indexes.CollectionChanged += (s, e) =>
            {
                UpdateCommandCanExecute();
                if (e.NewItems != null)
                {
                    foreach (IndexDefinition item in e.NewItems)
                        item.PropertyChanged += OnIndexPropertyChanged;
                }
            };
            foreach (var index in IndexSettings.Indexes)
            {
                index.PropertyChanged += OnIndexPropertyChanged;
            }

            _logger.LogInformation("MainViewModel başlatıldı. {IndexCount} indeks yüklendi.", IndexSettings.Indexes.Count);
        }

        // Ayar değişikliklerini dinle
        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(UserSettings.EnablePreview) ||
        e.PropertyName == nameof(UserSettings.PreviewPosition))
            {
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(VideoPreviewVisibility));
                OnPropertyChanged(nameof(WebViewPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedMessage));
                OnPropertyChanged(nameof(UserSettings));
            }

            if (e.PropertyName == nameof(UserSettings.PreviewMode) || e.PropertyName == nameof(UserSettings.Theme))
            {
                FireAndForget(LoadPreviewAsyncWithToken());

            }
        }

        private void OnIndexPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IndexDefinition.IsSelected))
            {
                (SearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // Arama Metodu
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(Query)) return;

            if (!IsSearchPerformed)
                IsSearchPerformed = true;

            IsSuggestionsOpen = false;
            StatusMessage = "Aranıyor...";
            SearchResults.Clear();

            var (validIndexPaths, indexName) = ValidateSelectedIndexes();
            if (!validIndexPaths.Any())
            {
                StatusMessage = "Arama yapılamadı. Lütfen geçerli bir indeks seçin.";
                if (!string.IsNullOrEmpty(indexName))
                {
                    MessageBox.Show($"'{indexName}' indeksi bulunamadı veya geçersiz.\n\nLütfen 'İndeks -> İndeks Yönetimi' menüsünden kontrol edin.",
                        "İndeks Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();

                var response = await _indexService.SearchAsync(
                    Query,
                    validIndexPaths,
                    UserSettings.DefaultSearchResultLimit);

                stopwatch.Stop();

                _lastExecutedLuceneQuery = response.LuceneQuery;

                if (_lastExecutedLuceneQuery == null && !string.IsNullOrEmpty(Query))
                {
                    StatusMessage = "Arama sorgusu geçersiz (örn: tek *).";
                    _logger.LogWarning("Geçersiz veya boş Lucene sorgusu: {Query}", Query);
                }

                // UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SearchResults.Clear();

                    foreach (var item in response.Results)
                    {
                        try
                        {
                            // İkon ve tür adı
                            item.FileIcon = Helpers.ShellIconHelper.GetFileIcon(item.Extension);
                            item.FileType = Helpers.ShellIconHelper.GetFileTypeName(item.Extension);

                            // Sadece resimler için thumbnail’i ARKA PLANDA üret
                            if (item.IsImage)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var thumb = await ThumbnailCache.GetImageThumbnailAsync(item.Path);
                                        if (thumb != null)
                                        {
                                            // UI thread dışında kullanılacağı için Freeeze
                                            thumb.Freeze();
                                            item.Thumbnail = thumb;
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogWarning(ex, "Thumbnail oluşturulamadı: {Path}", item.Path);
                                    }
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Dosya ikonu/türü/thumbnail yüklenemedi: {Path}", item.Path);
                            item.FileType = item.Extension.TrimStart('.').ToUpper();
                            item.FileIcon = null;
                        }

                        SearchResults.Add(item);
                    }

                    StatusMessage = $"{response.Results.Count} sonuç {stopwatch.Elapsed.TotalSeconds:F2} saniyede bulundu.";
                    _logger.LogInformation("Arama tamamlandı: {Query}, Sonuç: {Count}, Süre: {Ms}ms",
                        Query, response.Results.Count, stopwatch.ElapsedMilliseconds);

                    UpdateCommandCanExecute();
                });




                if (UserSettings.SaveSearchHistory)
                {
                    await _searchHistoryService.AddSearchTermAsync(Query);
                    _searchLogger.Information("Sorgu: \"{Query}\", Bulunan: {ResultCount}", Query, response.Results.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arama sırasında bir hata oluştu. Sorgu: {Query}", Query);
                StatusMessage = $"Arama Hatası: {ex.Message}";
            }
        }


        // İndeksleri Doğrulama
        private (List<string> ValidPaths, string FirstInvalidName) ValidateSelectedIndexes()
        {
            var selectedIndexes = IndexSettings.Indexes.Where(i => i.IsSelected).ToList();
            var validPaths = new List<string>();
            string firstInvalidName = string.Empty;
            if (!selectedIndexes.Any())
            {
                StatusMessage = "Arama yapmak için en az bir indeks seçmelisiniz.";
                return (validPaths, firstInvalidName);
            }
            foreach (var index in selectedIndexes)
            {
                try
                {
                    using var dir = FSDirectory.Open(index.IndexPath);
                    if (DirectoryReader.IndexExists(dir))
                    {
                        validPaths.Add(index.IndexPath);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(firstInvalidName)) firstInvalidName = index.Name;
                        _logger.LogWarning("İndeks doğrulanamadı (IndexExists false): {IndexName}, Path: {IndexPath}", index.Name, index.IndexPath);
                    }
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrEmpty(firstInvalidName)) firstInvalidName = index.Name;
                    _logger.LogWarning(ex, "İndeks doğrulanırken hata (Directory.Exists veya FSDirectory.Open hatası): {IndexName}, Path: {IndexPath}", index.Name, index.IndexPath);
                }
            }
            return (validPaths, firstInvalidName);
        }
        private void DebounceSuggestions(string query)
        {
            _suggestionCts?.Cancel();
            _suggestionCts?.Dispose();
            _suggestionCts = new CancellationTokenSource();
            var token = _suggestionCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_suggestionDelay, token);
                    if (token.IsCancellationRequested) return;

                    await UpdateSuggestionsAsync(query);
                }
                catch (TaskCanceledException)
                {
                    // yok say
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Suggestion update error");
                }
            }, token);
        }

        // Arama Önerileri
        // Arama Önerileri
        private async Task UpdateSuggestionsAsync(string query)
        {
            if (!UserSettings.ShowSearchSuggestions || string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                // ===== DEĞİŞİKLİK BURADA: UI Güncellemesi Dispatcher ile yapılmalı =====
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsSuggestionsOpen = false;
                    Suggestions.Clear();
                });
                // =====================================================================
                return;
            }

            // Bu kısım arka planda kalabilir (veri çekme)
            var suggestions = await _searchHistoryService.GetSuggestionsAsync(query, 5);

            // ===== DEĞİŞİKLİK BURADA: UI Güncellemesi Dispatcher ile yapılmalı =====
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (suggestions.Any())
                {
                    Suggestions.Clear();
                    foreach (var s in suggestions)
                    {
                        Suggestions.Add(s);
                    }
                    IsSuggestionsOpen = true;
                }
                else
                {
                    IsSuggestionsOpen = false;
                }
            });
            // =====================================================================
        }


        // Öneri Seçimi
        private void SelectSuggestion(object? selectedItem)
        {
            if (selectedItem is string suggestion)
            {
                // 1. Bayrağı hemen kapat (UI'ın kapanması için)
                IsSuggestionsOpen = false;

                // 2. Query'yi güncelleyen 'set' bloğunu atla
                //    'set' bloğu, UpdateSuggestionsAsync'ı tetikleyerek
                //    bu popup'ı yeniden açmaya çalışıyor (yarış durumu).

                // ESKİ (Hatalı):
                // Query = suggestion;
                // OnPropertyChanged(nameof(Query)); // Bu satır 'Query = suggestion' içindeydi

                // YENİ (Doğru):
                _query = suggestion; // Arka plandaki alanı (field) doğrudan ayarla
                OnPropertyChanged(nameof(Query)); // UI'ı (TextBox) güncelle

                // 3. Aramayı başlat
                _ = SearchAsync();
            }
        }


        // --- PENCERE AÇMA METODLARI ---
        private void OpenWindow<T>() where T : Window
        {
            try
            {
                var window = App.ServiceProvider.GetService<T>() as Window;
                if (window == null)
                {
                    window = Activator.CreateInstance<T>();
                }
                window.Owner = Application.Current.MainWindow;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{WindowType} penceresi açılırken hata oluştu.", typeof(T).Name);
                MessageBox.Show($"Pencere açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenWindow<T>(bool modal) where T : Window
        {
            if (modal) { OpenWindow<T>(); return; }
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is T) { w.Activate(); return; }
                }
                var window = App.ServiceProvider.GetService<T>() as Window;
                if (window == null) { window = Activator.CreateInstance<T>(); }
                window.Owner = Application.Current.MainWindow;
                window.Show();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{WindowType} (non-modal) penceresi açılırken hata oluştu.", typeof(T).Name);
            }
        }

        // --- LOG DOSYALARI ---
        private void OpenLogFile(string logFileName)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", logFileName);
                if (!System.IO.File.Exists(logPath))
                {
                    MessageBox.Show($"Log dosyası henüz oluşturulmamış: {logPath}", "Dosya Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{LogFileName} log dosyası açılırken hata.", logFileName);
                MessageBox.Show($"Log dosyası açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // --- DIŞA AKTARMA ---
        private void ExportResults()
        {
            if (!SearchResults.Any()) return;
            var sfd = new SaveFileDialog
            {
                Filter = "CSV Dosyası (*.csv)|*.csv|Tüm Dosyalar (*.*)|*.*",
                FileName = $"AramaSonuclari_{DateTime.Now:yyyyMMdd_HHmm}.csv",
                Title = "Arama Sonuçlarını Dışa Aktar"
            };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    string csvData = _csvExportService.ExportToCsv(SearchResults);
                    System.IO.File.WriteAllText(sfd.FileName, csvData, Encoding.UTF8);
                    StatusMessage = $"Sonuçlar başarıyla dışa aktarıldı: {sfd.FileName}";
                    _logger.LogInformation("Arama sonuçları CSV'ye aktarıldı: {FilePath}", sfd.FileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CSV dışa aktarma hatası: {FilePath}", sfd.FileName);
                    MessageBox.Show($"Dışa aktarma hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // -------------------------------------------------------------------
        // --- ÖNİZLEME (PREVIEW) MANTIĞI ---
        // -------------------------------------------------------------------

        // (Önizleme mantığı (LoadPreviewAsyncWithToken, ConvertFileToHtmlAsync vb.)
        // Adım 4'ün konusu OLMADIĞI için olduğu gibi bırakıldı.)

        private async Task LoadPreviewAsyncWithToken()
        {
            // Eski görevi iptal et
            _previewCts?.Cancel();
            var cts = new CancellationTokenSource();
            _previewCts = cts;
            var token = cts.Token;

            var currentSelection = SelectedSearchResult;

            // --- 1. AŞAMA: UI'ı sıfırla ---

            // WebView2'yi gizle (ama Source'u null yapma!)
            WebViewPreviewVisibility = Visibility.Collapsed;

            // Diğer önizleme içeriklerini temizle
            SelectedPreviewVideoPath = null;
            SelectedPreviewImagePath = null;
            SelectedPreviewContent = string.Empty;
            HighlightedPreviewContent = string.Empty;

            // Görünürlük değişikliklerini bildir
            OnPropertyChanged(nameof(ImagePreviewVisibility));
            OnPropertyChanged(nameof(TextPreviewVisibility));
            OnPropertyChanged(nameof(VideoPreviewVisibility));
            OnPropertyChanged(nameof(UnsupportedPreviewVisibility));

            if (currentSelection == null)
            {
                UnsupportedMessage = "Önizleme için bir dosya seçin.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            UnsupportedMessage = "Önizleme yükleniyor...";
            OnPropertyChanged(nameof(UnsupportedPreviewVisibility));

            // UI'ın güncellenmesi için kısa bekleme
            try
            {
                await Task.Delay(30, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            // --- 2. AŞAMA: Gerçek yüklemeyi yap ---
            try
            {
                var path = currentSelection.Path;
                var ext = currentSelection.Extension.ToLowerInvariant();

                if (path.Contains("|"))
                {
                    if (token.IsCancellationRequested) return;
                    UnsupportedMessage = $"'{Path.GetFileName(path)}' bir arşivin (ZIP, RAR vb.) içindedir.\n\nÖnizleme şu anda arşiv içi dosyalar için desteklenmemektedir.";
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                    return;
                }

                if (!System.IO.File.Exists(path))
                {
                    if (token.IsCancellationRequested) return;
                    UnsupportedMessage = $"Dosya bulunamadı: {path}";
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                    return;
                }

                // Resim/Video (Hızlı)
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tiff" || ext == ".webp")
                {
                    if (token.IsCancellationRequested) return;
                    SelectedPreviewImagePath = path;
                    OnPropertyChanged(nameof(ImagePreviewVisibility));
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                    return;
                }

                if (ext == ".mp4" || ext == ".wmv" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".mpeg" || ext == ".webm")
                {
                    string thumb = await Task.Run(() => FileProcessor.ExtractVideoThumbnail(path));
                    ThumbnailCache.SetVideoThumbnail(path, thumb);


                    if (token.IsCancellationRequested) return;

                    if (System.IO.File.Exists(thumb))
                    {
                        SelectedPreviewImagePath = thumb;
                        OnPropertyChanged(nameof(ImagePreviewVisibility));
                        OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                        return;
                    }
                }

                bool livePreviewSuccessful = false;
                bool requireFallback = false;

                // -------------------------
                // 1) CANLI ÖNİZLEME DENE
                // -------------------------
                if (UserSettings.EnablePreview && UserSettings.PreviewMode == PreviewMode.Canli)
                {
                    bool canLive =
                        ext is ".pdf" or ".txt" or ".log" or ".xml" or ".json" or ".cs" or ".html" or ".css"
                        or ".docx" or ".xlsx" or ".csv";

                    if (canLive)
                    {
                        try
                        {
                            // PDF → direkt WebView2
                            if (ext == ".pdf")
                            {
                                SelectedPreviewWebViewUri = new Uri(path);
                                WebViewPreviewVisibility = Visibility.Visible;
                                livePreviewSuccessful = true;
                            }
                            else
                            {
                                // DOCX / XLSX / METİN TÜREVLERİ → HTML'e dönüştür
                                string? htmlPath = await ConvertFileToHtmlAsync(path, ext, Query, token);

                                if (!token.IsCancellationRequested &&
                                    htmlPath != null && File.Exists(htmlPath))
                                {
                                    SelectedPreviewWebViewUri = new Uri(htmlPath);
                                    WebViewPreviewVisibility = Visibility.Visible;
                                    livePreviewSuccessful = true;
                                }
                                else
                                {
                                    requireFallback = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Canlı önizleme başarısız → Rapor moduna geçiliyor.");
                            requireFallback = true;
                        }
                    }
                    else
                    {
                        // Canlı mod bu formatı desteklemiyor → rapora geç
                        requireFallback = true;
                    }
                }
                else
                {
                    // Zaten Rapor modunda
                    requireFallback = true;
                }

                // -------------------------
                // 2) RAPOR MODUNA DÜŞÜŞ
                // -------------------------
                if (!livePreviewSuccessful || requireFallback)
                {
                    var indexDefinition = IndexSettings.Indexes
                        .FirstOrDefault(i => i.Name == currentSelection.IndexName);

                    if (indexDefinition == null)
                    {
                        UnsupportedMessage = "Hata: İndeks tanımı bulunamadı.";
                        return;
                    }

                    string content = await _indexService.GetContentByPathAsync(
                        indexDefinition.IndexPath, path);

                    if (!string.IsNullOrEmpty(content))
                    {
                        SelectedPreviewContent = content;
                        HighlightPreviewContent();
                        OnPropertyChanged(nameof(TextPreviewVisibility));
                    }
                    else
                    {
                        UnsupportedMessage =
                            $"Bu dosya türü ({ext}) için önizleme desteklenmiyor veya içerik yok.";
                    }
                }

            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) return;

                StatusMessage = "Hazır.";
                UnsupportedMessage = $"Önizleme yüklenirken hata:\n{ex.Message}";
                _logger.LogError(ex, "Önizleme yüklenirken hata oluştu: {FilePath}", currentSelection?.Path);
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                }
            }
        }


        // --- RAPOR MODU VURGULAMA (Lucene Highlighter) ---
        private void HighlightPreviewContent()
        {
            if (!string.IsNullOrEmpty(SelectedPreviewContent) && _lastExecutedLuceneQuery != null)
            {
                try
                {
                    IFormatter formatter = new SimpleHTMLFormatter("<B>", "</B>");
                    using var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    QueryScorer scorer = new QueryScorer(_lastExecutedLuceneQuery);
                    Highlighter highlighter = new Highlighter(formatter, scorer);

                    highlighter.TextFragmenter = new NullFragmenter();
                    highlighter.MaxDocCharsToAnalyze = 5 * 1024 * 1024;

                    using TokenStream tokenStream = analyzer.GetTokenStream("content", new StringReader(SelectedPreviewContent));
                    string result = highlighter.GetBestFragments(tokenStream, SelectedPreviewContent, 1, "...");

                    HighlightedPreviewContent = string.IsNullOrEmpty(result) ? SelectedPreviewContent : result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Rapor Modu vurgulaması sırasında hata oluştu.");
                    HighlightedPreviewContent = SelectedPreviewContent;
                }
            }
            else
            {
                HighlightedPreviewContent = SelectedPreviewContent;
            }
        }

        // --- CANLI MOD VURGULAMA (HTML <mark> etiketi) ---
        private string ApplyHtmlHighlighting(string htmlContent, string rawQuery)
        {
            if (string.IsNullOrWhiteSpace(htmlContent) || string.IsNullOrWhiteSpace(rawQuery))
            {
                return htmlContent;
            }

            var terms = rawQuery.Split(new[] { ' ', '"', '(', ')', '[', ']', ':', '*' },
                                       StringSplitOptions.RemoveEmptyEntries)
                                .Except(new[] { "AND", "OR", "NOT" }, StringComparer.OrdinalIgnoreCase)
                                .Distinct()
                                .Where(t => t.Length > 1)
                                .OrderByDescending(t => t.Length)
                                .ToList();

            if (!terms.Any()) return htmlContent;

            // DÜZELTME: Vurgulama <style> etiketleri içine (CSS) yazıldığı için
            // buradaki stil (inline) kaldırıldı.
            string pattern = "(>)([^<]*)(<)";

            return Regex.Replace(htmlContent, pattern, m =>
            {
                string textBetweenTags = m.Groups[2].Value;
                foreach (var term in terms)
                {
                    textBetweenTags = Regex.Replace(textBetweenTags,
                        Regex.Escape(term),
                        match => $"<mark>{match.Value}</mark>", // style='...' kaldırıldı
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                return m.Groups[1].Value + textBetweenTags + m.Groups[3].Value;
            }, RegexOptions.CultureInvariant);
        }

        // DÜZELTME: Kullanıcının sağladığı (Suggestion 1) stil metodu
        private string GetHtmlThemeStyles()
        {
            if (UserSettings.Theme.ToString() == "Dark")
            {
                // Koyu tema: koyu arka plan, açık metin
                return @"
<style>
    body { background-color: #1E1E1E; color: #FFFFFF; font-family: sans-serif; white-space: pre-wrap; }
    table { border-collapse: collapse; }
    th, td { border: 1px solid #555555; padding: 4px; text-align: left; }
    h2 { color: #FFFFFF; }
    mark { background: yellow; color: black; }
</style>";
            }
            else
            {
                // Açık tema: beyaz arka plan, siyah metin
                return @"
<style>
    body { background-color: #FFFFFF; color: #000000; font-family: sans-serif; white-space: pre-wrap; }
    table { border-collapse: collapse; }
    th, td { border: 1px solid #CCCCCC; padding: 4px; text-align: left; }
    h2 { color: #000000; }
    mark { background: yellow; color: black; }
</style>";
            }
        }

        private async Task<string?> ConvertFileToHtmlAsync(string sourcePath, string extension, string rawQuery, CancellationToken token)
        {
            string tempHtmlDir = Path.Combine(Path.GetTempPath(), "WpfIndexerPreview");
            System.IO.Directory.CreateDirectory(tempHtmlDir);
            string tempHtmlPath = Path.Combine(tempHtmlDir, $"preview_{Guid.NewGuid()}.html");
            string htmlContent = string.Empty;

            string themeStyle = GetHtmlThemeStyles();

            if (token.IsCancellationRequested) return null;

            try
            {
                // ********* .DOCX İÇİN (OpenXmlPowerTools) *********
                if (extension == ".docx")
                {
                    await Task.Run(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        byte[] byteArray = System.IO.File.ReadAllBytes(sourcePath);
                        using (var memoryStream = new MemoryStream())
                        {
                            memoryStream.Write(byteArray, 0, byteArray.Length);
                            memoryStream.Position = 0;

                            try
                            {
                                using (var wDoc = WordprocessingDocument.Open(memoryStream, true))
                                {
                                    if (wDoc.MainDocumentPart == null)
                                    {
                                        _logger.LogWarning("DOCX -> HTML dönüştürme atlandı (MainDocumentPart null): {sourcePath}", sourcePath);
                                        htmlContent = string.Empty;
                                        return;
                                    }

                                    Func<ImageInfo, XElement> imageHandler = imageInfo =>
                                    {
                                        using (var ms = new MemoryStream())
                                        {
                                            imageInfo.Bitmap.Save(ms, ImageFormat.Png);
                                            string base64 = Convert.ToBase64String(ms.ToArray());
                                            return new XElement(Xhtml.img,
                                                new XAttribute("src", $"data:image/png;base64,{base64}"),
                                                new XAttribute("width", imageInfo.Bitmap.Width.ToString()),
                                                new XAttribute("height", imageInfo.Bitmap.Height.ToString()));
                                        }
                                    };

                                    var settings = new HtmlConverterSettings()
                                    {
                                        ImageHandler = imageHandler,
                                        PageTitle = "Preview"
                                    };

                                    XElement html = HtmlConverter.ConvertToHtml(wDoc, settings);
                                    htmlContent = html.ToString();

                                    // DÜZELTME: HTML tema stilini enjekte et
                                    htmlContent = htmlContent.Replace("</head>", themeStyle + "</head>");
                                }
                            }
                            catch (ArgumentNullException ex)
                            {
                                _logger.LogError(ex, "DOCX -> HTML dönüştürme hatası (ArgumentNullException, muhtemelen bozuk dosya): {sourcePath}", sourcePath);
                                htmlContent = string.Empty;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "DOCX -> HTML dönüştürme genel hatası: {sourcePath}", sourcePath);
                                htmlContent = string.Empty;
                            }
                        }

                    }, token);
                }

                // ********* .XLSX İÇİN (ClosedXML) *********
                else if (extension == ".xlsx")
                {
                    await Task.Run(() =>
                    {
                        if (token.IsCancellationRequested) return;

                        try
                        {
                            var html = new StringBuilder($"<html><head><meta charset='UTF-8'>{themeStyle}</head><body>");

                            using (var workbook = new XLWorkbook(sourcePath))
                            {
                                var ws = workbook.Worksheets.FirstOrDefault(w => w.Visibility == XLWorksheetVisibility.Visible);
                                if (ws != null)
                                {
                                    html.Append($"<h2>{ws.Name}</h2><table>");
                                    foreach (var row in ws.RowsUsed().Take(500))
                                    {
                                        if (token.IsCancellationRequested) return;
                                        html.Append("<tr>");

                                        foreach (var cell in row.CellsUsed(XLCellsUsedOptions.Contents).Take(50))
                                        {
                                            html.Append($"<td>{System.Security.SecurityElement.Escape(cell.GetFormattedString())}</td>");
                                        }
                                        html.Append("</tr>");
                                    }
                                    html.Append("</table>");
                                }
                            }
                            html.Append("</body></html>");
                            htmlContent = html.ToString();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "XLSX -> HTML dönüştürme hatası: {sourcePath}", sourcePath);
                            htmlContent = string.Empty;
                        }
                    }, token);
                }
                // ********* .CSV İÇİN *********
                else if (extension == ".csv")
                {
                    await Task.Run(async () =>
                    {
                        var html = new StringBuilder($"<html><head><meta charset='UTF-8'>{themeStyle}</head><body><table>");
                        string[] lines = await System.IO.File.ReadAllLinesAsync(sourcePath, token);

                        if (lines.Length > 0)
                        {
                            html.Append("<thead><tr>");
                            foreach (var cell in lines[0].Split(',')) { html.Append($"<th>{System.Security.SecurityElement.Escape(cell)}</th>"); }
                            html.Append("</tr></thead>");
                            html.Append("<tbody>");
                            foreach (var line in lines.Skip(1).Take(1000))
                            {
                                if (token.IsCancellationRequested) return;
                                html.Append("<tr>");
                                foreach (var cell in line.Split(',')) { html.Append($"<td>{System.Security.SecurityElement.Escape(cell)}</td>"); }
                                html.Append("</tr>");
                            }
                            html.Append("</tbody>");
                        }
                        html.Append("</table></body></html>");
                        htmlContent = html.ToString();
                    }, token);
                }
                // DÜZELTME: .TXT ve DİĞER METİN DOSYALARI İÇİN
                else if (extension is ".txt" or ".log" or ".xml" or ".json" or ".cs" or ".html" or ".css")
                {
                    await Task.Run(async () =>
                    {
                        string content = await File.ReadAllTextAsync(sourcePath, token);
                        // Düz metni HTML'e çevirirken <pre> etiketi (veya white-space: pre-wrap stili)
                        // boşlukları ve satır sonlarını korur.
                        htmlContent = $"<html><head><meta charset='UTF-8'>{themeStyle}</head><body>" +
                                      $"<pre>{System.Security.SecurityElement.Escape(content)}</pre>" +
                                      $"</body></html>";
                    }, token);
                }
                else
                {
                    return null;
                }

                if (token.IsCancellationRequested) return null;

                if (string.IsNullOrEmpty(htmlContent))
                {
                    return null;
                }

                htmlContent = ApplyHtmlHighlighting(htmlContent, rawQuery);

                if (token.IsCancellationRequested) return null;

                await System.IO.File.WriteAllTextAsync(tempHtmlPath, htmlContent, token);
                // HTML cache'e kaydet
                ThumbnailCache.SetHtmlPreviewPath(sourcePath, tempHtmlPath);


                return tempHtmlPath;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _logger.LogError(ex, "{ext} dosyası HTML'e dönüştürülürken hata.", extension);
                }
                return null;
            }
        }

        // --- YENİ EKLENEN METOTLAR (Kopyala / Konum Aç) ---

        /// <summary>
        /// Seçili dosyanın (SelectedSearchResult) bulunduğu klasörü açar ve dosyayı seçer.
        /// (Bu metot zaten vardı, sadece yeni 'OpenContainingFolderCommand' tarafından da kullanılıyor)
        /// </summary>
        private void OpenFileLocation()
        {
            if (SelectedSearchResult == null) return;
            try
            {
                string path = SelectedSearchResult.Path;
                if (path.Contains("|"))
                {
                    path = path.Split('|')[0];
                }

                if (!System.IO.File.Exists(path))
                {
                    StatusMessage = "Dosya bulunamadı veya artık mevcut değil.";
                    return;
                }
                string argument = $"/select, \"{path}\"";
                Process.Start("explorer.exe", argument);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Dosya konumu açılamadı: {ex.Message}";
                _logger.LogError(ex, "Dosya konumu açılamadı: {FilePath}", SelectedSearchResult.Path);
            }
        }

        /// <summary>
        /// Seçili dosyayı (SelectedSearchResult) panoya "dosya olarak" kopyalar.
        /// (Windows Gezgini'nde CTRL+V ile yapıştırılabilir hale getirir)
        /// </summary>
        private void CopySelectedFile()
        {
            if (SelectedSearchResult == null) return;
            try
            {
                string path = SelectedSearchResult.Path;
                if (path.Contains("|"))
                {
                    StatusMessage = "Arşiv (zip, rar vb.) içindeki dosyalar kopyalanamaz.";
                    return;
                }
                if (!System.IO.File.Exists(path))
                {
                    StatusMessage = "Dosya bulunamadı veya artık mevcut değil.";
                    return;
                }

                // Dosyayı "kes" veya "kopyala" listesine (FileDrop) ekler
                var fileCollection = new StringCollection { path };

                // Panoya yapıştır (Kopyalama olarak ayarla)
                Clipboard.SetFileDropList(fileCollection);
                StatusMessage = $"Dosya panoya kopyalandı: {SelectedSearchResult.FileName}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Dosya kopyalanamadı: {ex.Message}";
                _logger.LogError(ex, "Dosya kopyalanamadı: {FilePath}", SelectedSearchResult.Path);
            }
        }

        // --- DİĞER METODLAR ---
        private void UpdateCommandCanExecute()
        {
            (SearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportResultsCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // YENİ EKLENEN KOMUTLARIN DA GÜNCELLENMESİ
            (CopyFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenContainingFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CopyFileFromPreviewCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenFileCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            if (name == nameof(SelectedSearchResult))
            {
                // Önceki önizlemeyi temizle (WebView2 Source'u null yapmadan)
                WebViewPreviewVisibility = Visibility.Collapsed;
                SelectedPreviewVideoPath = null;
                SelectedPreviewImagePath = null;
                SelectedPreviewContent = string.Empty;
                HighlightedPreviewContent = string.Empty;

                VideoPreviewVisibility = Visibility.Collapsed;

                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));

                // Yeni önizlemeyi yükle (fire-and-forget şekilde)
                FireAndForget(LoadPreviewAsyncWithToken());

                // Komutların CanExecute durumunu güncelle
                UpdateCommandCanExecute();

                return;
            }


            if (name == nameof(SelectedPreviewContent) ||
                name == nameof(SelectedPreviewImagePath) ||
                name == nameof(SelectedPreviewVideoPath) ||
                name == nameof(SelectedPreviewWebViewUri))
            {
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(VideoPreviewVisibility));
                OnPropertyChanged(nameof(WebViewPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
            }

            if (name == nameof(UnsupportedMessage))
            {
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
            }
        }
        
        private void OpenFile(object? parameter)
        {
            // Komut parametresi gelmezse, seçili olanı kullan
            var selected = parameter as SearchResult ?? SelectedSearchResult;
            if (selected == null) return;

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
                        StatusMessage = $"Arşiv dosyası bulunamadı: {archivePath}";
                        return;
                    }

                    // --- GÜVENLİK/UX İYİLEŞTİRMESİ ---
                    // Her dosya için benzersiz bir klasör oluşturarak çakışmaları önle
                    string tempDir = Path.Combine(Path.GetTempPath(), "WpfIndexerPreview", Guid.NewGuid().ToString());
                    System.IO.Directory.CreateDirectory(tempDir);
                    // --- BİTTİ ---

                    using (var archive = ArchiveFactory.Open(archivePath))
                    {
                        var entry = archive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace("\\", "/") == entryPath.Replace("\\", "/"));
                        if (entry == null)
                        {
                            StatusMessage = $"Arşiv içinde dosya bulunamadı: {entryPath}";
                            return;
                        }

                        tempFile = Path.Combine(tempDir, Path.GetFileName(entryPath));

                        // Dosya kilit kontrolü (MainWindow'dakinin aynısı)
                        if (File.Exists(tempFile))
                        {
                            try
                            {
                                using (FileStream fs = File.Open(tempFile, FileMode.Open, FileAccess.Read, FileShare.None)) { }
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
                        StatusMessage = $"Dosya bulunamadı: {path}";
                        return;
                    }
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                // MessageBox yerine StatusMessage'ı güncelle
                StatusMessage = $"Hata: Dosya açılamadı: {ex.Message}";
                _logger.LogError(ex, "Dosya açılamadı (OpenFileCommand): {FilePath}", path);
            }
        }
        private void FireAndForget(Task task)
        {
            task.ContinueWith(t =>
            {
                _logger.LogError(t.Exception, "Fire-and-forget task error");
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

    }

}