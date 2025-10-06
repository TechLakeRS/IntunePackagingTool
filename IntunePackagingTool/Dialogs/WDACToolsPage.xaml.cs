using IntunePackagingTool.Configuration;
using IntunePackagingTool.Dialogs;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using IntunePackagingTool.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace IntunePackagingTool
{
    public partial class WDACToolsPage : UserControl
    {
        private readonly ObservableCollection<CatalogTask> _catalogTasks;
        private readonly HyperVCatalogService _hyperVService;
        private readonly SemaphoreSlim _queueSemaphore;
        private bool _isProcessorRunning;

        public ObservableCollection<CatalogTask> CatalogTasks => _catalogTasks;

        public WDACToolsPage()
        {
            InitializeComponent();
            _catalogTasks = new ObservableCollection<CatalogTask>();
            _hyperVService = new HyperVCatalogService();
            _hyperVService.StatusChanged += HyperVService_StatusChanged;
            _hyperVService.DetailChanged += HyperVService_DetailChanged;
            _queueSemaphore = new SemaphoreSlim(1, 1);
            _isProcessorRunning = false;

            DataContext = this;
            CatalogQueueGrid.ItemsSource = _catalogTasks;

            // Start the background queue processor
            _catalogTasks.CollectionChanged += (s, e) => UpdateStatusBar();
            _ = QueueProcessor();
        }
        private void UpdateStatusBar()
        {
            Dispatcher.Invoke(() =>
            {
                TotalTasksText.Text = _catalogTasks.Count.ToString();
                QueuedTasksText.Text = _catalogTasks.Count(t => t.Status == "Queued").ToString();
                RunningTasksText.Text = _catalogTasks.Count(t => t.Status == "Running").ToString();
            });
        }
        #region Queue Processor

        private async Task QueueProcessor()
        {
            _isProcessorRunning = true;

            while (_isProcessorRunning)
            {
                try
                {
                    await Task.Delay(1000); // Check every second

                    // Find next queued task
                    var nextTask = _catalogTasks.FirstOrDefault(t => t.Status == "Queued");

                    if (nextTask != null)
                    {
                        await ProcessTask(nextTask);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Queue processor error: {ex.Message}");
                }
            }
        }

        private async Task ProcessTask(CatalogTask task)
        {
            await _queueSemaphore.WaitAsync();

            try
            {
                await ProcessHyperVTask(task);
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }

        private async Task ProcessHyperVTask(CatalogTask task)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                task.Status = "Running";
                HyperVStatusPanel.Visibility = Visibility.Visible;
                UpdateStatusBar();
            });

            try
            {
                var result = await _hyperVService.GenerateCatalogAsync(
                    task.HyperVHost,
                    task.VMName,
                    task.SnapshotName,
                    task.PackagePath);

                await Dispatcher.InvokeAsync(() =>
                {
                    if (result.Success)
                    {
                        task.Status = "Complete";
                        task.CatalogPath = result.CatalogFilePath;
                        task.Hash = result.CatalogHash;

                        MessageBox.Show(
                            $"Catalog generated for {task.AppName}!\n\n" +
                            $"File: {Path.GetFileName(result.CatalogFilePath)}\n" +
                            $"Hash: {result.CatalogHash}",
                            "Success",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        task.Status = $"Failed: {result.ErrorMessage}";
                        MessageBox.Show(
                            $"Failed to generate catalog for {task.AppName}:\n\n{result.ErrorMessage}",
                            "Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }

                    ViewHyperVLogButton.IsEnabled = true;
                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    task.Status = $"Error: {ex.Message}";
                    UpdateStatusBar();
                });
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    HyperVStatusPanel.Visibility = Visibility.Collapsed;
                });
            }
        }

        #endregion

        #region Event Handlers

        private void HyperVService_StatusChanged(object sender, string status)
        {
            Dispatcher.Invoke(() =>
            {
                HyperVStatusText.Text = status;
            });
        }

        private void HyperVService_DetailChanged(object sender, string detail)
        {
            Dispatcher.Invoke(() =>
            {
                HyperVStatusDetail.Text = detail;
            });
        }

        private void BrowseHyperVApp_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Deploy-Application.ps1",
                Filter = "PowerShell Scripts (*.ps1)|*.ps1|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFile = openFileDialog.FileName;

                if (!Path.GetFileName(selectedFile).Equals("Deploy-Application.ps1", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Please select Deploy-Application.ps1",
                        "Invalid File", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var scriptDirectory = Path.GetDirectoryName(selectedFile);
                string applicationPath;

                if (Path.GetFileName(scriptDirectory).Equals("Application", StringComparison.OrdinalIgnoreCase))
                {
                    applicationPath = Directory.GetParent(scriptDirectory)?.FullName ?? scriptDirectory;
                }
                else
                {
                    applicationPath = scriptDirectory;
                }

                HyperVAppPathTextBox.Text = applicationPath;
                GenerateHyperVCatalogButton.IsEnabled = true;

                MessageBox.Show($"Application path set to:\n{applicationPath}",
                    "Path Configured", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        private void VerifyCatalogs_Click2(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Catalog Files to Verify",
                Filter = "Catalog Files (*.cat)|*.cat|All Files (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\CatTS"
            };

            // Fallback if network path not accessible
            if (!Directory.Exists(dialog.InitialDirectory))
            {
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            if (dialog.ShowDialog() == true)
            {
                var catalogPaths = dialog.FileNames.ToList();

                if (catalogPaths.Count == 0)
                {
                    MessageBox.Show("No catalog files selected.",
                        "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var verificationWindow = new CatalogVerificationWindow(catalogPaths);
                verificationWindow.Owner = Window.GetWindow(this);
                verificationWindow.ShowDialog();
            }
        }
        private void VerifyCatalogs_Click(object sender, RoutedEventArgs e)
        {
            var cdfViewer = new CdfViewerWindow();
            cdfViewer.ShowDialog();
        }

        private void GenerateHyperVCatalog_Click(object sender, RoutedEventArgs e)
        {
           
            var appPath = HyperVAppPathTextBox.Text.Trim();

            if (string.IsNullOrEmpty(appPath))
            {
                MessageBox.Show("Please select an application path.", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var appInfo = ApplicationHelper.ParseApplicationInfo(appPath);

            var task = new CatalogTask
            {
                AppName = appInfo.Name,
                Version = appInfo.Version,
                PackagePath = appPath,
                HyperVHost = Paths.WDACHyperV.Host,
                VMName = Paths.WDACHyperV.VMName,
                SnapshotName = Paths.WDACHyperV.SnapshotName,
                Status = "Queued",
                IsSelected = true
            };

            _catalogTasks.Add(task);

            var queuePosition = _catalogTasks.Count(t => t.Status == "Queued");
            MessageBox.Show(
                $"Task added to queue:\n\n{appInfo.Name} {appInfo.Version}\n\n" +
                $"Position in queue: {queuePosition}",
                "Task Queued",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void ViewHyperVLog_Click(object sender, RoutedEventArgs e)
        {
            var log = _hyperVService.GetLog();

            if (string.IsNullOrEmpty(log))
            {
                MessageBox.Show("No log available.", "No Log",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var logWindow = new Window
            {
                Title = "Hyper-V Catalog Generation Log",
                Width = 800,
                Height = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this)
            };

            var textBox = new TextBox
            {
                Text = log,
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(10)
            };

            logWindow.Content = textBox;
            logWindow.ShowDialog();
        }

        #endregion

        #region Context Menu

        private void CatalogQueueGrid_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var menu = new ContextMenu();

            var removeItem = new MenuItem { Header = "Remove Selected" };
            removeItem.Click += (s, args) =>
            {
                var selected = _catalogTasks.Where(t => t.IsSelected && t.Status == "Queued").ToList();
                foreach (var task in selected)
                    _catalogTasks.Remove(task);
            };
            menu.Items.Add(removeItem);

            menu.Items.Add(new Separator());

            var clearCompletedItem = new MenuItem { Header = "Clear Completed" };
            clearCompletedItem.Click += (s, args) =>
            {
                var completed = _catalogTasks
                    .Where(t => t.Status.Contains("Complete") || t.Status.Contains("Failed") || t.Status.Contains("Error"))
                    .ToList();
                foreach (var task in completed)
                    _catalogTasks.Remove(task);
            };
            menu.Items.Add(clearCompletedItem);

            menu.Items.Add(new Separator());

            var exportItem = new MenuItem { Header = "Export Results to CSV" };
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
                    csv.AppendLine("AppName,Version,Status,CatalogPath,Hash,HyperVHost,VMName");

                    foreach (var task in _catalogTasks)
                    {
                        csv.AppendLine($"\"{task.AppName}\",\"{task.Version}\",\"{task.Status}\",\"{task.CatalogPath}\",\"{task.Hash}\",\"{task.HyperVHost}\",\"{task.VMName}\"");
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