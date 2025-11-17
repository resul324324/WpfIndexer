using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows; // YENİ: CS0246 'Window' türü için eklendi
using WpfIndexer.Helpers;
using WpfIndexer.Models;
using WpfIndexer.Services;

namespace WpfIndexer.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly UserSettingsService _userSettingsService;
        private readonly SearchHistoryService _searchHistoryService;
        private readonly ThemeService _themeService;

        public UserSettings Settings { get; }

        public ICommand SaveAndCloseCommand { get; }
        public ICommand ClearSearchHistoryCommand { get; }
        public event Action? RequestClose;


        public SettingsViewModel(UserSettingsService userSettingsService, SearchHistoryService searchHistoryService, ThemeService themeService)
        {
            _userSettingsService = userSettingsService;
            _searchHistoryService = searchHistoryService;
            _themeService = themeService;

            Settings = _userSettingsService.Settings;

            SaveAndCloseCommand = new RelayCommand(SaveAndClose);
            ClearSearchHistoryCommand = new RelayCommand(async _ => await ClearSearchHistory());
        }

        public bool AutoUpdateEnabled
        {
            get => Settings.AutoUpdateEnabled;
            set
            {
                if (Settings.AutoUpdateEnabled != value)
                {
                    Settings.AutoUpdateEnabled = value;
                    OnPropertyChanged();
                    // Ayarı hemen kaydet
                    _userSettingsService.SaveSettings();
                }
            }
        }
        private async Task ClearSearchHistory()
        {
            await _searchHistoryService.ClearHistoryAsync();
            // TODO: Kullanıcıya "Geçmiş temizlendi" bildirimi eklenebilir.
        }

        private void SaveAndClose(object? window)
        {
            _themeService.ApplyTheme(Settings.Theme);
            _userSettingsService.SaveSettings();

            // ViewModel View’a "kapan" sinyali gönderir
            RequestClose?.Invoke();

        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}