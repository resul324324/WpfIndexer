using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Documents;





namespace WpfIndexer.Models
{
    public class SearchResult : INotifyPropertyChanged
    {
        // --- Zorunlu Özellikler ---
        public required string Path { get; set; }
        public required string FileName { get; set; }
        public required string Extension { get; set; }
        public required string IndexName { get; set; }

        // --- Opsiyonel / Dolan Özellikler ---
        public string Snippet { get; set; } = "";
        public long Size { get; set; }

        /// <summary>
        /// Dosyanın klasör yolu
        /// </summary>
        public string DirectoryPath { get; set; } = string.Empty;

        /// <summary>
        /// İnsan tarafından görülen dosya türü (Metin Belgesi vb.)
        /// </summary>
        public string FileType { get; set; } = string.Empty;

        /// <summary>
        /// Dosya simgesi
        /// </summary>
        [Browsable(false)]
        public ImageSource? FileIcon { get; set; }
        


        /// <summary>
        /// Son değiştirilme tarihi
        /// </summary>
        public DateTime ModificationDate { get; set; }

        // --- Thumbnail & Image Detection ---

        public bool IsImage =>
            Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
            Extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);

        private ImageSource? _thumbnail;
        public ImageSource? Thumbnail
        {
            get => _thumbnail;
            set
            {
                _thumbnail = value;
                OnPropertyChanged();
            }
        }

        // --- INotifyPropertyChanged ---

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

}
