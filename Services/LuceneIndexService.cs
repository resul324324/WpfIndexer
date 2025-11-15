using System;
using System.Collections.Generic;
using System.IO; // Path.GetDirectoryName ve File.GetLastWriteTime için EKLENDİ
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using SharpCompress.Archives;
using SharpCompress.Common;
using WpfIndexer.Models;
using Directory = System.IO.Directory;

namespace WpfIndexer.Services
{
    public class LuceneIndexService : IIndexService
    {
        private const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;
        private readonly HashSet<string> _archiveExtensions = new() { ".zip", ".rar", ".7z", ".tar" };

        private IndexWriter? _writer;
        private HashSet<string>? _extHashSet;
        private IProgress<ProgressReportModel>? _progress;
        private int _fileCount;
        private int _totalCount;
        private int _maxOcrWorkers = Math.Max(1, Environment.ProcessorCount / 2);

        private const int MaxLockAgeInMinutes = 60;
        // YENİ: Kilit dosyamızın adı
        private const string LockFileName = "wpfindexer.update.lock";


        private FileStream CreateLockFile(string indexPath)
        {
            var lockFilePath = Path.Combine(indexPath, LockFileName);

            try
            {
                // 1. Kilit almayı dene (normal durum)
                // FileShare.None: Biz kapatana kadar kimse bu dosyayı açamasın.
                return new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            }
            catch (IOException ex) when (ex.Message.Contains("already exists") || ex.Message.Contains("kullanılmakta"))
            {
                // 2. KİLİT ALINAMADI. Dosya zaten var.
                // "Terk edilmiş" (stale) olup olmadığını kontrol et.
                try
                {
                    var fileInfo = new FileInfo(lockFilePath);

                    // Dosya gerçekten var mı ve 60 dakikadan daha mı eski?
                    if (fileInfo.Exists && (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > TimeSpan.FromMinutes(MaxLockAgeInMinutes)))
                    {
                        // 3. Kilit çok eski, muhtemelen çökmeden kalma. SİL.
                        // (Burada loglama yapmak çok faydalı olur)
                        File.Delete(lockFilePath);

                        // 4. Dosyayı sildikten sonra, kilidi tekrar almayı dene.
                        return new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    }
                }
                catch (Exception)
                {
                    // Eski kilidi silerken veya yeniden alırken bir hata olduysa.
                    // Güvenli tarafta kalıp kilitli olduğunu varsay.
                    throw new Exception("İndeks kilitli görünüyor (veya eski kilit silinemedi).");
                }

                // 5. Kilit var AMA eski değil (60dk'dan yeni). Normal "meşgul" durumu.
                throw new Exception("İndeks şu anda başka bir bilgisayar veya işlem tarafından güncelleniyor.");
            }
            catch (Exception ex)
            {
                // Başka bir erişim hatası (örn. yazma izni yok)
                throw new Exception($"İndeks klasörüne kilit dosyası (lock file) oluşturulamadı. Yazma izinlerini kontrol edin. Hata: {ex.Message}");
            }
        }

