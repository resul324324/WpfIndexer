using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using WpfIndexer.Helpers;
using WpfIndexer.Models;
using WpfIndexer.Services;
using Microsoft.Extensions.Logging; // YENİ: Logger için

namespace WpfIndexer.ViewModels
{
    public class IndexCreationViewModel : INotifyPropertyChanged
    {
        private readonly IIndexService _indexService;
        private readonly IndexSettingsService _settingsService;
        private CancellationTokenSource? _cts;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly ILogger<IndexCreationViewModel> _logger; // YENİ

        // GÜNCELLENDİ: Constructor'a ILogger eklendi
        public IndexCreationViewModel(IIndexService indexService, IndexSettingsService settingsService, ILogger<IndexCreationViewModel> logger)
        {
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _logger = logger; // YENİ

            // Defaults
            SourcePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            IndexRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "indexes");
            IndexPath = IndexRoot;

            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                IndexName = new DirectoryInfo(desktop).Name + "_Index";
            }
            catch
            {
                IndexName = "NewIndex";
            }

            try { Directory.CreateDirectory(IndexRoot); } catch { /* ignore */ }

            BrowseSourceCommand = new RelayCommand(_ => BrowseSource());
            BrowseIndexCommand = new RelayCommand(_ => BrowseIndex());
            OpenSelectFileTypesCommand = new RelayCommand(_ => OpenSelectFileTypes());
            StartIndexCommand = new RelayCommand(async _ => await StartIndexAsync(), _ => true);
            CancelCommand = new RelayCommand(_ => CancelIndexing(), _ => _cts != null && !_cts.IsCancellationRequested);
            CloseCommand = new RelayCommand(_ => CloseWindow(), _ => true);

            _selectedExtensions = FileProcessor.RecommendedGroup.SelectMany(g => g.Value).Distinct().ToList();
            UpdateSelectedExtensionsSummary();

