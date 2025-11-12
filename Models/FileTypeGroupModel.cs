using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System; // YENİ: Action için eklendi

namespace WpfIndexer.Models
{
    public class FileTypeGroupModel : INotifyPropertyChanged
    {
        public string GroupName { get; set; } = string.Empty;
        public ObservableCollection<FileTypeModel> FileTypes { get; set; } = new();

        private bool _isSelected;
        private bool _isUpdating = false; // Sonsuz döngüleri engellemek için

        // YENİ: Ebeveyn (VM) durumu güncellemek için
        private Action? _onGroupIsSelectedChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isUpdating) return;
                _isSelected = value;

                // Ebeveyn (Grup) seçimi, tüm çocuklara yansıtılır
                _isUpdating = true;
                foreach (var type in FileTypes)
                {
                    type.IsSelected = value;
                }
                _isUpdating = false;

                OnPropertyChanged();
                _onGroupIsSelectedChanged?.Invoke(); // YENİ: Ebeveyn VM'e haber ver
            }
        }

        public FileTypeGroupModel()
        {
            // Bu ctor, ViewModel'de doldurulduktan sonra kullanılacak
        }

        // YENİ: Ebeveyn VM'den geri aramayı ayarlamak için metot
        public void SetOnGroupSelectedChanged(Action onGroupIsSelectedChanged)
        {
            _onGroupIsSelectedChanged = onGroupIsSelectedChanged;
        }

        // Çocuklardan biri değiştiğinde bu metot çağrılır
        public void UpdateGroupSelectionState()
        {
            if (_isUpdating) return;

            _isUpdating = true;
            if (FileTypes.All(f => f.IsSelected))
            {
                _isSelected = true;
            }
            else
            {
                _isSelected = false;
            }
            OnPropertyChanged(nameof(IsSelected));
            _isUpdating = false;

            _onGroupIsSelectedChanged?.Invoke(); // YENİ: Ebeveyn VM'e haber ver
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}