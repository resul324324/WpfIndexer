using System.ComponentModel;
using System.Runtime.CompilerServices;
using System;

namespace WpfIndexer.Models
{
    public class FileTypeModel : INotifyPropertyChanged
    {
        public string Extension { get; set; } = string.Empty;
        private bool _isSelected;
        private Action? _onIsSelectedChanged; // Ebeveyn grubu güncellemek için

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
                _onIsSelectedChanged?.Invoke(); // Ebeveyne haber ver
            }
        }

        public void SetOnSelectedChanged(Action onIsSelectedChanged)
        {
            _onIsSelectedChanged = onIsSelectedChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}