            // GÜNCELLENDİ: CS0122 hatasını çözmek için kaldırıldı.
            // Bu metot artık IndexSettingsService'in içinde private.
            // LoadExistingIndexes(); 
        }

        #region Bindable properties

        private string _sourcePath = "";
        public string SourcePath { get => _sourcePath; set { _sourcePath = value; OnPropertyChanged(); } }

        private string _indexRoot = "";
        public string IndexRoot { get => _indexRoot; set { _indexRoot = value; OnPropertyChanged(); } }

        private string _indexPath = "";
        public string IndexPath { get => _indexPath; set { _indexPath = value; OnPropertyChanged(); } }

        private string _indexName = "";
        public string IndexName { get => _indexName; set { _indexName = value; OnPropertyChanged(); } }

        private OcrQuality _selectedOcrQuality = OcrQuality.Off;
        public OcrQuality SelectedOcrQuality { get => _selectedOcrQuality; set { _selectedOcrQuality = value; OnPropertyChanged(); } }

        public IEnumerable<OcrQuality> OcrQualityOptions => Enum.GetValues(typeof(OcrQuality)).Cast<OcrQuality>();

        private string _selectedExtensionsSummary = "";
        public string SelectedExtensionsSummary { get => _selectedExtensionsSummary; set { _selectedExtensionsSummary = value; OnPropertyChanged(); } }

        private string _statusMessage = "Bekleniyor...";
        public string StatusMessage { get => _statusMessage; set { _statusMessage = value; OnPropertyChanged(); } }

        private string _currentFile = "";
        public string CurrentFile { get => _currentFile; set { _currentFile = value; OnPropertyChanged(); } }

        private int _progressValue = 0;
        public int ProgressValue { get => _progressValue; set { _progressValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercentage)); } }

        private int _progressMax = 1;
        public int ProgressMax { get => _progressMax; set { _progressMax = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressPercentage)); } }

        private bool _isIndeterminate = false;
        public bool IsIndeterminate { get => _isIndeterminate; set { _isIndeterminate = value; OnPropertyChanged(); } }

        public string ProgressPercentage => ProgressMax == 0 ? "0%" : $"{(int)(ProgressValue * 100.0 / ProgressMax)}%";

        private string _elapsedTime = "00:00:00";
        public string ElapsedTime { get => _elapsedTime; set { _elapsedTime = value; OnPropertyChanged(); } }

        private string _remainingTime = "--:--:--";
        public string RemainingTime { get => _remainingTime; set { _remainingTime = value; OnPropertyChanged(); } }

        private string _progressCounterText = "0 / 0";
        public string ProgressCounterText { get => _progressCounterText; set { _progressCounterText = value; OnPropertyChanged(); } }

        #endregion

        // Commands
        public ICommand BrowseSourceCommand { get; }
        public ICommand BrowseIndexCommand { get; }
        public ICommand OpenSelectFileTypesCommand { get; }
        public ICommand StartIndexCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CloseCommand { get; }

        public Action<bool>? RequestClose { get; set; }
        public Action<bool>? IndexingCompletedCallback { get; set; }
        public bool OperationSucceeded { get; private set; } = false;

        private List<string> _selectedExtensions = new();

        private void BrowseSource()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "İndekslenecek kaynak klasörü seçin" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                SourcePath = dlg.SelectedPath;
            }
        }

        private void BrowseIndex()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "İndeksin kök klasörünü seçin" };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                IndexPath = dlg.SelectedPath;
            }
        }

        private void OpenSelectFileTypes()
        {
            // YENİ: SelectFileTypesViewModel DI üzerinden çözümlenmeli (eğer varsa)
            // Şimdilik 'new' ile devam ediliyor.
            var vm = new SelectFileTypesViewModel();
            var win = new Views.SelectFileTypesWindow { DataContext = vm, Owner = System.Windows.Application.Current.MainWindow };
            if (win.ShowDialog() == true)
            {
                _selectedExtensions = vm.GetSelectedExtensions();
                UpdateSelectedExtensionsSummary();
            }
        }

        private void UpdateSelectedExtensionsSummary()
        {
            if (_selectedExtensions == null || !_selectedExtensions.Any())
            {
                SelectedExtensionsSummary = "(varsayılan: önerilen grup)";
                return;
            }
            SelectedExtensionsSummary = string.Join(", ", _selectedExtensions.Take(8));
            if (_selectedExtensions.Count > 8) SelectedExtensionsSummary += $" (+{_selectedExtensions.Count - 8})";
        }

        // GÜNCELLENDİ: CS0122 hatasını çözmek için bu metot kaldırıldı.
        // private void LoadExistingIndexes()
        // {
        //     try
        //     {
        //         var _ = _settingsService.LoadIndexes();
        //     }
        //     catch
        //     {
        //         // ignore
        //     }
        // }

        private async Task StartIndexAsync()
        {
            StatusMessage = "Doğrulanıyor...";
            if (string.IsNullOrWhiteSpace(SourcePath) || !Directory.Exists(SourcePath))
            {
                StatusMessage = "Geçerli bir kaynak klasör seçin.";
                return;
            }

            if (string.IsNullOrWhiteSpace(IndexName))
            {
                StatusMessage = "Geçerli bir indeks adı girin.";
                return;
            }

            var targetIndexFolder = Path.Combine(IndexPath, IndexName);
            try
            {
                Directory.CreateDirectory(targetIndexFolder);
            }
            catch (Exception ex)
            {
                StatusMessage = $"İndeks klasörü oluşturulamadı: {ex.Message}";
                _logger.LogError(ex, "İndeks klasörü oluşturulamadı: {Path}", targetIndexFolder); // YENİ
                return;
            }

            var extensions = _selectedExtensions.Any() ? _selectedExtensions : FileProcessor.RecommendedGroup.SelectMany(g => g.Value).Distinct().ToList();

            var progress = new Progress<ProgressReportModel>(report =>
            {
                StatusMessage = report.Message;
                CurrentFile = report.CurrentFile;
                IsIndeterminate = report.IsIndeterminate;
                ElapsedTime = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");

                if (!report.IsIndeterminate || report.Current == 0)
                {
                    ProgressValue = report.Current;
                    ProgressMax = report.Total;
                    ProgressCounterText = $"{report.Current} / {report.Total}";

                    if (report.Current == 0 || report.Total == 0)
                    {
                        RemainingTime = "--:--:--";
                    }
                    else
                    {
                        var timePer = TimeSpan.FromTicks(_stopwatch.Elapsed.Ticks / Math.Max(1, report.Current));
                        var remaining = TimeSpan.FromTicks(timePer.Ticks * (report.Total - report.Current));
                        RemainingTime = remaining.ToString(@"hh\:mm\:ss");
                    }
                }
            });

            _cts = new CancellationTokenSource();
            _stopwatch.Restart();

            try
            {
                StatusMessage = "İndeksleme başlatılıyor...";
                _logger.LogInformation("İndeksleme başlıyor: {IndexName}, Kaynak: {SourcePath}", IndexName, SourcePath); // YENİ

                var resultCount = await _indexService.IndexDirectoryAsync(SourcePath, targetIndexFolder, extensions, progress, _cts.Token, SelectedOcrQuality);

                // GÜNCELLENDİ: CS0122 ve CS0815 hatalarını çözmek için
                // IndexSettingsService'in yeni yöntemleri kullanıldı.
                var newIndex = new IndexDefinition
                {
                    Name = IndexName,
                    IndexPath = targetIndexFolder,
                    SourcePath = SourcePath,
                    OcrQuality = SelectedOcrQuality,
                    Extensions = extensions.ToList(),
                    IsSelected = true
                };

                // Servisin paylaşılan koleksiyonuna ekle
                _settingsService.AddIndex(newIndex);
                // Servisin kaydetme metodunu çağır
                _settingsService.SaveIndexes();
                // ----------------------------------------------------

                StatusMessage = $"İşlem tamamlandı. {resultCount} öğe işlendi.";
                OperationSucceeded = true;
                IndexingCompletedCallback?.Invoke(true);
                RequestClose?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "İşlem iptal edildi.";
                OperationSucceeded = false;
                _logger.LogWarning("İndeksleme iptal edildi: {IndexName}", IndexName); // YENİ
                IndexingCompletedCallback?.Invoke(false);
                RequestClose?.Invoke(false);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Hata: {ex.Message}";
                OperationSucceeded = false;
                _logger.LogError(ex, "İndeksleme sırasında kritik hata: {IndexName}", IndexName); // YENİ
                IndexingCompletedCallback?.Invoke(false);
                RequestClose?.Invoke(false);
            }
            finally
            {
                _cts = null;
                _stopwatch.Stop();
            }
        }

        private void CancelIndexing()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }

        private void CloseWindow()
        {
            RequestClose?.Invoke(false);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}