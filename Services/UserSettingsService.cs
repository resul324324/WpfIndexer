using Newtonsoft.Json;
using System;
using System.IO;
using WpfIndexer.Models;

// DÜZELTME: Bu satır, iç içe namespace hatasından kaynaklanıyordu. Kaldırıldı.
// using WpfIndexer.Models.WpfIndexer.Models;

namespace WpfIndexer.Services
{
    public class UserSettingsService
    {
        private readonly string _settingsFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "settings.json");

        private UserSettings? _settings;

        public UserSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    _settings = LoadSettings();
                }
                return _settings;
            }
        }

        public UserSettingsService()
        {
            // Servis ilk istendiğinde ayarları yükle
            _settings = LoadSettings();
        }

        private UserSettings LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new UserSettings(); // Varsayılan ayarları döndür
            }

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                return JsonConvert.DeserializeObject<UserSettings>(json)
                       ?? new UserSettings();
            }
            catch (Exception)
            {
                // Hata durumunda (örn: bozuk JSON) varsayılan ayarları döndür
                return new UserSettings();
            }
        }

        public void SaveSettings()
        {
            if (_settings == null) return;

            try
            {
                var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                // Normalde bu hatayı loglamalıyız (bir sonraki adımda yapacağız)
                Console.WriteLine($"Ayarlar kaydedilemedi: {ex.Message}");
            }
        }
    }
}