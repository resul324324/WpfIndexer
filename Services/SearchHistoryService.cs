using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace WpfIndexer.Services
{
    public class SearchHistoryService
    {
        private readonly string _dbPath;
        private readonly ILogger<SearchHistoryService> _logger;

        private const int MaxHistoryCount = 1000;   // Veritabanında tutulacak EN FAZLA kayıt
        private const int SuggestionLimit = 10;      // Öneri listesinde gösterilecek en fazla kayıt

        public SearchHistoryService(ILogger<SearchHistoryService> logger)
        {
            _logger = logger;
            _dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "search_history.db");
        }

        // -------------------------------------------------------------
        // VERİTABANI OLUŞTURMA
        // -------------------------------------------------------------
        public async Task InitializeDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                string tableSql =
                @"CREATE TABLE IF NOT EXISTS SearchHistory (
                      Id INTEGER PRIMARY KEY AUTOINCREMENT,
                      Term TEXT NOT NULL,
                      CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                  );";

                using var cmd = new SqliteCommand(tableSql, connection);
                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("SearchHistoryService: Veritabanı hazır.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchHistoryService: Veritabanı oluşturulurken hata.");
            }
        }

        // -------------------------------------------------------------
        // KAYIT EKLEME (TOP-N KONTROLÜ İLE)
        // -------------------------------------------------------------
        public async Task AddSearchTermAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return;

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                // Önce ekle
                string insertSql = @"INSERT INTO SearchHistory (Term) VALUES (@term);";
                using (var cmd = new SqliteCommand(insertSql, connection))
                {
                    cmd.Parameters.AddWithValue("@term", term);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Şimdi TOP-N'yi koru
                string countSql = "SELECT COUNT(*) FROM SearchHistory;";
                using (var countCmd = new SqliteCommand(countSql, connection))
                {
                    long count = (long)await countCmd.ExecuteScalarAsync();
                    if (count > MaxHistoryCount)
                    {
                        long deleteCount = count - MaxHistoryCount;
                        string deleteSql =
                            @"DELETE FROM SearchHistory
                              WHERE Id IN (SELECT Id FROM SearchHistory ORDER BY CreatedAt ASC LIMIT @n);";

                        using var deleteCmd = new SqliteCommand(deleteSql, connection);
                        deleteCmd.Parameters.AddWithValue("@n", deleteCount);
                        await deleteCmd.ExecuteNonQueryAsync();

                        _logger.LogInformation("SearchHistoryService: Eski kayıtlar silindi ({Count}).", deleteCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchHistoryService.AddSearchTermAsync: Hata oluştu.");
            }
        }

        // -------------------------------------------------------------
        // ARAMA ÖNERİLERİ (LIKE sorgusu + LIMIT)
        // -------------------------------------------------------------
        public async Task<List<string>> GetSuggestionsAsync(string query, int limit = SuggestionLimit)
        {
            var list = new List<string>();

            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                string sql =
                    @"SELECT DISTINCT Term
                      FROM SearchHistory
                      WHERE Term LIKE @q
                      ORDER BY CreatedAt DESC
                      LIMIT @limit;";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@q", $"{query}%");
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    list.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchHistoryService.GetSuggestionsAsync: Hata oluştu.");
            }

            return list;
        }

        // -------------------------------------------------------------
        // TÜM GEÇMİŞİ TEMİZLEME
        // -------------------------------------------------------------
        public async Task ClearHistoryAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();

                using var cmd = new SqliteCommand("DELETE FROM SearchHistory;", connection);
                await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("SearchHistoryService: Tüm geçmiş silindi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchHistoryService.ClearHistoryAsync: Hata oluştu.");
            }
        }
    }
}