        public async Task<int> IndexDirectoryAsync(
            string sourcePath,
            string indexPath,
            IEnumerable<string> extensionsToInclude,
            IProgress<ProgressReportModel> progress,
            CancellationToken token,
            OcrQuality ocrQuality)
        {
            Interlocked.Exchange(ref _fileCount, 0);
            _totalCount = 0;
            _progress = progress;
            _extHashSet = new HashSet<string>(extensionsToInclude, StringComparer.OrdinalIgnoreCase);

            var ocrSemaphore = new SemaphoreSlim(_maxOcrWorkers);

            // YENİ: Kilit mekanizmasını uygula
            FileStream? lockStream = null;
            try
            {
                // İşleme başlamadan önce indeksi kilitle
                lockStream = CreateLockFile(indexPath);

                await Task.Run(async () =>
                {
                    var dir = FSDirectory.Open(indexPath);
                    using var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

                    using (_writer = new IndexWriter(dir, indexConfig))
                    {
                        _progress?.Report(new ProgressReportModel { Message = "Eski indeks temizleniyor...", IsIndeterminate = true });
                        token.ThrowIfCancellationRequested();
                        _writer.DeleteAll();

                        _progress?.Report(new ProgressReportModel { Message = "Dosya sistemi taranıyor...", IsIndeterminate = true });

                        var systemFiles = new Dictionary<string, (long Ticks, long Size)>();
                        await ScanFileSystemRecursiveAsync(sourcePath, systemFiles, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();

                        if (systemFiles.Count == 0)
                        {
                            _progress?.Report(new ProgressReportModel { Message = "Seçili türde dosya bulunamadı.", Current = 1, Total = 1 });
                            return;
                        }

                        _totalCount = systemFiles.Count;
                        _progress?.Report(new ProgressReportModel { Message = "İndeksleniyor...", Current = 0, Total = _totalCount, IsIndeterminate = false });

                        int maxDegree = Math.Max(1, Environment.ProcessorCount - 1);
                        using (var sem = new SemaphoreSlim(maxDegree))
                        {
                            var tasks = new List<Task>(systemFiles.Count);
                            foreach (var fileEntry in systemFiles)
                            {
                                // ... (Paralel işleme mantığı (tasks.Add) aynı kalıyor) ...
                                token.ThrowIfCancellationRequested();
                                await sem.WaitAsync(token).ConfigureAwait(false);

                                var path = fileEntry.Key;
                                string ext = Path.GetExtension(path).ToLowerInvariant();
                                long size = fileEntry.Value.Size;
                                long ticks = fileEntry.Value.Ticks;

                                tasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        token.ThrowIfCancellationRequested();
                                        bool needsOcr = ocrQuality != OcrQuality.Off && (
                                            ext == ".pdf" || ext == ".jpg" || ext == ".jpeg" ||
                                            ext == ".png" || ext == ".tiff" || ext == ".tif" ||
                                            ext == ".bmp" || ext == ".gif" || ext == ".webp" ||
                                            ext == ".psd" || ext == ".ai" || ext == ".eps"
                                        );

                                        if (needsOcr)
                                        {
                                            await ocrSemaphore.WaitAsync(token).ConfigureAwait(false);
                                            try
                                            {
                                                await ProcessItemAsync(path, ext, () => new DateTime(ticks, DateTimeKind.Utc), size, ocrQuality, token).ConfigureAwait(false);
                                            }
                                            finally
                                            {
                                                ocrSemaphore.Release();
                                            }
                                        }
                                        else
                                        {
                                            await ProcessItemAsync(path, ext, () => new DateTime(ticks, DateTimeKind.Utc), size, OcrQuality.Off, token).ConfigureAwait(false);
                                        }
                                    }
                                    catch (OperationCanceledException) { /* cancel */ }
                                    catch (Exception ex)
                                    {
                                        _progress?.Report(new ProgressReportModel { Message = $"İşleme hatası: {Path.GetFileName(path)} - {ex.Message}" });
                                    }
                                    finally
                                    {
                                        sem.Release();
                                    }
                                }, token));
                            }

                            await Task.WhenAll(tasks).ConfigureAwait(false);
                        }

                        _progress?.Report(new ProgressReportModel { Message = "İndeks diske yazılıyor (Flush)...", Current = _totalCount, Total = _totalCount, IsIndeterminate = true });
                        token.ThrowIfCancellationRequested();

                        // YENİ: Meta verileri indekse yaz (Tarihleri UTC olarak sakla)
                        var metadata = new Dictionary<string, string>
                        {
                            { "CreationDate", DateTime.UtcNow.ToString("o") }, // "o" = Round-trip format (ISO 8601)
                            { "LastUpdateDate", DateTime.UtcNow.ToString("o") }
                        };
                        _writer.SetCommitData(metadata);

                        _writer.Flush(true, true);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                // YENİ: Kilit dosyasını her durumda (başarılı veya hatalı) serbest bırak
                lockStream?.Close();
                var lockFilePath = Path.Combine(indexPath, LockFileName);
                if (File.Exists(lockFilePath))
                {
                    try { File.Delete(lockFilePath); } catch { /* Silinemezse ignore */ }
                }
            }

            return _fileCount;
        }

