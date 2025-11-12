using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace WpfIndexer.Services
{
    public class SearchHistoryService
    {
        private readonly string _dbPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "search_history.db");

        private readonly ILogger<SearchHistoryService> _logger;

        // YENİ: Kullanıcının istediği 500 kayıt limiti
        private const int MaxHistoryRecords = 500;

        public SearchHistoryService(ILogger<SearchHistoryService> logger)
        {
            _logger = logger;
        }

        private SqliteConnection GetConnection() => new SqliteConnection($"Data Source={_dbPath}");

        public async Task InitializeDatabaseAsync()
        {
            // ... (Bu metot aynı kalıyor) ...
            try
            {
                using var connection = GetConnection();
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS SearchHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Term TEXT NOT NULL UNIQUE,
                        SearchCount INTEGER NOT NULL DEFAULT 1,
                        LastSearchDate TEXT NOT NULL
                    );
                ";
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SearchHistory veritabanı tablosu oluşturulamadı.");
            }
        }

        public async Task AddSearchTermAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return;

            term = term.Trim();

            try
            {
                using var connection = GetConnection();
                await connection.OpenAsync();

                var insertCommand = connection.CreateCommand();
                insertCommand.CommandText = @"
                    INSERT INTO SearchHistory (Term, LastSearchDate)
                    VALUES ($term, $date);
                ";
                insertCommand.Parameters.AddWithValue("$term", term);
                insertCommand.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("o"));

                try
                {
                    await insertCommand.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // UNIQUE constraint violation
                {
                    var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = @"
                        UPDATE SearchHistory
                        SET SearchCount = SearchCount + 1, LastSearchDate = $date
                        WHERE Term = $term;
                    ";
                    updateCommand.Parameters.AddWithValue("$term", term);
                    updateCommand.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("o"));
                    await updateCommand.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arama terimi eklenirken/güncellenirken hata oluştu: {Term}", term);
            }
            finally
            {
                // YENİ: Ekleme yaptıktan sonra veritabanını 500 kayıtla sınırla
                await PruneHistoryAsync();
            }
        }

        public async Task<List<string>> GetSuggestionsAsync(string partialTerm, int limit = 10)
        {
            // ... (Bu metot aynı kalıyor) ...
            var suggestions = new List<string>();
            if (string.IsNullOrWhiteSpace(partialTerm)) return suggestions;

            try
            {
                using var connection = GetConnection();
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = @"
                    SELECT Term FROM SearchHistory
                    WHERE Term LIKE $term || '%' 
                    ORDER BY SearchCount DESC, LastSearchDate DESC
                    LIMIT $limit;
                ";
                command.Parameters.AddWithValue("$term", partialTerm.Trim());
                command.Parameters.AddWithValue("$limit", limit);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    suggestions.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arama önerileri alınırken hata oluştu: {PartialTerm}", partialTerm);
            }
            return suggestions;
        }

        public async Task ClearHistoryAsync()
        {
            // ... (Bu metot aynı kalıyor) ...
            try
            {
                using var connection = GetConnection();
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM SearchHistory;";
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arama geçmişi (SQLite) temizlenirken hata oluştu.");
            }
        }

        // YENİ METOT: Veritabanını en son 500 kayıtla sınırlar
        private async Task PruneHistoryAsync(int limit = MaxHistoryRecords)
        {
            try
            {
                using var connection = GetConnection();
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                // Son 500 kaydın ID'si DIŞINDAKİ tüm kayıtları sil
                command.CommandText = $@"
                    DELETE FROM SearchHistory
                    WHERE Id NOT IN (
                        SELECT Id FROM SearchHistory
                        ORDER BY LastSearchDate DESC
                        LIMIT {limit}
                    );
                ";
                int rowsDeleted = await command.ExecuteNonQueryAsync();
                if (rowsDeleted > 0)
                {
                    _logger.LogInformation("{Count} adet eski arama geçmişi kaydı silindi.", rowsDeleted);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Arama geçmişi (SQLite) temizlenirken (Prune) hata oluştu.");
            }
        }
    }
}