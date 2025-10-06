using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IntunePackagingTool.Models
{
    public class CatalogTask : INotifyPropertyChanged
    {
        private string _status = "Queued";
        private bool _isSelected = false;
        private string _appName = "";
        private string _version = "";
        private string _packagePath = "";
        private string _catalogPath = "";
        private string _hash = "";
        public string TaskType { get; set; } // "HyperV" or "Folder"
        public string HyperVHost { get; set; }
        public string VMName { get; set; }
        public string SnapshotName { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string AppName
        {
            get => _appName;
            set
            {
                _appName = value;
                OnPropertyChanged();
            }
        }

        public string Version
        {
            get => _version;
            set
            {
                _version = value;
                OnPropertyChanged();
            }
        }

        public string PackagePath
        {
            get => _packagePath;
            set
            {
                _packagePath = value;
                OnPropertyChanged();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public string CatalogPath
        {
            get => _catalogPath;
            set
            {
                _catalogPath = value;
                OnPropertyChanged();
            }
        }

        public string Hash
        {
            get => _hash;
            set
            {
                _hash = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}