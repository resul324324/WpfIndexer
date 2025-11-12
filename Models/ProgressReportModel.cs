namespace WpfIndexer.Models
{
    public class ProgressReportModel
    {
        public string Message { get; set; } = string.Empty;

        // YENİ: O anda işlenen dosyanın yolu
        public string CurrentFile { get; set; } = string.Empty;

        public int Current { get; set; } = 0;
        public int Total { get; set; } = 1; // 0'a bölme hatası almamak için 1
        public bool IsIndeterminate { get; set; } = false;
    }
}