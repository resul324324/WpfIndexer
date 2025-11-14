using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpfIndexer.Models;

namespace WpfIndexer.Services
{
    /// <summary>
    /// Uygulama başlangıcında ve dosya değişikliklerinde
    /// indeksleri sessizce güncelleyen arka plan servisi.
    /// </summary>
    public class AutoUpdateService : IDisposable
    {
        private readonly IIndexService _indexService;
        private readonly IndexSettingsService _indexSettingsService;
        private readonly UserSettingsService _userSettingsService;
        private readonly ILogger<AutoUpdateService> _logger;

        // DÜZELTME (CS8618): Olayın (event) null olması normaldir, '?' ekliyoruz.
        public event Action<string>? StatusChanged;

        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();

        // DÜZELTME (CS8618): Bu Timer, constructor'da değil Start() metodunda
        // başlatıldığı için nullable ('?') olmalıdır.
        private Timer? _startupTimer;

        private Timer _debounceTimer; // Bu constructor'da atanıyor, sorun yok.
        private volatile bool _isChecking = false;
        private string _lastStatusMessage = "";

        // Not: Sizin 3000ms (3sn) ve 6000ms (6sn) ayarlarınızı koruyorum.
        private const int StartupDelay = 10000;     // 10 saniye
        private const int DebouncePeriod = 500000;   // 500 saniye

        public AutoUpdateService(
            IIndexService indexService,
            IndexSettingsService indexSettingsService,
            UserSettingsService userSettingsService,
            ILogger<AutoUpdateService> logger)
        {
            _indexService = indexService;
            _indexSettingsService = indexSettingsService;
            _userSettingsService = userSettingsService;
            _logger = logger;
            _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
            // _startupTimer burada atanmadığı için CS8618 uyarısı alıyordunuz.
        }

        public void Start()
        {
            if (!_userSettingsService.Settings.AutoUpdateEnabled)
            {
                _logger.LogInformation("Otomatik güncelleme servisi ayarlardan kapatılmış.");
                return;
            }

            // DÜZELTME (CS8622): Timer'ın 'state' parametresi null olabilir (object?)
            _startupTimer = new Timer(OnStartupTimerElapsed, null, StartupDelay, Timeout.Infinite);
            InitializeWatchers();
            _logger.LogInformation("AutoUpdateService başlatıldı. 3 saniye sonra ilk kontrol yapılacak.");
        }

        private void InitializeWatchers()
        {
            var indexesToWatch = _indexSettingsService.Indexes
                .Where(i => !string.IsNullOrEmpty(i.SourcePath) && Directory.Exists(i.SourcePath));

            foreach (var index in indexesToWatch)
            {
                try
                {
                    // DÜZELTME (CS8604): .Where filtresi sayesinde SourcePath'in
                    // null olmadığını biliyoruz. '!' ile derleyiciye güvence veriyoruz.
                    var watcher = new FileSystemWatcher(index.SourcePath!)
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };
                    watcher.Changed += OnFileSystemChanged;
                    watcher.Created += OnFileSystemChanged;
                    watcher.Deleted += OnFileSystemChanged;
                    watcher.Renamed += OnFileSystemChanged;
                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Path} klasörü izlenemiyor.", index.SourcePath);
                }
            }
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            // DÜZELTME (CS8602): e.Name null olabilir, önce onu kontrol et.
            if (e.Name == null || e.Name.Contains("wpfindexer.update.lock") || e.Name.StartsWith("~"))
                return;

            _logger.LogInformation("Dosya değişikliği algılandı: {File}.", e.Name);
            _debounceTimer.Change(DebouncePeriod, Timeout.Infinite);
        }

        // DÜZELTME (CS8622): 'state' parametresi null olabilir (object?)
        private async void OnStartupTimerElapsed(object? state)
        {
            await CheckAllIndexesAsync("Başlangıç kontrolü");
        }

        // DÜZELTME (CS8622): 'state' parametresi null olabilir (object?)
        private async void OnDebounceTimerElapsed(object? state)
        {
            await CheckAllIndexesAsync("Dosya değişikliği");
        }

        public string GetLastStatusMessage() => _lastStatusMessage;

        private void NotifyStatus(string message)
        {
            _lastStatusMessage = message;
            // StatusChanged artık nullable (?) olduğu için, '?.' ile çağırmak zaten güvenli.
            StatusChanged?.Invoke(message);
        }

        private async Task CheckAllIndexesAsync(string trigger)
        {
            if (_isChecking) return;
            if (!_userSettingsService.Settings.AutoUpdateEnabled) return;

            _isChecking = true;
            _logger.LogInformation("Otomatik güncelleme kontrolü başladı. Tetikleyen: {Trigger}", trigger);
            NotifyStatus("Güncellemeler kontrol ediliyor...");

            var indexesToUpdate = _indexSettingsService.Indexes
                .Where(i => !string.IsNullOrEmpty(i.SourcePath));

            foreach (var index in indexesToUpdate)
            {
                await CheckSingleIndexAsync(index);
            }

            NotifyStatus("Kontrol tamamlandı.");
            _isChecking = false;
        }

        private async Task CheckSingleIndexAsync(IndexDefinition index)
        {
            try
            {
                var progress = new Progress<ProgressReportModel>(report =>
                {
                    if (!string.IsNullOrEmpty(report.CurrentFile))
                        NotifyStatus($"Güncelleniyor ({index.Name}): {Path.GetFileName(report.CurrentFile)}");
                    else if (report.IsIndeterminate)
                        NotifyStatus($"{index.Name} için {report.Message}...");
                });

                // DÜZELTME (CS8604): SourcePath'in null olmadığını '!' ile,
                // Extensions'ın null olabileceğini '??' ile belirtiyoruz.
                var result = await _indexService.UpdateIndexAsync(
                    index.SourcePath!, // .Where ile null olamaz
                    index.IndexPath,
                    index.Extensions ?? new List<string>(), // Null ise boş liste kullan
                    progress,
                    CancellationToken.None,
                    index.OcrQuality
                );

                int changes = result.Added.Count + result.Updated.Count + result.Deleted.Count;
                if (changes > 0)
                {
                    _logger.LogInformation("{IndexName} güncellendi: {Changes} dosya etkilendi.", index.Name, changes);
                    NotifyStatus($"{index.Name} güncellendi: {changes} dosya etkilendi.");
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("başka bir bilgisayar") || ex.Message.Contains("güncelleniyor"))
                {
                    _logger.LogInformation("{IndexName} kilitli, güncelleme atlandı.", index.Name);
                    NotifyStatus($"{index.Name} meşgul, güncelleme atlandı.");
                }
                else
                {
                    _logger.LogError(ex, "{IndexName} otomatik güncellenirken hata oluştu.", index.Name);
                    NotifyStatus($"{index.Name} güncellenirken hata oluştu.");
                }
            }
        }

        public void Dispose()
        {
            // _startupTimer artık nullable olduğu için '?.' ile dispose etmek zaten doğru.
            _startupTimer?.Dispose();
            _debounceTimer?.Dispose();
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}