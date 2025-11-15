using System;
using System.ComponentModel; // Bu using gerekli
using System.Windows.Media; // Bu using gerekli

namespace WpfIndexer.Models
{
    public class SearchResult
    {
        // --- Mevcut Özelliklerin ---
        public required string Path { get; set; }
        public required string FileName { get; set; }
        public required string Extension { get; set; }
        public string Snippet { get; set; } = "";
        public long Size { get; set; }
        public required string IndexName { get; set; }

        // --- YENİ EKLENEN ÖZELLİKLER ---

        /// <summary>
        /// Dosyanın son değiştirilme tarihi. "Tarih" sütunu için.
        /// </summary>
        public DateTime ModificationDate { get; set; }

        /// <summary>
        /// Sadece dosyanın bulunduğu klasörün yolu. "Yol" sütunu için.
        /// </summary>
        public string DirectoryPath { get; set; }

        /// <summary>
        /// Dosyanın sistemdeki görünen tür adı ("Metin Belgesi" vb.). "Tür" sütunu için.
        /// </summary>
        public string FileType { get; set; } = string.Empty;

        /// <summary>
        /// Dosya simgesi.
        /// </summary>
        [Browsable(false)] // Bu özellik DataGrid'de otomatik sütun olmasın
        public ImageSource? FileIcon { get; set; } // Null olabilir
    }
}