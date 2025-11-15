using System;
using System.Collections.Generic;

namespace WpfIndexer.Models
{
    // Bu sınıf, Lucene CommitData'dan okunan ayarları
    // geçici olarak tutmak için kullanılır.
    public class StoredIndexMetadata
    {
        public string? SourcePath { get; set; }
        public OcrQuality OcrQuality { get; set; } = OcrQuality.Off;
        public List<string> Extensions { get; set; } = new();
        public DateTime? CreationDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
    }
}