        public async Task<UpdateResult> UpdateIndexAsync(
            string sourcePath,
            string indexPath,
            IEnumerable<string> extensionsToInclude,
            IProgress<ProgressReportModel> progress,
            CancellationToken token,
            OcrQuality ocrQuality)
        {
            Interlocked.Exchange(ref _fileCount, 0);
            _progress = progress;
            _extHashSet = new HashSet<string>(extensionsToInclude, StringComparer.OrdinalIgnoreCase);

            var updateResult = new UpdateResult();
            var ocrSemaphore = new SemaphoreSlim(_maxOcrWorkers);

            // YENİ: Kilit mekanizmasını uygula
            FileStream? lockStream = null;
            try
            {
                // İşleme başlamadan önce indeksi kilitle
                lockStream = CreateLockFile(indexPath);

                // YENİ: Mevcut meta verileri (Oluşturma Tarihini) korumak için oku
                var existingMetadata = GetIndexMetadata(indexPath);

                await Task.Run(async () =>
                {
                    _progress?.Report(new ProgressReportModel { Message = "İndeks durumu okunuyor...", IsIndeterminate = true });
                    var indexFiles = GetIndexState(indexPath);
                    token.ThrowIfCancellationRequested();

                    _progress?.Report(new ProgressReportModel { Message = "Dosya sistemi taranıyor...", IsIndeterminate = true });
                    var systemFiles = new Dictionary<string, (long Ticks, long Size)>();
                    await ScanFileSystemRecursiveAsync(sourcePath, systemFiles, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();

                    // ... (Değişiklik karşılaştırma kodları aynı kalıyor) ...
                    _progress?.Report(new ProgressReportModel { Message = "Değişiklikler karşılaştırılıyor...", IsIndeterminate = true });
                    var filesToAdd = new List<string>();
                    var filesToUpdate = new List<string>();
                    var filesToDelete = new List<Term>();
                    foreach (var sysEntry in systemFiles)
                    {
                        if (!indexFiles.ContainsKey(sysEntry.Key)) { filesToAdd.Add(sysEntry.Key); }
                        else if (sysEntry.Value.Ticks > indexFiles[sysEntry.Key].Ticks || sysEntry.Value.Size != indexFiles[sysEntry.Key].Size) { filesToUpdate.Add(sysEntry.Key); }
                    }
                    foreach (var indexEntry in indexFiles)
                    {
                        if (!systemFiles.ContainsKey(indexEntry.Key)) { filesToDelete.Add(new Term("path_exact", indexEntry.Key)); updateResult.Deleted.Add(indexEntry.Key); }
                    }
                    token.ThrowIfCancellationRequested();
                    // ... (Karşılaştırma sonu) ...

                    _totalCount = filesToAdd.Count + filesToUpdate.Count + filesToDelete.Count;
                    if (_totalCount == 0)
                    {
                        _progress?.Report(new ProgressReportModel { Message = "İndeks güncel. Değişiklik yok.", Current = 1, Total = 1 });
                        return; // Değişiklik yoksa meta veriyi de güncellemeye gerek yok
                    }

                    var dir = FSDirectory.Open(indexPath);
                    using var analyzer = new StandardAnalyzer(AppLuceneVersion);
                    var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);

                    using (_writer = new IndexWriter(dir, indexConfig))
                    {
                        // ... (Silme, Ekleme, Güncelleme döngüleri (filesToDelete, filesToAdd, filesToUpdate) aynı kalıyor) ...
                        if (filesToDelete.Any())
                        {
                            _progress?.Report(new ProgressReportModel { Message = $"{filesToDelete.Count} dosya siliniyor...", Current = _fileCount, Total = _totalCount });
                            _writer.DeleteDocuments(filesToDelete.ToArray());
                            Interlocked.Add(ref _fileCount, filesToDelete.Count);
                        }
                        int maxDegree = Math.Max(1, Environment.ProcessorCount / 2);
                        using (var sem = new SemaphoreSlim(maxDegree))
                        {
                            var addTasks = new List<Task>(filesToAdd.Count);
                            foreach (var path in filesToAdd)
                            {
                                // ... (addTasks.Add(...) içindeki mantık aynı) ...
                                token.ThrowIfCancellationRequested();
                                await sem.WaitAsync(token).ConfigureAwait(false);
                                addTasks.Add(Task.Run(async () =>
                                {
                                    try
                                    {
                                        var fi = new FileInfo(path);
                                        bool needsOcr = ocrQuality != OcrQuality.Off && (fi.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".tif", StringComparison.OrdinalIgnoreCase));
                                        if (needsOcr) await ocrSemaphore.WaitAsync(token).ConfigureAwait(false);
                                        try
                                        {
                                            await ProcessItemAsync(path, fi.Extension, () => fi.LastWriteTimeUtc, fi.Length, ocrQuality, token).ConfigureAwait(false);
                                            updateResult.Added.Add(path);
                                        }
                                        finally
                                        {
                                            if (needsOcr) ocrSemaphore.Release();
                                        }
                                    }
                                    catch (Exception) { /* skip */ }
                                    finally { sem.Release(); }
                                }, token));
                            }
                            await Task.WhenAll(addTasks).ConfigureAwait(false);
                        }
                        foreach (var path in filesToUpdate)
                        {
                            // ... (filesToUpdate içindeki mantık aynı) ...
                            token.ThrowIfCancellationRequested();
                            var count = Interlocked.Increment(ref _fileCount);
                            _progress?.Report(new ProgressReportModel { Message = $"Güncelleniyor: {Path.GetFileName(path)}", CurrentFile = path, Current = count, Total = _totalCount });
                            var fi = new FileInfo(path);
                            bool needsOcr = ocrQuality != OcrQuality.Off && (fi.Extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) || fi.Extension.Equals(".tif", StringComparison.OrdinalIgnoreCase));
                            var doc = await CreateDocumentAsync(path, fi.Extension, () => fi.LastWriteTimeUtc, fi.Length, ocrQuality, token, _progress, count, _totalCount).ConfigureAwait(false);
                            if (doc != null)
                            {
                                _writer.UpdateDocument(new Term("path_exact", path), doc);
                                updateResult.Updated.Add(path);
                            }
                        }
                        // ... (Döngülerin sonu) ...

                        _progress?.Report(new ProgressReportModel { Message = "Değişiklikler kaydediliyor...", Current = _totalCount, Total = _totalCount, IsIndeterminate = true });
                        token.ThrowIfCancellationRequested();

                        // YENİ: Meta verileri güncelle (Oluşturma tarihini koru, UTC olarak sakla)
                        var metadata = new Dictionary<string, string>
                        {
                            // 'CreationDate' okunamadıysa, bu güncelleme tarihini kullan
                            // Okunduysa, UI'da göstermek için Local'e çevrilmişti, geri UTC'ye al.
                            { "CreationDate", existingMetadata.CreationDate?.ToUniversalTime().ToString("o") ?? DateTime.UtcNow.ToString("o") },
                            { "LastUpdateDate", DateTime.UtcNow.ToString("o") }
                        };
                        _writer.SetCommitData(metadata);

                        _writer.Flush(true, true);
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                // YENİ: Kilidi her durumda serbest bırak
                lockStream?.Close();
                var lockFilePath = Path.Combine(indexPath, LockFileName);
                if (File.Exists(lockFilePath))
                {
                    try { File.Delete(lockFilePath); } catch { /* Silinemezse ignore */ }
                }
            }

            return updateResult;
        }

        // DİKKAT: Dönüş tipi Task<List<SearchResult>> -> Task<LuceneSearchResponse> olarak değişti
        public async Task<LuceneSearchResponse> SearchAsync(string query, IEnumerable<string> indexPaths, int maxResults)
        {
            // Orijinal 'await Task.Run' yapınızı koruyoruz,
            // sadece içerdeki dönüş tipini değiştiriyoruz.
            return await Task.Run(() =>
            {
                // 1. YENİ: Yanıt nesnesi 'results' yerine 'response' oldu
                var response = new LuceneSearchResponse();
                var paths = indexPaths.ToList();
                if (string.IsNullOrWhiteSpace(query) || !paths.Any()) return response; // Boş yanıt dön

                StandardAnalyzer? analyzer = null;
                try
                {
                    analyzer = new StandardAnalyzer(AppLuceneVersion);
                    var parser = new MultiFieldQueryParser(AppLuceneVersion, new[] { "filename", "content", "path" }, analyzer);

                    // YENİ ÇÖZÜM: Başına * (joker) konulmuş aramaları etkinleştir
                    parser.AllowLeadingWildcard = true;

                    // Sizin mevcut sorgu mantığınız
                    string finalQuery = query;
                    bool isSpecialQuery = query.Contains("*") || query.Contains("?") || query.Contains("~") ||
                                          query.Contains("\"") || query.ToUpper().Contains(" AND ") ||
                                          query.ToUpper().Contains(" OR ") || query.ToUpper().Contains(" NOT ");
                    if (!isSpecialQuery && query.Contains(" "))
                    {
                        finalQuery = $"\"{query}\"";
                    }

                    var luceneQuery = parser.Parse(finalQuery);

                    // 2. YENİ: Oluşturulan Query nesnesini yanıta ata
                    response.LuceneQuery = luceneQuery; // <-- Vurgulama için bu nesneyi dışarı taşıyoruz

                    foreach (var path in paths)
                    {
                        var indexName = new DirectoryInfo(path).Name;
                        using var dir = FSDirectory.Open(path);
                        if (!Directory.Exists(path) || !DirectoryReader.IndexExists(dir))
                        {
                            continue;
                        }
                        using var reader = DirectoryReader.Open(dir);
                        var searcher = new IndexSearcher(reader);
                        var hits = searcher.Search(luceneQuery, maxResults).ScoreDocs;

                        // ====================================================================
                        // >>>>>>>> ADIM 2 DEĞİŞİKLİĞİ BURADA BAŞLIYOR <<<<<<<<<
                        // ====================================================================
                        foreach (var hit in hits)
                        {
                            var doc = searcher.Doc(hit.Doc);

                            // --- YENİ EKLENEN VERİ ÇEKME İŞLEMLERİ ---

                            // 1. Path (zaten vardı, sadece değişken adı alalım)
                            string docPath = doc.Get("path_exact") ?? string.Empty;
                            if (string.IsNullOrEmpty(docPath)) continue; // Path yoksa atla

                            // 2. Tarihi al (Lucene'de Ticks olarak saklanmış)
                            long.TryParse(doc.GetField("modified_date")?.GetStringValue(), out long ticks);
                            DateTime modificationDate = (ticks > 0) ? new DateTime(ticks) : DateTime.MinValue;

                            // 3. Boyutu al (zaten vardı)
                            long.TryParse(doc.Get("size"), out long size);

                            // 4. Klasör yolunu al (Sadece klasör)
                            string directoryPath = Path.GetDirectoryName(docPath) ?? docPath; // Null gelirse path'in kendisidir (örn: C:\)

                            // --- BİTTİ ---

                            // 3. YENİ: Sonuçları 'response.Results'a ekle
                            // SearchResult nesnesini YENİ özelliklerle oluştur
                            response.Results.Add(new SearchResult
                            {
                                // Mevcut 'required' alanlar
                                Path = docPath,
                                FileName = doc.Get("filename") ?? System.IO.Path.GetFileName(docPath), // Fallback
                                Extension = doc.Get("extension") ?? System.IO.Path.GetExtension(docPath), // Fallback
                                IndexName = indexName,

                                // Mevcut diğer alanlar
                                Size = size,
                                Snippet = string.Empty, // Snippet'i şimdilik boş başlatıyoruz. Vurgulama sonraki adım.

                                // YENİ EKLENEN ALANLAR
                                ModificationDate = modificationDate,
                                DirectoryPath = directoryPath,
                                FileType = string.Empty, // VM'de (sonraki adımda) doldurulacak
                                FileIcon = null          // VM'de (sonraki adımda) doldurulacak
                            });
                        }
                        // ==================================================================
                        // >>>>>>>> ADIM 2 DEĞİŞİKLİĞİ BURADA BİTİYOR <<<<<<<<<
                        // ==================================================================
                    }
                }
                catch (ParseException pex)
                {
                    _progress?.Report(new ProgressReportModel { Message = $"Hatalı Arama Sorgusu: {pex.Message}", IsIndeterminate = true });
                    // Hata durumunda response.LuceneQuery null olacak, bu ViewModel tarafından kontrol edilecek.
                }
                catch (Exception ex)
                {
                    _progress?.Report(new ProgressReportModel { Message = $"Arama Hatası: {ex.Message}", IsIndeterminate = true });
                }
                finally
                {
                    analyzer?.Dispose();
                }

                // 4. YENİ: 'results' listesi yerine 'response' nesnesini dön
                return response;
            }).ConfigureAwait(false);
        }

        public async Task<string> GetContentByPathAsync(string indexPath, string filePath)
        {
            // ... (Bu metot GÜNCELLENMEDİ, aynı kalıyor) ...
            return await Task.Run(() =>
            {
                try
                {
                    using var dir = FSDirectory.Open(indexPath);
                    if (!DirectoryReader.IndexExists(dir))
                    {
                        return "Hata: İndeks bulunamadı.";
                    }
                    using var reader = DirectoryReader.Open(dir);
                    var searcher = new IndexSearcher(reader);
                    var query = new TermQuery(new Term("path_exact", filePath));
                    var hits = searcher.Search(query, 1).ScoreDocs;
                    if (hits.Length > 0)
                    {
                        var doc = searcher.Doc(hits[0].Doc);
                        var content = doc.Get("content");
                        return content ?? "(İçerik depolanmamış)";
                    }
                    return "(Dosya indekste bulunamadı)";
                }
                catch (Exception ex)
                {
                    return $"Önizleme yüklenirken hata oluştu: {ex.Message}";
                }
            });
        }

        // YENİ METOT: İndeksten meta verileri okur
        public (DateTime? CreationDate, DateTime? LastUpdateDate) GetIndexMetadata(string indexPath)
        {
            try
            {
                using var dir = FSDirectory.Open(indexPath);
                if (!DirectoryReader.IndexExists(dir))
                {
                    return (null, null);
                }

                using var reader = DirectoryReader.Open(dir);
                // En son commit'in (yazma işleminin) meta verisini al
                var commitData = reader.IndexCommit.UserData;

                DateTime? creationDate = null;
                DateTime? lastUpdateDate = null;

                if (commitData.TryGetValue("CreationDate", out var creationDateStr))
                {
                    // "o" formatında (ISO 8601) saklanan UTC tarihi oku
                    if (DateTime.TryParse(creationDateStr, out var date))
                        creationDate = date.ToLocalTime(); // UI'da göstermek için Local'e çevir
                }

                if (commitData.TryGetValue("LastUpdateDate", out var lastUpdateDateStr))
                {
                    if (DateTime.TryParse(lastUpdateDateStr, out var date))
                        lastUpdateDate = date.ToLocalTime(); // UI'da göstermek için Local'e çevir
                }

                return (creationDate, lastUpdateDate);
            }
            catch (Exception)
            {
                // İndeks okunamazsa veya bozuksa
                return (null, null);
            }
        }

        #region Helpers
        // ... (GetIndexState, ScanFileSystemRecursiveAsync, ScanItem, CreateDocumentAsync, ProcessItemAsync metotları aynı kalıyor) ...

        private Dictionary<string, (long Ticks, long Size)> GetIndexState(string indexPath)
        {
            var indexState = new Dictionary<string, (long Ticks, long Size)>();
            try
            {
                if (!DirectoryReader.IndexExists(FSDirectory.Open(indexPath))) return indexState;
                using var reader = DirectoryReader.Open(FSDirectory.Open(indexPath));
                for (int i = 0; i < reader.MaxDoc; i++)
                {
                    var doc = reader.Document(i);
                    if (doc == null) continue;
                    var path = doc.Get("path_exact");
                    var dateString = doc.Get("modified_date");
                    var sizeString = doc.Get("size");
                    if (path != null && dateString != null && sizeString != null)
                    {
                        if (long.TryParse(dateString, out var ticks) && long.TryParse(sizeString, out var size))
                        {
                            if (!indexState.ContainsKey(path))
                                indexState.Add(path, (ticks, size));
                        }
                    }
                }
            }
            catch (Exception) { /* ignore */ }
            return indexState;
        }

        private async Task ScanFileSystemRecursiveAsync(string path, Dictionary<string, (long Ticks, long Size)> systemFiles, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(path))
            {
                ScanItem(path, systemFiles, token);
                return;
            }
            if (!Directory.Exists(path)) return;
            try
            {
                foreach (var filePath in Directory.GetFiles(path))
                {
                    token.ThrowIfCancellationRequested();
                    ScanItem(filePath, systemFiles, token);
                }
                foreach (var dir in Directory.GetDirectories(path))
                {
                    token.ThrowIfCancellationRequested();
                    await ScanFileSystemRecursiveAsync(dir, systemFiles, token).ConfigureAwait(false);
                }
            }
            catch (UnauthorizedAccessException) { /* skip */ }
        }

