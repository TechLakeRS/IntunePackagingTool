using System;
using System.ComponentModel;

namespace IntunePackagingTool
{
    public enum DetectionRuleType
    {
        File,
        Registry,
        MSI 
    }

    public class DetectionRule : INotifyPropertyChanged
    {
        private DetectionRuleType _type;
        private string _path = "";
        private string _fileOrFolderName = "";
        private bool _checkVersion;
        private string _operator = ""; 

        public DetectionRuleType Type
        {
            get => _type;
            set
            {
                _type = value;
                OnPropertyChanged(nameof(Type));
                OnPropertyChanged(nameof(Title)); 
            }
        }

        public string Path
        {
            get => _path;
            set
            {
                _path = value ?? "";
                OnPropertyChanged(nameof(Path));
                OnPropertyChanged(nameof(Title));
            }
        }

        public string FileOrFolderName
        {
            get => _fileOrFolderName;
            set
            {
                _fileOrFolderName = value ?? "";
                OnPropertyChanged(nameof(FileOrFolderName));
                OnPropertyChanged(nameof(Title));
            }
        }

        public bool CheckVersion
        {
            get => _checkVersion;
            set
            {
                _checkVersion = value;
                OnPropertyChanged(nameof(CheckVersion));
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Operator
        {
            get => _operator;
            set
            {
                _operator = value ?? "";
                OnPropertyChanged(nameof(Operator));
                OnPropertyChanged(nameof(Title));
            }
        }

       
        public string Title
        {
            get
            {
                return Type switch
                {
                    DetectionRuleType.File => CheckVersion
                        ? $"File: {System.IO.Path.Combine(Path, FileOrFolderName)} (Check Version)"
                        : $"File: {System.IO.Path.Combine(Path, FileOrFolderName)}",

                    DetectionRuleType.Registry => string.IsNullOrEmpty(FileOrFolderName)
                        ? $"Registry: {Path} (Key Exists)"
                        : $"Registry: {Path}\\{FileOrFolderName}",

                    DetectionRuleType.MSI => CheckVersion
                        ? $"MSI: {Path} (Version {Operator} {FileOrFolderName})"
                        : $"MSI: {Path}",

                    _ => "Unknown Detection Rule"
                };
            }
        }

       
        public string Description
        {
            get
            {
                return Type switch
                {
                    DetectionRuleType.File => CheckVersion
                        ? $"Detects file '{FileOrFolderName}' in path '{Path}' and validates its version"
                        : $"Detects presence of file '{FileOrFolderName}' in path '{Path}'",

                    DetectionRuleType.Registry => string.IsNullOrEmpty(FileOrFolderName)
                        ? $"Checks if registry key '{Path}' exists"
                        : $"Checks registry value '{FileOrFolderName}' in key '{Path}'",

                    DetectionRuleType.MSI => CheckVersion
                        ? $"Detects MSI product '{Path}' with version {Operator} {FileOrFolderName}"
                        : $"Detects presence of MSI product '{Path}'",

                    _ => "Unknown detection rule type"
                };
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}