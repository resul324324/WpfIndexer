using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Diagnostics;
using WpfIndexer.Models;
using WpfIndexer.Services;
using WpfIndexer.Helpers;
using Microsoft.Extensions.Logging; // YENİ: Logger için

namespace WpfIndexer.ViewModels
{
    public class IndexUpdateViewModel : INotifyPropertyChanged
    {
        private readonly IIndexService _indexService;
        private readonly IndexSettingsService _settingsService;
        private CancellationTokenSource? _cts;
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly ILogger<IndexUpdateViewModel> _logger; // YENİ

        private readonly IndexDefinition _indexToUpdate;

        // GÜNCELLENDİ: Constructor'a ILogger eklendi
        public IndexUpdateViewModel(IIndexService indexService, IndexSettingsService settingsService, IndexDefinition indexToUpdate, ILogger<IndexUpdateViewModel> logger)
        {
            _indexService = indexService ?? throw new ArgumentNullException(nameof(indexService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _indexToUpdate = indexToUpdate ?? throw new ArgumentNullException(nameof(indexToUpdate));
            _logger = logger; // YENİ

            IndexName = _indexToUpdate.Name;
            SourcePath = _indexToUpdate.SourcePath ?? string.Empty;

            SelectedOcrQuality = _indexToUpdate.OcrQuality;
            _selectedExtensions = _indexToUpdate.Extensions ?? new List<string>();

            UpdateSelectedExtensionsSummary();

            StartUpdateCommand = new RelayCommand(async _ => await StartUpdateAsync(), _ => true);
            CancelCommand = new RelayCommand(_ => CancelIndexing(), _ => _cts != null && !_cts.IsCancellationRequested);
            CloseCommand = new RelayCommand(_ => CloseWindow(), _ => true);

            OpenSelectFileTypesCommand = new RelayCommand(_ => OpenSelectFileTypes());
        }

        #region Bindable props

        public string IndexName { get; }
        public string SourcePath { get; }

        private OcrQuality _selectedOcrQuality;
        public OcrQuality SelectedOcrQuality
        {
            get => _selectedOcrQuality;
            set { _selectedOcrQuality = value; OnPropertyChanged(); }
        }

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
        public ICommand StartUpdateCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand OpenSelectFileTypesCommand { get; }

        public Action<bool>? RequestClose { get; set; }
        public bool OperationSucceeded { get; private set; } = false;

        private List<string> _selectedExtensions;

        private void OpenSelectFileTypes()
        {
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
                SelectedExtensionsSummary = "(Hata: Dosya türü seçilmemiş)";
                return;
            }
            SelectedExtensionsSummary = string.Join(", ", _selectedExtensions.Take(8));
            if (_selectedExtensions.Count > 8) SelectedExtensionsSummary += $" (+{_selectedExtensions.Count - 8})";
        }

        private async Task StartUpdateAsync()
        {
            if (string.IsNullOrEmpty(SourcePath))
            {
                StatusMessage = "Geçerli kaynak yolu yok.";
                return;
            }

            var extensions = _selectedExtensions;
            if (extensions == null || !extensions.Any())
            {
                StatusMessage = "Hata: Güncelleme için en az bir dosya türü seçilmelidir.";
                return;
            }

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
                StatusMessage = "Güncelleme başlatılıyor...";
                _logger.LogInformation("Güncelleme başlıyor: {IndexName}", IndexName); // YENİ

                var indexPath = _indexToUpdate.IndexPath;
                await _indexService.UpdateIndexAsync(SourcePath, indexPath, extensions, progress, _cts.Token, SelectedOcrQuality);

                // GÜNCELLENDİ: CS0122 ve CS0815 hatalarını çözmek için
                try
                {
                    // _indexToUpdate NESNESİ ZATEN servisin koleksiyonundaki
                    // ana nesnedir. Sadece onu güncellemek yeterli.
                    _indexToUpdate.OcrQuality = this.SelectedOcrQuality;
                    _indexToUpdate.Extensions = extensions;

                    // Servisin kaydetme metodunu çağır
                    _settingsService.SaveIndexes();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Güncelleme sonrası ayarlar kaydedilemedi: {IndexName}", IndexName); // YENİ
                }
                // ----------------------------------------------------

                StatusMessage = "Güncelleme tamamlandı.";
                OperationSucceeded = true;
                RequestClose?.Invoke(true);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Güncelleme iptal edildi.";
                _logger.LogWarning("Güncelleme iptal edildi: {IndexName}", IndexName); // YENİ
                OperationSucceeded = false;
                RequestClose?.Invoke(false);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Güncelleme hatası: {ex.Message}";
                _logger.LogError(ex, "Güncelleme sırasında kritik hata: {IndexName}", IndexName); // YENİ
                OperationSucceeded = false;
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
            _cts?.Cancel();
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