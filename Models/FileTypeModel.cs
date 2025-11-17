using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WpfIndexer.Models
{
    public class FileTypeModel : INotifyPropertyChanged
    {
        public string Extension { get; set; } = string.Empty;

        private bool _isSelected;
        private Action? _onIsSelectedChanged;

        /// <summary>
        /// Sessiz mod: event fırlatmadan değer günceller.
        /// Bu yöntem toplu seçimlerde UI donmasını engeller.
        /// </summary>
        public void SetIsSelectedSilent(bool value)
        {
            _isSelected = value;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return; // gereksiz event fırlatmayı engelle

                _isSelected = value;
                OnPropertyChanged();

                // Grup modeline haber ver (seçim durumu değişti)
                _onIsSelectedChanged?.Invoke();
            }
        }

        public void SetOnSelectedChanged(Action? onIsSelectedChanged)
        {
            _onIsSelectedChanged = onIsSelectedChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