        private void ScanItem(string path, Dictionary<string, (long Ticks, long Size)> systemFiles, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !(_extHashSet?.Contains(ext) ?? false)) return;
            var fi = new FileInfo(path);
            systemFiles[path] = (fi.LastWriteTimeUtc.Ticks, fi.Length);
            if (_archiveExtensions.Contains(ext))
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    using var archive = ArchiveFactory.Open(path);
                    foreach (var entry in archive.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entry.IsDirectory || entry.Key == null) continue;
                        var entryExt = Path.GetExtension(entry.Key).ToLowerInvariant();
                        if (_extHashSet?.Contains(entryExt) ?? false)
                        {
                            var entryPath = $"{path}|{entry.Key}";
                            systemFiles[entryPath] = (entry.LastModifiedTime?.Ticks ?? fi.LastWriteTimeUtc.Ticks, entry.Size);
                        }
                    }
                }
                catch (Exception) { /* skip broken archives */ }
            }
        }

        private async Task<Document?> CreateDocumentAsync(string path, string ext, Func<DateTime> lastModifiedFunc, long size, OcrQuality ocrQuality, CancellationToken token,
            IProgress<ProgressReportModel>? progress, int currentCount, int totalCount)
        {
            token.ThrowIfCancellationRequested();
            string content;
            if (path.Contains("|"))
            {
                var parts = path.Split('|', 2);
                try
                {
                    token.ThrowIfCancellationRequested();
                    using var archive = ArchiveFactory.Open(parts[0]);
                    var entry = archive.Entries.FirstOrDefault(e => e.Key != null && e.Key.Replace("\\", "/") == parts[1].Replace("\\", "/"));
                    if (entry == null) return null;
                    await using var stream = entry.OpenEntryStream();
                    content = await FileProcessor.ExtractTextFromStreamAsync(stream, ext, entry.Key!, ocrQuality,
                        progress, currentCount, totalCount).ConfigureAwait(false);
                }
                catch (Exception) { return null; }
            }
            else
            {
                content = await FileProcessor.ExtractTextAsync(path, ocrQuality,
                    progress, currentCount, totalCount).ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(content)) content = "";
            var ticks = lastModifiedFunc.Invoke().Ticks;
            var doc = new Document
            {
                new TextField("path", path, Field.Store.NO),
                new StringField("path_exact", path, Field.Store.YES),
                new TextField("filename", Path.GetFileName(path), Field.Store.YES),
                new StringField("extension", ext, Field.Store.YES),
                new TextField("content", content, Field.Store.YES),
                new StringField("modified_date", ticks.ToString(), Field.Store.YES),
                new StringField("size", size.ToString(), Field.Store.YES)
            };
            return doc;
        }

        private async Task ProcessItemAsync(string path, string ext, Func<DateTime> lastModifiedFunc, long size, OcrQuality ocrQuality, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var doc = await CreateDocumentAsync(path, ext, lastModifiedFunc, size, ocrQuality, token,
                _progress, _fileCount + 1, _totalCount).ConfigureAwait(false);
            if (doc != null)
            {
                _writer!.AddDocument(doc);
            }
            var count = Interlocked.Increment(ref _fileCount);
            _progress?.Report(new ProgressReportModel { Message = "İndeksleniyor...", CurrentFile = path, Current = count, Total = _totalCount });
        }
        // Bu sınıf, hem sonuçları hem de vurgulama için gereken
        // Lucene Query nesnesini bir arada tutar.
        public class LuceneSearchResponse
        {
            public List<SearchResult> Results { get; set; } = new List<SearchResult>();
            public Query? LuceneQuery { get; set; }
        }
        #endregion
    }
}