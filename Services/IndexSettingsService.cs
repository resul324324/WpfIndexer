using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using WpfIndexer.Models;

namespace WpfIndexer.Services
{
    /// <summary>
    /// Paylaşılan İndeks listesini (koleksiyonunu) yönetir ve disk ile senkronize eder.
    /// </summary>
    public class IndexSettingsService : INotifyPropertyChanged
    {
        private readonly string _settingsFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "indexes.json");

        private readonly ILogger<IndexSettingsService> _logger;

        public ObservableCollection<IndexDefinition> Indexes { get; } = new();

        public IndexSettingsService(ILogger<IndexSettingsService> logger)
        {
            _logger = logger;
            LoadIndexes();
        }

        private void LoadIndexes()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return; // Boş koleksiyonla başla
            }

            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                var loaded = JsonConvert.DeserializeObject<List<IndexDefinition>>(json) ?? new List<IndexDefinition>();

                Indexes.Clear();
                foreach (var index in loaded)
                {
                    // Yüklendikten sonra PropertyChanged olaylarını dinle
                    index.PropertyChanged += OnIndexPropertyChanged;
                    Indexes.Add(index);
                }
                _logger.LogInformation("{Count} adet kayıtlı indeks yüklendi.", Indexes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "indexes.json dosyası yüklenemedi.");
            }
        }

        public void SaveIndexes()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Indexes, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
                _logger.LogInformation("İndeks ayarları kaydedildi.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "İndeks ayarları (indexes.json) kaydedilemedi.");
            }
        }

        /// <summary>
        /// İndeks koleksiyonuna yeni bir indeks ekler.
        /// </summary>
        public void AddIndex(IndexDefinition newIndex)
        {
            if (Indexes.Any(i => i.IndexPath == newIndex.IndexPath))
            {
                MessageBox.Show("Bu indeks zaten eklenmiş.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            newIndex.PropertyChanged += OnIndexPropertyChanged;
            Indexes.Add(newIndex);
            OnPropertyChanged(nameof(Indexes));
        }

        /// <summary>
        /// Mevcut bir indeks klasörünü doğrular ve ekler.
        /// </summary>
        public void AddExistingIndex(string indexPath)
        {
            if (!Directory.EnumerateFiles(indexPath, "segments.*").Any())
            {
                MessageBox.Show("Bu klasör geçerli bir Lucene indeks klasörü değil. 'segments.*' dosyaları bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (Indexes.Any(i => i.IndexPath == indexPath))
            {
                MessageBox.Show("Bu indeks zaten eklenmiş.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var indexName = new DirectoryInfo(indexPath).Name;
            var newIndex = new IndexDefinition
            {
                Name = indexName,
                IndexPath = indexPath,
                SourcePath = null, // Kaynak yolu bilinmiyor (güncellenemez)
                IsSelected = true
            };

            AddIndex(newIndex);
            _logger.LogInformation("Mevcut indeks eklendi: {IndexName}", indexName);
        }

        /// <summary>
        /// Verilen indeksleri listeden kaldırır.
        /// </summary>
        public void RemoveIndexes(List<IndexDefinition> indexesToDelete)
        {
            foreach (var index in indexesToDelete)
            {
                index.PropertyChanged -= OnIndexPropertyChanged;
                Indexes.Remove(index);
            }
            _logger.LogInformation("{Count} adet indeks listeden kaldırıldı.", indexesToDelete.Count);
            OnPropertyChanged(nameof(Indexes));
        }

        /// <summary>
        /// İndekslerden biri (örn: IsSelected) değiştiğinde tetiklenir.
        /// </summary>
        private void OnIndexPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IndexDefinition.IsSelected))
            {
                // Değişiklik olduğunda kaydetmeye gerek yok,
                // sadece ana VM'in haberdar olmasını sağlayabiliriz.
                // Kaydetme işlemi program kapanırken OnExit'te yapılacak.
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}