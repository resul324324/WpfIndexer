using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WpfIndexer.Models
{
    public class FileTypeGroupModel : INotifyPropertyChanged
    {
        public string GroupName { get; set; } = string.Empty;

        public ObservableCollection<FileTypeModel> FileTypes { get; set; } = new();

        private bool _isUpdating = false;

        // VM'ye "grup seçimi değişti" bilgisini iletmek için
        private Action? _onGroupIsSelectedChanged;

        /// <summary>
        /// Grup seçili mi? En az bir eleman seçiliyse true döner.
        /// </summary>
        public bool IsSelected
        {
            get => FileTypes.Any(f => f.IsSelected);

            set
            {
                if (_isUpdating)
                    return;

                _isUpdating = true;

                // --- SESSİZ MOD (Event fırlatmaz) ---
                foreach (var type in FileTypes)
                {
                    type.SetIsSelectedSilent(value);
                }

                _isUpdating = false;

                // UI sadece 1 defa güncellensin
                OnPropertyChanged();
                OnPropertyChanged(nameof(FileTypes));

                // Parent VM'e haber ver
                _onGroupIsSelectedChanged?.Invoke();
            }
        }

        public FileTypeGroupModel() { }

        /// <summary>
        /// Parent ViewModel, seçim değişimini buradan dinler.
        /// </summary>
        public void SetOnGroupSelectedChanged(Action callback)
        {
            _onGroupIsSelectedChanged = callback;
        }

        /// <summary>
        /// Çocuklardan birinin IsSelected'i değiştiğinde çağrılır.
        /// </summary>
        public void UpdateGroupSelectionState()
        {
            if (_isUpdating)
                return;

            _isUpdating = true;

            // Tüm alt elemanlar seçili ise grup da seçili görünür
            bool newValue = FileTypes.Any(f => f.IsSelected);

            // _isSelected diye extra alan tutmuyoruz
            // direkt IsSelected property'si UI'da binding'i tetikliyor
            OnPropertyChanged(nameof(IsSelected));

            _isUpdating = false;

            // Parent VM'e haber ver
            _onGroupIsSelectedChanged?.Invoke();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
