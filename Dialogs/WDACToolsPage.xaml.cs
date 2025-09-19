using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;

namespace IntunePackagingTool
{
    public partial class WDACToolsPage : UserControl
    {
        private readonly ObservableCollection<CatalogTask> _catalogTasks;
        private readonly WDACService _wdacService;
        private bool _isProcessing = false;

        public ObservableCollection<CatalogTask> CatalogTasks => _catalogTasks;
        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                _isProcessing = value;
                UpdateUIState();
            }
        }

        public WDACToolsPage()
        {
            InitializeComponent();
            _catalogTasks = new ObservableCollection<CatalogTask>();
            _wdacService = new WDACService();

            DataContext = this;
            CatalogQueueGrid.ItemsSource = _catalogTasks;
        }

        #region Event Handlers

        private async void GenerateFromFolder_Click(object sender, RoutedEventArgs e)
        {
            if (IsProcessing)
            {
                MessageBox.Show("A catalog generation is already in progress.",
                    "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Use the SelectFolderByFile method to select the folder by selecting Deploy-Application.exe
            var appFolder = SelectFolderByFile(
                "Select application folder",
                "Deploy-Application.exe");

            if (string.IsNullOrEmpty(appFolder))
                return;

            // Validate folder structure
            if (!ValidatePackageFolder(appFolder))
                return;

            // Parse application info and create task
            var appInfo = WDACService.ParseApplicationInfo(appFolder);
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
            if (_catalogTasks.Count == 1 && !IsProcessing)
            {
                await ProcessSelectedTasks();
            }
        }

        private async void BatchProcess_Click(object sender, RoutedEventArgs e)
        {
            if (IsProcessing)
            {
                MessageBox.Show("A catalog generation is already in progress.",
                    "Processing", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var openFileDialog = new OpenFileDialog
            {
                Title = "Select packages list (CSV or TXT)",
                Filter = "List files (*.csv;*.txt)|*.csv;*.txt|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            try
            {
                var lines = await File.ReadAllLinesAsync(openFileDialog.FileName);
                var addedCount = 0;

                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                {
                    var task = ParseBatchLine(line);
                    if (task != null && Directory.Exists(task.PackagePath))
                    {
                        _catalogTasks.Add(task);
                        addedCount++;
                    }
                }

                MessageBox.Show($"Added {addedCount} packages to queue.",
                    "Batch Import", MessageBoxButton.OK, MessageBoxImage.Information);

                if (addedCount > 0 && !IsProcessing)
                {
                    var result = MessageBox.Show(
                        "Do you want to start processing the batch now?",
                        "Start Processing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await ProcessSelectedTasks();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}",
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ValidateCatalogs_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select catalog files to validate",
                Filter = "Catalog files (*.cat)|*.cat|All files (*.*)|*.*",
                Multiselect = true
            };

            if (openFileDialog.ShowDialog() != true)
                return;

            var results = new System.Text.StringBuilder();
            results.AppendLine("CATALOG VALIDATION RESULTS");
            results.AppendLine("=" + new string('=', 50));

            foreach (var catFile in openFileDialog.FileNames)
            {
                try
                {
                    var isValid = await _wdacService.ValidateCatalogAsync(catFile);
                    var status = isValid ? "✅ Valid" : "❌ Invalid";
                    results.AppendLine($"{Path.GetFileName(catFile)}: {status}");
                }
                catch (Exception ex)
                {
                    results.AppendLine($"{Path.GetFileName(catFile)}: ❌ Error - {ex.Message}");
                }
            }

            MessageBox.Show(results.ToString(), "Catalog Validation Results",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Processing Methods

        private async Task ProcessSelectedTasks()
        {
            var selectedTasks = _catalogTasks
                .Where(t => t.IsSelected && t.Status == "Queued")
                .ToList();

            if (!selectedTasks.Any())
            {
                MessageBox.Show("No tasks selected for processing.",
                    "Nothing to Process",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            IsProcessing = true;

            try
            {
                var progress = new Progress<WDACService.BatchProgress>(UpdateBatchProgress);
                var results = await _wdacService.GenerateBatchCatalogsAsync(selectedTasks, progress);

                // Update task statuses based on results
                for (int i = 0; i < selectedTasks.Count && i < results.Count; i++)
                {
                    var task = selectedTasks[i];
                    var result = results[i];

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

                ShowProcessingResults(selectedTasks);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during batch processing: {ex.Message}",
                    "Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        

        #endregion

        #region Helper Methods

        private bool ValidatePackageFolder(string folderPath)
        {
            var deployAppPath = Path.Combine(folderPath, "Application", "Deploy-Application.exe");

            if (!File.Exists(deployAppPath))
            {
                // Check root folder as fallback
                deployAppPath = Path.Combine(folderPath, "Deploy-Application.exe");

                if (!File.Exists(deployAppPath))
                {
                    MessageBox.Show(
                        "Deploy-Application.exe not found in the selected folder.\n\n" +
                        "Expected locations:\n" +
                        "• [Folder]\\Application\\Deploy-Application.exe\n" +
                        "• [Folder]\\Deploy-Application.exe",
                        "Invalid Folder",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return false;
                }
            }

            return true;
        }

        private CatalogTask? ParseBatchLine(string line)
        {
            try
            {
                var parts = line.Split(',').Select(p => p.Trim()).ToArray();

                if (parts.Length == 3)
                {
                    // Format: AppName,Version,Path
                    return new CatalogTask
                    {
                        AppName = parts[0],
                        Version = parts[1],
                        PackagePath = parts[2],
                        Status = "Queued",
                        IsSelected = true
                    };
                }
                else if (parts.Length == 1)
                {
                    // Format: Path only
                    var appInfo = WDACService.ParseApplicationInfo(parts[0]);
                    return new CatalogTask
                    {
                        AppName = appInfo.Name,
                        Version = appInfo.Version,
                        PackagePath = parts[0],
                        Status = "Queued",
                        IsSelected = true
                    };
                }
            }
            catch
            {
                // Ignore malformed lines
            }

            return null;
        }

        private void UpdateBatchProgress(WDACService.BatchProgress progress)
        {
            Dispatcher.Invoke(() =>
            {
                // Update UI with batch progress
                // You could add a progress bar or status label here
                var statusMessage = $"Processing {progress.CurrentTask}/{progress.TotalTasks}: {progress.CurrentTaskName}";

                // Find the task and update its status
                var task = _catalogTasks.FirstOrDefault(t => t.AppName == progress.CurrentTaskName);
                if (task != null && !string.IsNullOrEmpty(progress.CurrentMessage))
                {
                    task.Status = progress.CurrentMessage;
                }
            });
        }

        private void ShowProcessingResults(IList<CatalogTask> tasks)
        {
            var successful = tasks.Count(t => t.Status.StartsWith("✅"));
            var failed = tasks.Count(t => t.Status.StartsWith("❌"));

            var message = $"Processing complete!\n\n" +
                         $"✅ Successful: {successful}\n" +
                         $"❌ Failed: {failed}\n" +
                         $"📊 Total: {tasks.Count}";

            MessageBox.Show(message, "Batch Processing Complete",
                MessageBoxButton.OK,
                successful == tasks.Count ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        private void UpdateUIState()
        {
            Dispatcher.Invoke(() =>
            {
                // Update button states based on processing status
                // This could be bound to IsEnabled properties in XAML
            });
        }

        #endregion

        #region Folder Selection Methods

        /// <summary>
        /// Select folder by navigating to a specific file
        /// </summary>
        private string SelectFolderByFile(string description, string targetFileName = "Deploy-Application.exe")
        {
            var dialog = new OpenFileDialog
            {
                Title = $"{description} - Navigate to folder and select {targetFileName}",
                Filter = $"{targetFileName}|{targetFileName}|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                // Get the directory of the selected file
                var dir = Path.GetDirectoryName(dialog.FileName);

                // If they selected Deploy-Application.exe in Application subfolder, go up one level
                if (dir?.EndsWith("\\Application") == true)
                {
                    return Directory.GetParent(dir)?.FullName ?? dir;
                }

                return dir ?? "";
            }

            return "";
        }

        /// <summary>
        /// Alternative: Select any file in a folder to get the folder path
        /// </summary>
        private string SelectFolder(string description)
        {
            var dialog = new OpenFileDialog
            {
                Title = description + " (Select any file in the target folder)",
                Filter = "All files (*.*)|*.*",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Folder Selection"
            };

            if (dialog.ShowDialog() == true)
            {
                return Path.GetDirectoryName(dialog.FileName) ?? "";
            }

            return "";
        }

        /// <summary>
        /// Alternative: Use a textbox input with browse button
        /// </summary>
        private string ShowFolderInputDialog(string title, string message)
        {
            var inputWindow = new Window
            {
                Title = title,
                Width = 500,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var messageBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(messageBlock, 0);
            grid.Children.Add(messageBlock);

            var pathPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Grid.SetRow(pathPanel, 1);

            var pathTextBox = new TextBox
            {
                Width = 380,
                Margin = new Thickness(0, 0, 10, 0)
            };
            pathPanel.Children.Add(pathTextBox);

            var browseButton = new Button
            {
                Content = "Browse...",
                Width = 80
            };
            // Add event handler with += operator
            browseButton.Click += (s, e) =>
            {
                    var folder = SelectFolderByFile("Select application folder", "Deploy-Application.exe");
                    if (!string.IsNullOrEmpty(folder))
                    {
                        pathTextBox.Text = folder;
                    }
                
            };
            pathPanel.Children.Add(browseButton);
            grid.Children.Add(pathPanel);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 20, 0, 0)
            };
            Grid.SetRow(buttonPanel, 3);

            var okButton = new Button
            {
                Content = "OK",
                Width = 80,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true
            };
            okButton.Click += (s, e) =>
            {
                inputWindow.DialogResult = true;
                inputWindow.Close();
            };
            buttonPanel.Children.Add(okButton);

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 80,
                IsCancel = true
            };
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            inputWindow.Content = grid;

            if (inputWindow.ShowDialog() == true)
            {
                return pathTextBox.Text;
            }

            return "";
        }

        #endregion

        #region Context Menu

        private void CatalogQueueGrid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();

            // Process Selected
            var processItem = new MenuItem { Header = "🚀 Process Selected" };
            processItem.Click += async (s, args) => await ProcessSelectedTasks();
            processItem.IsEnabled = !IsProcessing;
            menu.Items.Add(processItem);

            menu.Items.Add(new Separator());

            // Select All
            var selectAllItem = new MenuItem { Header = "✅ Select All" };
            selectAllItem.Click += (s, args) =>
            {
                foreach (var task in _catalogTasks)
                    task.IsSelected = true;
            };
            menu.Items.Add(selectAllItem);

            // Deselect All
            var deselectAllItem = new MenuItem { Header = "⬜ Deselect All" };
            deselectAllItem.Click += (s, args) =>
            {
                foreach (var task in _catalogTasks)
                    task.IsSelected = false;
            };
            menu.Items.Add(deselectAllItem);

            menu.Items.Add(new Separator());

            // Remove Selected
            var removeItem = new MenuItem { Header = "🗑️ Remove Selected" };
            removeItem.Click += (s, args) =>
            {
                var selected = _catalogTasks.Where(t => t.IsSelected).ToList();
                foreach (var task in selected)
                    _catalogTasks.Remove(task);
            };
            menu.Items.Add(removeItem);

            // Clear Completed
            var clearCompletedItem = new MenuItem { Header = "🧹 Clear Completed" };
            clearCompletedItem.Click += (s, args) =>
            {
                var completed = _catalogTasks
                    .Where(t => t.Status.StartsWith("✅") || t.Status.StartsWith("❌"))
                    .ToList();
                foreach (var task in completed)
                    _catalogTasks.Remove(task);
            };
            menu.Items.Add(clearCompletedItem);

            menu.Items.Add(new Separator());

            // Export Results
            var exportItem = new MenuItem { Header = "💾 Export Results to CSV" };
            exportItem.Click += ExportResults_Click;
            menu.Items.Add(exportItem);

            menu.IsOpen = true;
        }

        private void ExportResults_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Export Catalog Results",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"WDAC_Catalogs_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var csv = new System.Text.StringBuilder();
                    csv.AppendLine("AppName,Version,Status,CatalogPath,Hash");

                    foreach (var task in _catalogTasks)
                    {
                        csv.AppendLine($"\"{task.AppName}\",\"{task.Version}\",\"{task.Status}\",\"{task.CatalogPath}\",\"{task.Hash}\"");
                    }

                    File.WriteAllText(saveDialog.FileName, csv.ToString());
                    MessageBox.Show("Results exported successfully!", "Export Complete",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting results: {ex.Message}", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}