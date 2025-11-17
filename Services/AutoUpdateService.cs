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
    public class AutoUpdateService : IDisposable
    {
        private readonly IIndexService _indexService;
        private readonly IndexSettingsService _indexSettingsService;
        private readonly UserSettingsService _userSettingsService;
        private readonly ILogger<AutoUpdateService> _logger;

        public event Action<string>? StatusChanged;

        private readonly List<FileSystemWatcher> _watchers = new();
        private CancellationTokenSource? _cts;
        private Task? _workerTask;

        private volatile bool _isRunning = false;
        private volatile bool _pendingUpdate = false;
        private string _lastStatus = "";

        // Kontrol aralıkları
        private const int WorkerDelay = 2000;         // 2 saniyede bir worker kontrolü
        private const int DebounceDelay = 8000;       // Dosya değişikliği sonrası 8 saniye bekleme

        private DateTime _lastChangeTime = DateTime.MinValue;

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
        }

        public void Start()
        {
            if (!_userSettingsService.Settings.AutoUpdateEnabled)
            {
                _logger.LogInformation("AutoUpdateService devre dışı.");
                return;
            }

            InitializeWatchers();

            _cts = new CancellationTokenSource();
            _workerTask = Task.Run(() => WorkerLoopAsync(_cts.Token));

            _logger.LogInformation("AutoUpdateService başlatıldı (worker aktif).");
        }

        private void InitializeWatchers()
        {
            foreach (var index in _indexSettingsService.Indexes)
            {
                if (string.IsNullOrEmpty(index.SourcePath)) continue;
                if (!Directory.Exists(index.SourcePath)) continue;

                try
                {
                    var watcher = new FileSystemWatcher(index.SourcePath)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                        IncludeSubdirectories = true,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += OnFileChanged;
                    watcher.Created += OnFileChanged;
                    watcher.Renamed += OnFileChanged;
                    watcher.Deleted += OnFileChanged;

                    _watchers.Add(watcher);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Klasör izlenemiyor: {Path}", index.SourcePath);
                }
            }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (e.Name == null) return;
            if (e.Name.StartsWith("~") || e.Name.Contains("wpfindexer.update.lock"))
                return;

            _logger.LogInformation("Dosya değişti: {File}", e.Name);

            _pendingUpdate = true;
            _lastChangeTime = DateTime.Now;
        }

        private async Task WorkerLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // AutoUpdate kapalı ise tamamen uyur
                    if (!_userSettingsService.Settings.AutoUpdateEnabled)
                    {
                        await Task.Delay(WorkerDelay, token);
                        continue;
                    }

                    // Henüz değişiklik olmadı → uyku
                    if (!_pendingUpdate)
                    {
                        await Task.Delay(WorkerDelay, token);
                        continue;
                    }

                    // Debounce süresi tamamlanmadı → bekle
                    if ((DateTime.Now - _lastChangeTime).TotalMilliseconds < DebounceDelay)
                    {
                        await Task.Delay(WorkerDelay, token);
                        continue;
                    }

                    // Zaten çalışıyor ise bekle
                    if (_isRunning)
                    {
                        await Task.Delay(WorkerDelay, token);
                        continue;
                    }

                    _isRunning = true;
                    _pendingUpdate = false;

                    await CheckAllIndexesAsync("Watcher");

                    _isRunning = false;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker döngüsü hatası.");
                }
            }
        }

        private async Task CheckAllIndexesAsync(string trigger)
        {
            NotifyStatus($"Güncelleme kontrolü ({trigger})...");
            _logger.LogInformation("AutoUpdate tetiklendi ({Trigger})", trigger);

            foreach (var index in _indexSettingsService.Indexes)
            {
                if (!Directory.Exists(index.SourcePath)) continue;
                await CheckSingleIndexAsync(index);
            }

            NotifyStatus("Kontrol tamamlandı.");
        }

        private async Task CheckSingleIndexAsync(IndexDefinition index)
        {
            try
            {
                var progress = new Progress<ProgressReportModel>(r =>
                {
                    if (!string.IsNullOrWhiteSpace(r.CurrentFile))
                        NotifyStatus($"{index.Name}: {Path.GetFileName(r.CurrentFile)} güncelleniyor...");
                });

                var result = await _indexService.UpdateIndexAsync(
                    index.SourcePath!,
                    index.IndexPath,
                    index.Extensions ?? new List<string>(),
                    progress,
                    CancellationToken.None,
                    index.OcrQuality
                );

                var total = result.Added.Count + result.Updated.Count + result.Deleted.Count;

                if (total > 0)
                    NotifyStatus($"{index.Name} güncellendi ({total} değişiklik).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{IndexName} güncelleme hatası.", index.Name);
                NotifyStatus($"{index.Name} güncellenirken hata.");
            }
        }

        private void NotifyStatus(string msg)
        {
            _lastStatus = msg;
            StatusChanged?.Invoke(msg);
        }

        public string GetLastStatusMessage() => _lastStatus;

        public void Dispose()
        {
            try
            {
                _cts?.Cancel();
                _cts?.Dispose();
                foreach (var w in _watchers)
                {
                    w.EnableRaisingEvents = false;
                    w.Dispose();
                }
                _watchers.Clear();
            }
            catch { }
        }
    }
}
