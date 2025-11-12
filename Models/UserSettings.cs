using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfIndexer.Models
{
    public enum AppTheme
    {
        Light,
        Dark,
        SystemDefault
    }

    public class UserSettings : INotifyPropertyChanged
    {

        private AppTheme _theme = AppTheme.Light;
        public AppTheme Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); }
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


        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}