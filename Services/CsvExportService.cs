using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WpfIndexer.Models;

namespace WpfIndexer.Services
{
    public class CsvExportService
    {
        public string ExportToCsv(IEnumerable<SearchResult> results)
        {
            var sb = new StringBuilder();

            // Başlık satırı
            sb.AppendLine("DosyaAdı;Tür;Boyut(Bytes);İndeksAdı;Yol");

            // Veri satırları
            foreach (var r in results)
            {
                sb.AppendLine(
                    $"{EscapeCsvField(r.FileName)};" +
                    $"{EscapeCsvField(r.Extension)};" +
                    $"{r.Size};" +
                    $"{EscapeCsvField(r.IndexName)};" +
                    $"{EscapeCsvField(r.Path)}"
                );
            }

            return sb.ToString();
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";

            // " karakterlerini çift tırnakla kaç
            string escaped = field.Replace("\"", "\"\"");

            // Eğer alan ; veya " içeriyorsa veya satır başı/sonu boşlukları varsa
            if (escaped.Contains(';') || escaped.Contains('\"') || escaped.Trim() != escaped)
            {
                return $"\"{escaped}\"";
            }
            return escaped;
        }
    }
}