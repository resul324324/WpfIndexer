using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO; // YENİ: StringReader için eklendi
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfIndexer.Helpers;
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.Views;
using Microsoft.Extensions.Logging; // ILogger<T> için
using Serilog; // ILogger (Serilog arayüzü) için
using Lucene.Net.Store;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Lucene.Net.Index;

// YENİ 'USING' BİLDİRİMLERİ (ADIM 4.1)
using Lucene.Net.Util; // AppLuceneVersion için
using Lucene.Net.Search; // Query sınıfı için
using Lucene.Net.Search.Highlight; // Highlighter, QueryScorer, IFormatter, SimpleHtmlFormatter için
using Lucene.Net.Analysis; // TokenStream için
using Lucene.Net.Analysis.Standard; // StandardAnalyzer için

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace WpfIndexer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        // YENİ: AppLuceneVersion sabiti eklendi (Highlighter için gerekli)
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        // Servisler
        private readonly IIndexService _indexService;
        private readonly ILogger<MainViewModel> _logger; // system.log için
        private readonly SearchHistoryService _searchHistoryService;
        private readonly Serilog.ILogger _searchLogger; // arama.log için (DI'dan gelecek)
        private readonly CsvExportService _csvExportService;
        private readonly AutoUpdateService _autoUpdateService;

        // YENİ ALAN (ADIM 4.2): Lucene'den gelen asıl sorgu nesnesini sakla
        private Query? _lastExecutedLuceneQuery;

        // Paylaşılan ayar/veri servisleri
        public IndexSettingsService IndexSettings { get; }
        public UserSettings UserSettings { get; }

        // ... (Tüm diğer özellikleriniz (SearchResults, Query, vb.) SİZİN KODUNUZDAKİ GİBİ AYNI) ...
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
                _ = UpdateSuggestionsAsync(value);
            }
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
                _ = LoadPreviewAsync(); // GÜNCELLENDİ: Vurgulama bu metodun İÇİNE taşındı
                (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
        private string _statusMessage = "Hazır.";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        // Bu özellik sizin kodunuzdaki gibi kalıyor (Ham metin için)
        private string _selectedPreviewContent = string.Empty;
        public string SelectedPreviewContent { get => _selectedPreviewContent; set { _selectedPreviewContent = value; OnPropertyChanged(); } }

        // YENİ ÖZELLİK (ADIM 4.2): Vurgulanmış metni tutar
        private string _highlightedPreviewContent = string.Empty;
        public string HighlightedPreviewContent
        {
            get => _highlightedPreviewContent;
            set { _highlightedPreviewContent = value; OnPropertyChanged(); }
        }

        // ... (Diğer önizleme özellikleriniz (ImagePath, VideoPath, Visibility vb.) SİZİN KODUNUZDAKİ GİBİ AYNI) ...
        private string? _selectedPreviewImagePath;
        public string? SelectedPreviewImagePath { get => _selectedPreviewImagePath; set { _selectedPreviewImagePath = value; OnPropertyChanged(); } }
        private Uri? _selectedPreviewVideoPath;
        public Uri? SelectedPreviewVideoPath { get => _selectedPreviewVideoPath; set { _selectedPreviewVideoPath = value; OnPropertyChanged(); } }
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
        // DEĞİŞTİRİLDİ: Rapor Modu kontrolü eklendi
        public Visibility TextPreviewVisibility => UserSettings.EnablePreview &&
                                                  UserSettings.PreviewMode == PreviewMode.Rapor &&
                                                  _selectedPreviewContent.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ImagePreviewVisibility => UserSettings.EnablePreview && _selectedPreviewImagePath != null ? Visibility.Visible : Visibility.Collapsed;
        private Visibility _videoPreviewVisibility = Visibility.Collapsed;
        public Visibility VideoPreviewVisibility { get => _videoPreviewVisibility; set { _videoPreviewVisibility = value; OnPropertyChanged(); } }
        public Visibility UnsupportedPreviewVisibility
        {
            get
            {
                if (!UserSettings.EnablePreview) return Visibility.Visible;
                if (TextPreviewVisibility == Visibility.Visible) return Visibility.Collapsed;
                if (ImagePreviewVisibility == Visibility.Visible) return Visibility.Collapsed;
                if (VideoPreviewVisibility == Visibility.Visible) return Visibility.Collapsed;
                if (WebViewPreviewVisibility == Visibility.Visible) return Visibility.Collapsed; // YENİ EKLENDİ
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

        // ... (Tüm Komutlarınız SİZİN KODUNUZDAKİ GİBİ AYNI) ...
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

        // Constructor (SİZİN KODUNUZDAKİ GİBİ AYNI)
        public MainViewModel(
            IIndexService indexService,
            IndexSettingsService indexSettingsService,
            UserSettingsService userSettingsService,
            SearchHistoryService searchHistoryService,
            AutoUpdateService autoUpdateService,
            CsvExportService csvExportService,
            ILogger<MainViewModel> logger, // system.log için
            Serilog.ILogger searchLogger) // arama.log için
        {
            _indexService = indexService;
            IndexSettings = indexSettingsService;
            UserSettings = userSettingsService.Settings;
            _searchHistoryService = searchHistoryService;
            _autoUpdateService = autoUpdateService;
            _csvExportService = csvExportService;
            _logger = logger;
            _searchLogger = searchLogger;

            // Komut tanımlamaları (SİZİN KODUNUZDAKİ GİBİ AYNI)
            SearchCommand = new RelayCommand(async _ => await SearchAsync(), _ => !string.IsNullOrWhiteSpace(Query));
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

            _autoUpdateService.StatusChanged += (status) =>
            {
                // Arka plandaki bir thread'den gelebilir, UI thread'ine al
                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = status; // MEVCUT StatusMessage özelliğini güncelle
                });
            };

            // YENİ: Servisi BAŞLAT
            _autoUpdateService.Start();

            StatusMessage = "Uygulama başlatıldı. Servisler yükleniyor...";

            // Değişiklik dinleyicileri (SİZİN KODUNUZDAKİ GİBİ AYNI)
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

        // Bu metotlar SİZİN KODUNUZDAKİ GİBİ AYNI
        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Ayarlar değiştiğinde (örn: Önizlemeyi aç/kapa)
            if (e.PropertyName == nameof(UserSettings.EnablePreview))
            {
                // Önizleme görünürlüklerini yeniden tetikle
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(VideoPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedMessage));
            }
            if (e.PropertyName == nameof(UserSettings.PreviewMode))
                _ = LoadPreviewAsync();
            {
            }
        }

        private void OnIndexPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // Bir indeksin 'IsSelected' özelliği değiştiğinde
            if (e.PropertyName == nameof(IndexDefinition.IsSelected))
            {
                // 'Ara' butonunun durumunu güncelle
                (SearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        // GÜNCELLENMİŞ METOT (ADIM 4.3)
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(Query)) return;

            IsSuggestionsOpen = false;
            StatusMessage = "Aranıyor...";
            SearchResults.Clear();

            var (validIndexPaths, indexName) = ValidateSelectedIndexes();
            if (!validIndexPaths.Any())
            {
                StatusMessage = "Arama yapılamadı. Lütfen geçerli bir indeks seçin.";
                if (!string.IsNullOrEmpty(indexName))
                {
                    MessageBox.Show($"'{indexName}' indeksi bulunamadı veya geçersiz.\n\nLütfen 'İndeks -> İndeks Yönetimi' menüsünden kontrol edin.", "İndeks Bulunamadı", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();

                // YENİ: Dönüş 'results' -> 'response' oldu (Adım 2'ye dayanarak)
                var response = await _indexService.SearchAsync(Query, validIndexPaths, UserSettings.DefaultSearchResultLimit);
                stopwatch.Stop();

                // YENİ: Query nesnesini sakla
                _lastExecutedLuceneQuery = response.LuceneQuery;

                if (_lastExecutedLuceneQuery == null && !string.IsNullOrEmpty(Query))
                {
                    StatusMessage = "Arama sorgusu geçersiz (örn: tek *).";
                    _logger.LogWarning("Geçersiz veya boş Lucene sorgusu: {Query}", Query);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    SearchResults.Clear();
                    // GÜNCELLENDİ: 'results' -> 'response.Results'
                    foreach (var item in response.Results)
                        SearchResults.Add(item);

                    // GÜNCELLENDİ: 'results.Count' -> 'response.Results.Count'
                    StatusMessage = $"{response.Results.Count} sonuç {stopwatch.Elapsed.TotalSeconds:F2} saniyede bulundu.";
                    _logger.LogInformation("Arama tamamlandı: Sorgu '{Query}', Sonuç: {ResultCount}, Süre: {Duration}ms", Query, response.Results.Count, stopwatch.ElapsedMilliseconds);

                    UpdateCommandCanExecute();
                });

                // GÜNCELLENDİ: 'results.Count' -> 'response.Results.Count'
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

        // ValidateSelectedIndexes (SİZİN KODUNUZDAKİ GİBİ AYNI)
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

        // UpdateSuggestionsAsync (SİZİN KODUNUZDAKİ GİBİ AYNI)
        private async Task UpdateSuggestionsAsync(string query)
        {
            if (!UserSettings.ShowSearchSuggestions || string.IsNullOrWhiteSpace(query) || query.Length < 2)
            {
                IsSuggestionsOpen = false;
                Suggestions.Clear();
                return;
            }
            var suggestions = await _searchHistoryService.GetSuggestionsAsync(query, 5);
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
        }

        // SelectSuggestion (SİZİN KODUNUZDAKİ GİBİ AYNI)
        private void SelectSuggestion(object? selectedItem)
        {
            if (selectedItem is string suggestion)
            {
                Query = suggestion;
                OnPropertyChanged(nameof(Query));
                IsSuggestionsOpen = false;
                _ = SearchAsync();
            }
        }

        // OpenWindow<T> (Her iki versiyon da SİZİN KODUNUZDAKİ GİBİ AYNI)
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

        // OpenLogFile (SİZİN KODUNUZDAKİ GİBİ AYNI)
        private void OpenLogFile(string logFileName)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", logFileName);
                if (!File.Exists(logPath))
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

        // ExportResults (SİZİN KODUNUZDAKİ GİBİ AYNI)
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
                    File.WriteAllText(sfd.FileName, csvData, Encoding.UTF8);
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

        // LoadPreviewAsync'nin GÜNCELLENMİŞ TAMAMI
        private async Task LoadPreviewAsync()
        {
            // 1. Tüm önizleyicileri sıfırla
            SelectedPreviewVideoPath = null;
            SelectedPreviewImagePath = null;
            SelectedPreviewContent = string.Empty; // Rapor modu içeriği
            SelectedPreviewWebViewUri = null;      // Canlı mod içeriği
            WebViewPreviewVisibility = Visibility.Collapsed;

            // finally bloğu HighlightPreviewContent() çağıracak, 
            // bu da SelectedPreviewContent'i (boş) HighlightedPreviewContent'e atayacak.

            if (SelectedSearchResult == null)
            {
                UnsupportedMessage = "Önizleme için bir dosya seçin.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            var path = SelectedSearchResult.Path;
            var ext = SelectedSearchResult.Extension.ToLowerInvariant();

            // 2. Arşiv içi dosyalar (Her iki modda da desteklenmiyor)
            if (path.Contains("|"))
            {
                UnsupportedMessage = $"'{Path.GetFileName(path)}' bir arşivin (ZIP, RAR vb.) içindedir.\n\nÖnizleme şu anda arşiv içi dosyalar için desteklenmemektedir.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            if (!File.Exists(path))
            {
                UnsupportedMessage = $"Dosya bulunamadı: {path}";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            // 3. Dosya türüne göre yönlendirme (İç try-catch)
            try
            {
                // 3a. Resim/Video (Her iki modda da aynı davranır)
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tiff" || ext == ".webp")
                {
                    SelectedPreviewImagePath = path;
                    OnPropertyChanged(nameof(ImagePreviewVisibility));
                    return; // finally bloğu çalışacak
                }
                if (ext == ".mp4" || ext == ".wmv" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".mpeg" || ext == ".webm")
                {
                    string thumb = await Task.Run(() => FileProcessor.ExtractVideoThumbnail(path));
                    if (File.Exists(thumb))
                    {
                        SelectedPreviewImagePath = thumb;
                        OnPropertyChanged(nameof(ImagePreviewVisibility));
                        return; // finally bloğu çalışacak
                    }
                }

                // 3b. MODA GÖRE DALLANMA

                // ********* CANLI MOD MANTIĞI *********
                if (UserSettings.EnablePreview && UserSettings.PreviewMode == PreviewMode.Canli)
                {
                    // WebView2'nin açabileceği PDF, TXT vb. dosyalar
                    if (ext == ".pdf" || ext == ".txt" || ext == ".log" || ext == ".xml" || ext == ".json" || ext == ".cs" || ext == ".html" || ext == ".css")
                    {
                        SelectedPreviewWebViewUri = new Uri(path);
                        WebViewPreviewVisibility = Visibility.Visible;
                        return; // finally bloğu çalışacak
                    }

                    // Canlı mod, Office dosyalarını vb. desteklemiyorsa:
                    UnsupportedMessage = $"Canlı Önizleme modu bu dosya türü ({ext}) için desteklenmemektedir.\n\n'Ayarlar' menüsünden 'Rapor Modu'nu seçerek düz metin içeriğini görmeyi deneyebilirsiniz.";
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                    return; // finally bloğu çalışacak
                }

                // ********* RAPOR MODU MANTIĞI (Mevcut kodunuz) *********
                if (UserSettings.EnablePreview && UserSettings.PreviewMode == PreviewMode.Rapor)
                {
                    StatusMessage = "Önizleme indeksten yükleniyor...";
                    var indexDefinition = IndexSettings.Indexes.FirstOrDefault(i => i.Name == SelectedSearchResult.IndexName);
                    if (indexDefinition == null)
                    {
                        UnsupportedMessage = "Hata: İndeks tanımı bulunamadı.";
                        StatusMessage = "Hazır.";
                        OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                        return; // finally bloğu çalışacak
                    }

                    string content = await _indexService.GetContentByPathAsync(indexDefinition.IndexPath, SelectedSearchResult.Path);
                    StatusMessage = "Hazır.";

                    if (!string.IsNullOrEmpty(content))
                    {
                        SelectedPreviewContent = content; // Vurgulama bunu kullanacak
                        OnPropertyChanged(nameof(TextPreviewVisibility));
                        return; // finally bloğu çalışacak
                    }
                }

                // Hiçbir koşul karşılanmazsa
                UnsupportedMessage = $"Bu dosya türü ({ext}) için önizleme desteklenmiyor veya dosya boş.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
            }
            catch (Exception ex)
            {
                StatusMessage = "Hazır.";
                UnsupportedMessage = $"Önizleme yüklenirken hata:\n{ex.Message}";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                _logger.LogError(ex, "Önizleme yüklenirken hata oluştu: {FilePath}", SelectedSearchResult?.Path);
            }
            finally
            {
                // Vurgulamayı GÜNCELLE (Rapor Modu için)
                // Canlı Mod'dayken 'SelectedPreviewContent' boş olacağı için 
                // 'HighlightPreviewContent' sadece boş bir dize atayacaktır, bu da sorun değil.
                HighlightPreviewContent();
            }
        }

        // YENİ YARDIMCI METOT (ADIM 4.4)
        private void HighlightPreviewContent()
        {
            // 'SelectedPreviewContent' ayarlandıktan SONRA çalışmalı
            if (!string.IsNullOrEmpty(SelectedPreviewContent) && _lastExecutedLuceneQuery != null)
            {
                try
                {
                    // 1. Vurgulayıcıyı ayarla
                    IFormatter formatter = new SimpleHTMLFormatter("<B>", "</B>");
                    using var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    QueryScorer scorer = new QueryScorer(_lastExecutedLuceneQuery);
                    Highlighter highlighter = new Highlighter(formatter, scorer);

                    // YENİ ÇÖZÜM (KISA METİN SORUNU İÇİN):
                    // 'Highlighter'a metni bölmemesini, tamamını tek bir parça (fragment) olarak almasını söyle.
                    highlighter.TextFragmenter = new NullFragmenter();

                    // YENİ ÇÖZÜM (BÜYÜK DOSYA SORUNU İÇİN):
                    // Analiz edilecek maksimum karakter sayısını artır (örn: 5 MB).
                    // (Dosyalarınız çok büyükse bu değeri artırabilirsiniz, ancak RAM kullanımını artırır).
                    highlighter.MaxDocCharsToAnalyze = 5 * 1024 * 1024; // 5 MB sınırı

                    // 2. Metni vurgula
                    using TokenStream tokenStream = analyzer.GetTokenStream("content", new StringReader(SelectedPreviewContent));
                    // Metnin tamamını tek bir parça olarak al (maxNumFragments: 1)
                    string result = highlighter.GetBestFragments(tokenStream, SelectedPreviewContent, 1, "...");

                    if (string.IsNullOrEmpty(result))
                    {
                        // Eşleşme bulamazsa (veya GetBestFragments boş dönerse), düz metni ata
                        HighlightedPreviewContent = SelectedPreviewContent;
                    }
                    else
                    {
                        // Vurgulanmış metni ata
                        HighlightedPreviewContent = result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Vurgulama sırasında hata oluştu.");
                    HighlightedPreviewContent = SelectedPreviewContent; // Hata olursa düz metni göster
                }
            }
            else
            {
                // Metin yoksa (örn. resim/video gösteriliyorsa veya hata oluştuysa)
                // vurgulanmış içeriği de temizle
                HighlightedPreviewContent = SelectedPreviewContent; // (boş string'e eşit olacak)
            }
        }

        // OpenFileLocation (SİZİN KODUNUZDAKİ GİBİ AYNI)
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
                if (!File.Exists(path))
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

        // UpdateCommandCanExecute (SİZİN KODUNUZDAKİ GİBİ AYNI)
        private void UpdateCommandCanExecute()
        {
            (SearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportResultsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // INotifyPropertyChanged (SİZİN KODUNUZDAKİ GİBİ AYNI)
        public event PropertyChangedEventHandler? PropertyChanged;
        // INotifyPropertyChanged (BU METODUN İÇİNİ GÜNCELLİYORUZ)
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            // SİZİN MANTIĞINIZI VE YENİ MANTIĞI BİRLEŞTİREN GÜNCEL BLOK
            if (name == nameof(SelectedPreviewContent) ||
                name == nameof(SelectedPreviewImagePath) ||
                name == nameof(SelectedPreviewVideoPath) ||
                name == nameof(SelectedPreviewWebViewUri)) // YENİ EKLENDİ
            {
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(VideoPreviewVisibility));
                OnPropertyChanged(nameof(WebViewPreviewVisibility)); // YENİ EKLENDİ
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
            }

            // SİZİN MEVCUT MANTIĞINIZ (Dokunulmadı)
            if (name == nameof(UnsupportedMessage))
            {
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
            }
        }

    }
}