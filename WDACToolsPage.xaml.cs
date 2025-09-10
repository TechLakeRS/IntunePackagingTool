using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IntunePackagingTool.Services;

namespace IntunePackagingTool
{
    public partial class WDACToolsPage : UserControl
    {
        private ObservableCollection<CatalogTask> _catalogTasks;
        private readonly string _scriptPath = @"\\nbb.local\SYS\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\CreateSecurityCatalog.ps1";
        private bool _isProcessing = false;

        public WDACToolsPage()
        {
            InitializeComponent();
            _catalogTasks = new ObservableCollection<CatalogTask>();
            CatalogQueueGrid.ItemsSource = _catalogTasks;
        }

        // Generate catalog from a single folder
        private async void GenerateFromFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select application folder containing Deploy-Application.exe",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var appFolder = folderDialog.SelectedPath;
                var deployAppPath = Path.Combine(appFolder, "Deploy-Application.exe");

                if (!File.Exists(deployAppPath))
                {
                    MessageBox.Show("Deploy-Application.exe not found in selected folder.",
                        "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Extract app info from folder name or prompt user
                var folderName = Path.GetFileName(appFolder);
                var appInfo = ParseApplicationInfo(folderName);

                var task = new CatalogTask
                {
                    AppName = appInfo.Name,
                    Version = appInfo.Version,
                    PackagePath = appFolder,
                    Status = "Queued",
                    IsSelected = true
                };

                _catalogTasks.Add(task);

                // Auto-start if this is the only task
                if (_catalogTasks.Count == 1 && !_isProcessing)
                {
                    await ProcessSelectedTasks();
                }
            }
        }

        // Batch process multiple packages
        private async void BatchProcess_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select packages list (CSV or TXT)",
                Filter = "List files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var lines = File.ReadAllLines(openFileDialog.FileName);

                    foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                    {
                        // Expected format: "AppName,Version,Path" or just "Path"
                        var parts = line.Split(',');

                        string path = parts.Length == 3 ? parts[2].Trim() : parts[0].Trim();

                        if (Directory.Exists(path))
                        {
                            var task = new CatalogTask
                            {
                                AppName = parts.Length >= 2 ? parts[0].Trim() : Path.GetFileName(path),
                                Version = parts.Length >= 2 ? parts[1].Trim() : "1.0.0",
                                PackagePath = path,
                                Status = "Queued",
                                IsSelected = true
                            };

                            _catalogTasks.Add(task);
                        }
                    }

                    MessageBox.Show($"Added {_catalogTasks.Count} packages to queue.",
                        "Batch Import", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // Validate existing catalogs
        private void ValidateCatalogs_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select catalog files to validate",
                Filter = "Catalog files (*.cat)|*.cat|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var results = new System.Text.StringBuilder();

                foreach (var catFile in openFileDialog.FileNames)
                {
                    try
                    {
                        // Run validation PowerShell command
                        using (var process = new Process())
                        {
                            process.StartInfo = new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-Command \"Test-CiCatalog -CatalogFilePath '{catFile}'\"",
                                RedirectStandardOutput = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };

                            process.Start();
                            var output = process.StandardOutput.ReadToEnd();
                            process.WaitForExit();

                            var status = process.ExitCode == 0 ? "✅ Valid" : "❌ Invalid";
                            results.AppendLine($"{Path.GetFileName(catFile)}: {status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        results.AppendLine($"{Path.GetFileName(catFile)}: ❌ Error - {ex.Message}");
                    }
                }

                MessageBox.Show(results.ToString(), "Catalog Validation Results",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // Process selected tasks
        private async Task ProcessSelectedTasks()
        {
            var selectedTasks = _catalogTasks.Where(t => t.IsSelected && t.Status == "Queued").ToList();

            if (!selectedTasks.Any())
            {
                MessageBox.Show("No tasks selected for processing.", "Nothing to Process",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _isProcessing = true;

            try
            {
                foreach (var task in selectedTasks)
                {
                    task.Status = "Processing...";

                    var result = await GenerateCatalogAsync(task);

                    if (result.Success)
                    {
                        task.Status = "✅ Complete";
                        task.CatalogPath = result.CatalogPath;
                        task.Hash = result.Hash;
                    }
                    else
                    {
                        task.Status = $"❌ Failed: {result.ErrorMessage}";
                    }
                }

                var successful = selectedTasks.Count(t => t.Status.StartsWith("✅"));
                MessageBox.Show($"Processing complete!\n\nSuccessful: {successful}/{selectedTasks.Count}",
                    "Batch Processing Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        // Generate catalog for a single task
        private async Task<CatalogResult> GenerateCatalogAsync(CatalogTask task)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-ExecutionPolicy Bypass -File \"{_scriptPath}\" " +
                                      $"-ApplicationPath \"{task.PackagePath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        // Parse output to get catalog path
                        var output = process.StandardOutput.ReadToEnd();
                        // Extract catalog path from output...

                        return new CatalogResult
                        {
                            Success = true,
                            CatalogPath = ExtractCatalogPath(output),
                            Hash = ExtractHash(output)
                        };
                    }
                    else
                    {
                        return new CatalogResult
                        {
                            Success = false,
                            ErrorMessage = process.StandardError.ReadToEnd()
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new CatalogResult
                    {
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
            });
        }

        // Helper methods
        private ApplicationInfo ParseApplicationInfo(string folderName)
        {
            // Try to parse "Manufacturer_AppName_Version" format
            var parts = folderName.Split('_');

            return new ApplicationInfo
            {
                Manufacturer = parts.Length > 0 ? parts[0] : "Unknown",
                Name = parts.Length > 1 ? parts[1] : folderName,
                Version = parts.Length > 2 ? parts[2] : "1.0.0"
            };
        }

        private string ExtractCatalogPath(string output)
        {
            // Parse the PowerShell output to find the catalog path
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(".cat"))
                {
                    // Extract path from line
                    return line.Trim();
                }
            }
            return "";
        }

        private string ExtractHash(string output)
        {
            // Parse the PowerShell output to find the hash
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Hash:"))
                {
                    return line.Split(':')[1].Trim();
                }
            }
            return "";
        }

        // Context menu for grid
        private void CatalogQueueGrid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();

            var processItem = new MenuItem { Header = "Process Selected" };
            processItem.Click += async (s, args) => await ProcessSelectedTasks();
            menu.Items.Add(processItem);

            var removeItem = new MenuItem { Header = "Remove Selected" };
            removeItem.Click += (s, args) =>
            {
                var selected = _catalogTasks.Where(t => t.IsSelected).ToList();
                foreach (var task in selected)
                    _catalogTasks.Remove(task);
            };
            menu.Items.Add(removeItem);

            menu.IsOpen = true;
        }
    }

    // Data model for catalog tasks
    public class CatalogTask : INotifyPropertyChanged
    {
        private string _status = "Queued";
        private bool _isSelected = false;

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string AppName { get; set; }
        public string Version { get; set; }
        public string PackagePath { get; set; }

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string CatalogPath { get; set; }
        public string Hash { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}