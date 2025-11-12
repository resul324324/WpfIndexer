using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WpfIndexer.Models;
using WpfIndexer.Services;

namespace WpfIndexer.ViewModels
{
    public class SelectFileTypesViewModel : INotifyPropertyChanged
    {
        // YENİ: Önerilen grup için ayrı bir özellik
        public FileTypeGroupModel RecommendedGroup { get; }

        // GÜNCELLENDİ: Bu liste artık sadece diğer grupları içeriyor
        public ObservableCollection<FileTypeGroupModel> Groups { get; } = new();

        private bool _selectAll;
        private bool _isUpdatingSelectAll = false;

        // GÜNCELLENDİ: Bu özellik artık sadece 'Groups' koleksiyonunu kontrol ediyor
        public bool SelectAll
        {
            get => _selectAll;
            set
            {
                if (_isUpdatingSelectAll) return;
                _selectAll = value;

                _isUpdatingSelectAll = true;
                // Sadece 'Groups' koleksiyonunu güncelle
                foreach (var group in Groups)
                {
                    group.IsSelected = value;
                }
                _isUpdatingSelectAll = false;

                OnPropertyChanged();
            }
        }


        public SelectFileTypesViewModel()
        {
            // 1. Önerilen Grubu Yükle
            var recGroupEntry = FileProcessor.RecommendedGroup.First();
            RecommendedGroup = new FileTypeGroupModel
            {
                GroupName = recGroupEntry.Key,
                IsSelected = true // Varsayılan olarak seçili gelsin
            };

            foreach (var ext in recGroupEntry.Value)
            {
                var fileTypeModel = new FileTypeModel
                {
                    Extension = ext,
                    IsSelected = true // Varsayılan olarak seçili gelsin
                };
                // Geri arama: Çocuk değişirse Ebeveyni (RecommendedGroup) güncellet
                fileTypeModel.SetOnSelectedChanged(RecommendedGroup.UpdateGroupSelectionState);
                RecommendedGroup.FileTypes.Add(fileTypeModel);
            }
            // Geri arama: RecommendedGroup değişirse, kendi iç tutarlılığını korusun
            // (Tümünü Seç'i etkilemez)
            RecommendedGroup.SetOnGroupSelectedChanged(() => { });


            // 2. Diğer Grupları Yükle
            foreach (var group in FileProcessor.FileTypeGroups)
            {
                var groupModel = new FileTypeGroupModel
                {
                    GroupName = group.Key
                    // IsSelected varsayılan olarak false
                };

                foreach (var ext in group.Value)
                {
                    var fileTypeModel = new FileTypeModel { Extension = ext };
                    // Geri arama: Çocuk değişirse Ebeveyni (groupModel) güncellet
                    fileTypeModel.SetOnSelectedChanged(groupModel.UpdateGroupSelectionState);
                    groupModel.FileTypes.Add(fileTypeModel);
                }

                // Geri arama: Bu gruplardan biri değişirse "Tümünü Seç" kutusunu güncelle
                groupModel.SetOnGroupSelectedChanged(UpdateSelectAllState);

                Groups.Add(groupModel);
            }

            // Başlangıç durumunu ayarla (false olacak)
            UpdateSelectAllState();
        }

        /// <summary>
        /// GÜNCELLENDİ: Sadece 'Groups' koleksiyonunu kontrol eder.
        /// </summary>
        private void UpdateSelectAllState()
        {
            if (_isUpdatingSelectAll) return;

            _isUpdatingSelectAll = true;

            _selectAll = Groups.Any() && Groups.All(g => g.IsSelected);

            OnPropertyChanged(nameof(SelectAll));
            _isUpdatingSelectAll = false;
        }

        /// <summary>
        /// GÜNCELLENDİ: Hem Önerilen gruptan hem de diğer gruplardan seçimleri toplar.
        /// </summary>
        public List<string> GetSelectedExtensions()
        {
            // 1. Önerilen gruptan seçilileri al
            var recommendedExtensions = RecommendedGroup.FileTypes
                .Where(f => f.IsSelected)
                .Select(f => f.Extension);

            // 2. Diğer gruplardan seçilileri al
            var otherExtensions = Groups
                .SelectMany(g => g.FileTypes)
                .Where(f => f.IsSelected)
                .Select(f => f.Extension);

            // İkisini birleştirip tek liste yap (çakışma olmamalı ama Distinct() garantidir)
            return recommendedExtensions.Concat(otherExtensions).Distinct().ToList();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}