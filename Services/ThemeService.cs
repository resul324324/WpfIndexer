using System;
using System.Linq;
using System.Windows;
using WpfIndexer.Models;

namespace WpfIndexer.Services
{
    public class ThemeService
    {
        private readonly UserSettingsService _settingsService;

        public ThemeService(UserSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void ApplyTheme()
        {
            ApplyTheme(_settingsService.Settings.Theme);
        }

        public void ApplyTheme(AppTheme theme)
        {
            // TODO: Sistem temasını algılama eklenebilir
            if (theme == AppTheme.SystemDefault)
            {
                theme = AppTheme.Light; // Şimdilik varsayılan
            }

            var dictionaries = Application.Current.Resources.MergedDictionaries;

            // Önceki temayı kaldır
            var oldTheme = dictionaries.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));
            if (oldTheme != null)
            {
                dictionaries.Remove(oldTheme);
            }

            // Yeni temayı ekle
            string themeFile = theme == AppTheme.Dark ? "Resources/DarkTheme.xaml" : "Resources/LightTheme.xaml";
            var newTheme = new ResourceDictionary { Source = new Uri(themeFile, UriKind.Relative) };
            dictionaries.Add(newTheme);

            _settingsService.Settings.Theme = theme;
        }
    }
}