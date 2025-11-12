using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WpfIndexer.Helpers;
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.Views;
using Microsoft.Extensions.Logging;
using System.Drawing.Imaging;
using Serilog;
using Lucene.Net.Store;
using System.Collections.Generic;
using Microsoft.Win32;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Lucene.Net.Index;

// Lucene 'using'
using Lucene.Net.Util;
using Lucene.Net.Search;
using Lucene.Net.Search.Highlight;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;

// Canlı Mod için 'using'
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using OpenXmlPowerTools;
using DocumentFormat.OpenXml.Packaging;
using ClosedXML.Excel;

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
                _ = LoadPreviewAsync();
                (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        // Komutlar
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

            // Komut tanımlamaları
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
            if (e.PropertyName == nameof(UserSettings.EnablePreview))
            {
                OnPropertyChanged(nameof(TextPreviewVisibility));
                OnPropertyChanged(nameof(ImagePreviewVisibility));
                OnPropertyChanged(nameof(VideoPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                OnPropertyChanged(nameof(UnsupportedMessage));
            }

            if (e.PropertyName == nameof(UserSettings.PreviewMode))
            {
                _ = LoadPreviewAsync();
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

                var response = await _indexService.SearchAsync(Query, validIndexPaths, UserSettings.DefaultSearchResultLimit);
                stopwatch.Stop();

                _lastExecutedLuceneQuery = response.LuceneQuery;

                if (_lastExecutedLuceneQuery == null && !string.IsNullOrEmpty(Query))
                {
                    StatusMessage = "Arama sorgusu geçersiz (örn: tek *).";
                    _logger.LogWarning("Geçersiz veya boş Lucene sorgusu: {Query}", Query);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    SearchResults.Clear();
                    foreach (var item in response.Results)
                        SearchResults.Add(item);

                    StatusMessage = $"{response.Results.Count} sonuç {stopwatch.Elapsed.TotalSeconds:F2} saniyede bulundu.";
                    _logger.LogInformation("Arama tamamlandı: Sorgu '{Query}', Sonuç: {ResultCount}, Süre: {Duration}ms", Query, response.Results.Count, stopwatch.ElapsedMilliseconds);

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

        // Arama Önerileri
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

        // Öneri Seçimi
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

        private async Task LoadPreviewAsync()
        {
            _previewCts?.Cancel();
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            SelectedPreviewVideoPath = null;
            SelectedPreviewImagePath = null;
            SelectedPreviewContent = string.Empty;
            SelectedPreviewWebViewUri = null;
            WebViewPreviewVisibility = Visibility.Collapsed;

            if (SelectedSearchResult == null)
            {
                UnsupportedMessage = "Önizleme için bir dosya seçin.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            var path = SelectedSearchResult.Path;
            var ext = SelectedSearchResult.Extension.ToLowerInvariant();

            if (path.Contains("|"))
            {
                UnsupportedMessage = $"'{Path.GetFileName(path)}' bir arşivin (ZIP, RAR vb.) içindedir.\n\nÖnizleme şu anda arşiv içi dosyalar için desteklenmemektedir.";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            if (!System.IO.File.Exists(path))
            {
                UnsupportedMessage = $"Dosya bulunamadı: {path}";
                OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                return;
            }

            try
            {
                // Resim/Video (Her iki modda da ortak)
                if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif" || ext == ".tiff" || ext == ".webp")
                {
                    SelectedPreviewImagePath = path;
                    OnPropertyChanged(nameof(ImagePreviewVisibility));
                    return;
                }
                if (ext == ".mp4" || ext == ".wmv" || ext == ".avi" || ext == ".mkv" || ext == ".mov" || ext == ".mpeg" || ext == ".webm")
                {
                    string thumb = await Task.Run(() => FileProcessor.ExtractVideoThumbnail(path));

                    if (System.IO.File.Exists(thumb))
                    {
                        SelectedPreviewImagePath = thumb;
                        OnPropertyChanged(nameof(ImagePreviewVisibility));
                        return;
                    }
                }

                bool livePreviewSuccessful = false;

                // ********* ÖNCE CANLI MODU DENE *********
                if (UserSettings.EnablePreview && UserSettings.PreviewMode == PreviewMode.Canli)
                {
                    // WebView2'nin doğrudan açabildiği dosyalar (PDF, TXT, vb.)
                    if (ext == ".pdf" || ext == ".txt" || ext == ".log" || ext == ".xml" || ext == ".json" || ext == ".cs" || ext == ".html" || ext == ".css")
                    {
                        SelectedPreviewWebViewUri = new Uri(path);
                        WebViewPreviewVisibility = Visibility.Visible;
                        livePreviewSuccessful = true;
                    }
                    // HTML'e DÖNÜŞÜM GEREKEN dosyalar (DOCX, XLSX, CSV)
                    else if (ext == ".docx" || ext == ".xlsx" || ext == ".csv")
                    {
                        StatusMessage = "Canlı önizleme için dosya dönüştürülüyor...";
                        string? htmlPath = await ConvertFileToHtmlAsync(path, ext, Query, token);
                        StatusMessage = "Hazır.";

                        if (token.IsCancellationRequested) return;

                        if (htmlPath != null && System.IO.File.Exists(htmlPath))
                        {
                            SelectedPreviewWebViewUri = new Uri(htmlPath);
                            WebViewPreviewVisibility = Visibility.Visible;
                            livePreviewSuccessful = true;
                        }
                    }
                }

                // ********* RAPOR MODU (veya Canlı Mod Başarısız Olursa YEDEK MOD) *********
                if (UserSettings.EnablePreview && !livePreviewSuccessful)
                {
                    if (UserSettings.PreviewMode == PreviewMode.Canli)
                    {
                        StatusMessage = "Canlı mod desteklenmiyor, Rapor moduna düşülüyor...";
                    }
                    else
                    {
                        StatusMessage = "Önizleme indeksten yükleniyor...";
                    }

                    var indexDefinition = IndexSettings.Indexes.FirstOrDefault(i => i.Name == SelectedSearchResult.IndexName);
                    if (indexDefinition == null)
                    {
                        UnsupportedMessage = "Hata: İndeks tanımı bulunamadı.";
                        StatusMessage = "Hazır.";
                        OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                        return;
                    }

                    string content = await _indexService.GetContentByPathAsync(indexDefinition.IndexPath, SelectedSearchResult.Path);
                    StatusMessage = "Hazır.";

                    if (token.IsCancellationRequested) return;

                    if (!string.IsNullOrEmpty(content))
                    {
                        SelectedPreviewContent = content;
                        OnPropertyChanged(nameof(TextPreviewVisibility));
                        return;
                    }
                }

                if (!livePreviewSuccessful)
                {
                    UnsupportedMessage = $"Bu dosya türü ({ext}) için önizleme desteklenmiyor veya dosya boş.";
                    OnPropertyChanged(nameof(UnsupportedPreviewVisibility));
                }
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
                if (!token.IsCancellationRequested)
                {
                    HighlightPreviewContent();
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

            string pattern = "(>)([^<]*)(<)";

            return Regex.Replace(htmlContent, pattern, m =>
            {
                string textBetweenTags = m.Groups[2].Value;
                foreach (var term in terms)
                {
                    textBetweenTags = Regex.Replace(textBetweenTags,
                        Regex.Escape(term),
                        match => $"<mark style='background:yellow; color:black;'>{match.Value}</mark>",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                return m.Groups[1].Value + textBetweenTags + m.Groups[3].Value;
            }, RegexOptions.CultureInvariant);
        }

        private async Task<string?> ConvertFileToHtmlAsync(string sourcePath, string extension, string rawQuery, CancellationToken token)
        {
            string tempHtmlDir = Path.Combine(Path.GetTempPath(), "WpfIndexerPreview");
            System.IO.Directory.CreateDirectory(tempHtmlDir);
            string tempHtmlPath = Path.Combine(tempHtmlDir, $"preview_{Guid.NewGuid()}.html");
            string htmlContent = string.Empty;

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

                            using (var wDoc = WordprocessingDocument.Open(memoryStream, true))
                            {
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

                        var html = new StringBuilder("<html><head><meta charset='UTF-8'><style>table { border-collapse: collapse; font-family: sans-serif; } th, td { border: 1px solid #ccc; padding: 4px; text-align: left; mso-number-format:\\@; }</style></head><body>");

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

                                    // DÜZELTME (Hata CS1503): 'CellsUsed(true)' -> 'CellsUsed(XLCellsUsedOptions.Contents)'
                                    foreach (var cell in row.CellsUsed(XLCellsUsedOptions.Contents).Take(50))
                                    {
                                        html.Append($"<td>{cell.GetFormattedString()}</td>");
                                    }
                                    html.Append("</tr>");
                                }
                                html.Append("</table>");
                            }
                        }
                        html.Append("</body></html>");
                        htmlContent = html.ToString();
                    }, token);
                }
                // ********* .CSV İÇİN (Mevcut kod) *********
                else if (extension == ".csv")
                {
                    var html = new StringBuilder("<html><head><meta charset='UTF-8'><style>table { border-collapse: collapse; font-family: sans-serif; } th, td { border: 1px solid #ccc; padding: 4px; text-align: left; }</style></head><body><table>");

                    string[] lines = await System.IO.File.ReadAllLinesAsync(sourcePath, token);

                    if (lines.Length > 0)
                    {
                        html.Append("<thead><tr>");
                        foreach (var cell in lines[0].Split(',')) { html.Append($"<th>{cell}</th>"); }
                        html.Append("</tr></thead>");
                        html.Append("<tbody>");
                        foreach (var line in lines.Skip(1).Take(1000))
                        {
                            if (token.IsCancellationRequested) return null;
                            html.Append("<tr>");
                            foreach (var cell in line.Split(',')) { html.Append($"<td>{cell}</td>"); }
                            html.Append("</tr>");
                        }
                        html.Append("</tbody>");
                    }
                    html.Append("</table></body></html>");
                    htmlContent = html.ToString();
                }
                else
                {
                    return null;
                }

                if (token.IsCancellationRequested) return null;

                // Dönüşen HTML'i vurgula
                // Hata CS0126 ('string' hatası) bu satırdaydı,
                // yukarıdaki ImageHandler düzeltildiği için artık çalışacaktır.
                htmlContent = ApplyHtmlHighlighting(htmlContent, rawQuery);

                if (token.IsCancellationRequested) return null;

                await System.IO.File.WriteAllTextAsync(tempHtmlPath, htmlContent, token);
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
        // --- DİĞER METODLAR ---
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

        private void UpdateCommandCanExecute()
        {
            (SearchCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenFileLocationCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportResultsCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        // --- INotifyPropertyChanged ---
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

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
    }
}