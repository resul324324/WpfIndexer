using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WpfIndexer.Models;
using static WpfIndexer.Services.LuceneIndexService;

namespace WpfIndexer.Services
{
    public interface IIndexService
    {
        Task<int> IndexDirectoryAsync(
            string sourcePath,
            string indexPath,
            IEnumerable<string> extensionsToInclude,
            IProgress<ProgressReportModel> progress,
            CancellationToken token,
            OcrQuality ocrQuality);

        Task<UpdateResult> UpdateIndexAsync(
            string sourcePath,
            string indexPath,
            IEnumerable<string> extensionsToInclude,
            IProgress<ProgressReportModel> progress,
            CancellationToken token,
            OcrQuality ocrQuality);

        // DİKKAT: Dönüş tipi List<SearchResult> -> LuceneSearchResponse olarak değişti
        Task<LuceneSearchResponse> SearchAsync(
            string query,
            IEnumerable<string> indexPaths,
            int maxResults);

        Task<string> GetContentByPathAsync(string indexPath, string filePath);

        // YENİ METOT: İndeksten tarihleri okumak için
        StoredIndexMetadata? GetIndexMetadata(string indexPath);
    }
}