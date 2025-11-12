namespace WpfIndexer.Models
{
    public class SearchResult
    {
        public required string Path { get; set; }
        public required string FileName { get; set; }
        public required string Extension { get; set; }
        public string Snippet { get; set; } = "";

        // Dosya boyutu
        public long Size { get; set; }

        // YENİ: Hangi indeksten geldiğini belirtir
        public required string IndexName { get; set; }
    }
}