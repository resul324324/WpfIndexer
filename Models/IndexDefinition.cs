using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System; // YENİ

namespace WpfIndexer.Models
{
    public class IndexDefinition : INotifyPropertyChanged
    {
        // ... (Name, IndexPath, SourcePath, OcrQuality, Extensions özellikleri aynı kalıyor) ...

        public string Name { get; set; } = string.Empty;
        public string IndexPath { get; set; } = string.Empty;
        public string? SourcePath { get; set; }
        public OcrQuality OcrQuality { get; set; } = OcrQuality.Off;
        public List<string> Extensions { get; set; } = new();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        // --- YENİ ÖZELLİKLER (Bunlar indexes.json'a kaydedilmez) ---
        // [JsonIgnore] atribütü gerekebilir, ancak SaveIndexes sadece bu modeli
        // temel aldığı için sorun olmayacaktır.

        private DateTime? _creationDate;
        public DateTime? CreationDate
        {
            get => _creationDate;
            set { _creationDate = value; OnPropertyChanged(); }
        }

        private DateTime? _lastUpdateDate;
        public DateTime? LastUpdateDate
        {
            get => _lastUpdateDate;
            set { _lastUpdateDate = value; OnPropertyChanged(); }
        }
        // -----------------------------------------------------------

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}