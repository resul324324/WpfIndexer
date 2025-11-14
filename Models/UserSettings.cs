using System.ComponentModel;
using System.Runtime.CompilerServices;

// Dosyanın tamamı WpfIndexer.Models isim alanı içinde olmalı
namespace WpfIndexer.Models
{
    public enum AppTheme
    {
        Light,
        Dark,
        SystemDefault
    }
    public enum PreviewMode
    {
        Rapor, // Vurgulamalı düz metin (mevcut sistem)
        Canli  // Gerçek dosya görünümü (yeni sistem)
    }

    // DÜZELTME: Enum'u buraya, ana namespace'e taşıdım.
    // İç içe olan "WpfIndexer.Models.WpfIndexer.Models" hatası düzeltildi.
    public enum PreviewPanePosition
    {
        Right,
        Bottom
    }

    public class UserSettings : INotifyPropertyChanged
    {
        private AppTheme _theme = AppTheme.Light;
        public AppTheme Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); }
        }
        private PreviewMode _previewMode = PreviewMode.Rapor; // Varsayılan Rapor Modu
        public PreviewMode PreviewMode
        {
            get => _previewMode;
            set
            {
                if (_previewMode != value)
                {
                    _previewMode = value;
                    OnPropertyChanged(nameof(PreviewMode));
                }
            }
        }
        private bool _enablePreview = true;
        public bool EnablePreview
        {
            get => _enablePreview;
            set { _enablePreview = value; OnPropertyChanged(); }
        }

        private bool _saveSearchHistory = true;
        public bool SaveSearchHistory
        {
            get => _saveSearchHistory;
            set { _saveSearchHistory = value; OnPropertyChanged(); }
        }
        public bool AutoUpdateEnabled { get; set; } = true;

        private bool _showSearchSuggestions = true;
        public bool ShowSearchSuggestions
        {
            get => _showSearchSuggestions;
            set { _showSearchSuggestions = value; OnPropertyChanged(); }
        }

        private int _defaultSearchResultLimit = 100;
        public int DefaultSearchResultLimit
        {
            get => _defaultSearchResultLimit;
            set { _defaultSearchResultLimit = value; OnPropertyChanged(); }
        }

        private bool _enableVerboseLogging = false;
        public bool EnableVerboseLogging
        {
            get => _enableVerboseLogging;
            set { _enableVerboseLogging = value; OnPropertyChanged(); }
        }

        // YENİ: İstenen yeni özellik buraya eklendi.
        private PreviewPanePosition _previewPosition = PreviewPanePosition.Right;
        public PreviewPanePosition PreviewPosition
        {
            get => _previewPosition;
            set { _previewPosition = value; OnPropertyChanged(); }
        }


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}