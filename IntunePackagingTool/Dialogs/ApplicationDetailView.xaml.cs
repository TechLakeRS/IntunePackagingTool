using IntunePackagingTool.Dialogs;
using IntunePackagingTool.Models;
using IntunePackagingTool.Utilities;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool
{
    public partial class ApplicationDetailView : UserControl
    {
        public event EventHandler? BackToListRequested;
        private ApplicationDetail? _currentApp;

        public ApplicationDetailView()
        {
            InitializeComponent();

            this.DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ApplicationDetail app && app.IconImage != null)
                {
                    AppIconImage.Source = app.IconImage;
                }
            };
        }

        public async void LoadApplicationDetail(ApplicationDetail app)
        {
            _currentApp = app;
            this.DataContext = app;

            // Update all sections
            UpdateHeader(app);
            UpdateOverview(app);
            UpdateDescription(app);
            UpdateCommands(app);
            UpdateDetectionRules(app);
            UpdateRequirements(app);
            UpdateGroups(app);
            UpdatePublishing(app);
            UpdateUrls(app);

            // Load network path asynchronously
            await LoadNetworkPathAsync(app);

            // Update status
            StatusText.Text = $"Loaded details for {app.DisplayName}";
        }

        #region Update Methods

        private void UpdateHeader(ApplicationDetail app)
        {
            AppTitleText.Text = app.DisplayName;
            AppSubtitleText.Text = $"{app.Version} • {app.Publisher}";

            if (app.IconImage != null)
            {
                AppIconImage.Source = app.IconImage;
            }
        }

        private void UpdateOverview(ApplicationDetail app)
        {
            // Basic Information
            AppNameText.Text = app.DisplayName;
            AppVersionText.Text = app.Version;
            PublisherText.Text = app.Publisher;
            CategoryText.Text = app.Category;
            FileSizeText.Text = app.Size > 0 ? app.SizeFormatted : "Unknown";
            InstallContextText.Text = app.InstallContext;
            OwnerText.Text = string.IsNullOrEmpty(app.Owner) ? "Not specified" : app.Owner;
            DeveloperText.Text = string.IsNullOrEmpty(app.Developer) ? "Not specified" : app.Developer;
            LastModifiedText.Text = app.LastModifiedDateTime != DateTime.MinValue
                ? app.LastModifiedFormatted
                : "Unknown";
        }

        private void UpdateDescription(ApplicationDetail app)
        {
            DescriptionText.Text = string.IsNullOrEmpty(app.Description)
                ? "No description provided"
                : app.Description;
        }

        private void UpdateCommands(ApplicationDetail app)
        {
            InstallCommandText.Text = app.InstallCommand;
            UninstallCommandText.Text = app.UninstallCommand;
        }

        private void UpdateDetectionRules(ApplicationDetail app)
        {
            DetectionRulesList.ItemsSource = app.DetectionRules;
        }

        private void UpdateRequirements(ApplicationDetail app)
        {
            // System Requirements
            DiskSpaceText.Text = app.MinimumFreeDiskSpaceInMB > 0
                ? $"{app.MinimumFreeDiskSpaceInMB} MB"
                : "Not specified";
            MemoryText.Text = app.MinimumMemoryInMB > 0
                ? $"{app.MinimumMemoryInMB} MB"
                : "Not specified";
            ProcessorsText.Text = app.MinimumNumberOfProcessors > 0
                ? $"{app.MinimumNumberOfProcessors}"
                : "Not specified";
            CpuSpeedText.Text = app.MinimumCpuSpeedInMHz > 0
                ? $"{app.MinimumCpuSpeedInMHz} MHz"
                : "Not specified";
            ArchitecturesText.Text = string.IsNullOrEmpty(app.ApplicableArchitectures)
                ? "Not specified"
                : app.ApplicableArchitectures;
            MinWindowsText.Text = string.IsNullOrEmpty(app.MinimumSupportedWindowsRelease)
                ? "Not specified"
                : app.MinimumSupportedWindowsRelease;
        }

        private void UpdateGroups(ApplicationDetail app)
        {
            var groups = app.AssignedGroups ?? new System.Collections.Generic.List<AssignedGroup>();

            AssignedGroupsList.ItemsSource = groups;
            GroupCountText.Text = groups.Count == 1 ? "1 group" : $"{groups.Count} groups";

            if (groups.Count == 0)
            {
                NoGroupsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                NoGroupsPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdatePublishing(ApplicationDetail app)
        {
            UploadStateText.Text = string.IsNullOrEmpty(app.UploadState) ? "Unknown" : app.UploadState;
            PublishingStateText.Text = string.IsNullOrEmpty(app.PublishingState) ? "Unknown" : app.PublishingState;
            CreatedDateText.Text = app.CreatedDateTime != DateTime.MinValue
                ? app.CreatedDateFormatted
                : "Unknown";
        }

        private void UpdateUrls(ApplicationDetail app)
        {
            // Privacy URL
            if (!string.IsNullOrEmpty(app.PrivacyInformationUrl))
            {
                PrivacyUrlPanel.Visibility = Visibility.Visible;
                PrivacyUrlText.Text = app.PrivacyInformationUrl;
            }
            else
            {
                PrivacyUrlPanel.Visibility = Visibility.Collapsed;
            }

            // Information URL
            if (!string.IsNullOrEmpty(app.InformationUrl))
            {
                InfoUrlPanel.Visibility = Visibility.Visible;
                InfoUrlText.Text = app.InformationUrl;
            }
            else
            {
                InfoUrlPanel.Visibility = Visibility.Collapsed;
            }

            // Hide entire URLs card if both are empty
            if (string.IsNullOrEmpty(app.PrivacyInformationUrl) &&
                string.IsNullOrEmpty(app.InformationUrl))
            {
                UrlsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                UrlsPanel.Visibility = Visibility.Visible;
            }
        }

        #endregion

        #region Network Path Loading

        private async Task LoadNetworkPathAsync(ApplicationDetail app)
        {
            StatusText.Text = "Finding network share path...";
            SourcePathText.Text = "Searching...";
            ActionButtonsPanel.Visibility = Visibility.Collapsed;
            ManualBrowsePanel.Visibility = Visibility.Collapsed;

            await Task.Run(async () =>
            {
                try
                {
                    var networkPath = NetworkShareHelper.FindApplicationPath(app.DisplayName, app.Version);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        if (!string.IsNullOrEmpty(networkPath))
                        {
                            if (_currentApp != null)
                            {
                                _currentApp.NetworkSharePath = networkPath;
                            }
                            SourcePathText.Text = networkPath;
                            ActionButtonsPanel.Visibility = Visibility.Visible;
                            ManualBrowsePanel.Visibility = Visibility.Collapsed;
                            PathHelpText.Text = "✅ Package path validated - ready for updates";
                            StatusText.Text = $"Loaded details for {app.DisplayName}";
                        }
                        else
                        {
                            SourcePathText.Text = "❌ Network share folder not found";
                            ActionButtonsPanel.Visibility = Visibility.Collapsed;
                            ManualBrowsePanel.Visibility = Visibility.Visible;
                            PathHelpText.Text = "⚠️ Browse for Deploy-Application.exe";
                            StatusText.Text = $"Loaded details for {app.DisplayName} - Network path not found";
                        }
                    });
                }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        SourcePathText.Text = "❌ Error searching network path";
                        ActionButtonsPanel.Visibility = Visibility.Collapsed;
                        ManualBrowsePanel.Visibility = Visibility.Visible;
                        PathHelpText.Text = "⚠️ Browse for Deploy-Application.exe";
                        StatusText.Text = $"Error: {ex.Message}";
                        Debug.WriteLine($"Error loading network path: {ex}");
                    });
                }
            });
        }

        #endregion

        #region Group Interactions

        private void GroupItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var grid = sender as Grid;
            var group = grid?.Tag as AssignedGroup;

            if (group != null && !string.IsNullOrEmpty(group.GroupId))
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var intuneService = mainWindow?.GetIntuneService();

                if (intuneService != null)
                {
                    var dialog = new GroupMembersDialog(
                        intuneService,
                        group.GroupId,
                        group.GroupName,
                        group.AssignmentType)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    dialog.ShowDialog();
                }
                else
                {
                    MessageBox.Show("Unable to access Intune service. Please ensure you're connected.",
                        "Service Unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void GroupItem_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush(Color.FromRgb(239, 246, 255)); // #EFF6FF
                var hint = FindVisualChild<TextBlock>(border, "ViewMembersHint");
                if (hint != null)
                {
                    hint.Visibility = Visibility.Visible;
                }
            }
        }

        private void GroupItem_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = Brushes.White;
                var hint = FindVisualChild<TextBlock>(border, "ViewMembersHint");
                if (hint != null)
                {
                    hint.Visibility = Visibility.Collapsed;
                }
            }
        }

        private T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                if (child is T element && element.Name == name)
                    return element;

                var result = FindVisualChild<T>(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region Action Buttons

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackToListRequested?.Invoke(this, EventArgs.Empty);
        }

        private async void UpdateApplicationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null)
            {
                MessageBox.Show("No application selected.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentApp = _currentApp;
            string packagePath = "";

            if (!string.IsNullOrEmpty(currentApp.NetworkSharePath) &&
                ValidatePackageForUpdate(currentApp.NetworkSharePath))
            {
                packagePath = currentApp.NetworkSharePath;
            }
            else
            {
                var result = MessageBox.Show(
                    $"Network share path for '{currentApp.DisplayName}' was not found or is invalid.\n\n" +
                    $"Would you like to manually browse for the Deploy-Application.exe file?",
                    "Network Path Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                packagePath = BrowseForDeployApplicationFile();
                if (string.IsNullOrEmpty(packagePath))
                    return;
            }

            await PerformPackageUpdate(packagePath);
        }

        private void OpenScriptButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentApp?.NetworkSharePath))
                {
                    var scriptPath = Path.Combine(_currentApp.NetworkSharePath, "Application", "Deploy-Application.ps1");

                    if (!File.Exists(scriptPath))
                    {
                        scriptPath = Path.Combine(_currentApp.NetworkSharePath, "Deploy-Application.ps1");
                    }

                    if (File.Exists(scriptPath))
                    {
                        OpenScriptInEditor(scriptPath);
                        return;
                    }
                }

                var message = string.IsNullOrEmpty(_currentApp?.NetworkSharePath)
                    ? $"Network share path not found for {_currentApp?.DisplayName}.\n\nWould you like to browse for the Deploy-Application.ps1 file manually?"
                    : $"Deploy-Application.ps1 not found in:\n{_currentApp.NetworkSharePath}\n\nWould you like to browse for the file manually?";

                var result = MessageBox.Show(message, "Script Not Found",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    BrowseForScriptManually();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening script: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error opening script";
            }
        }

        private void ReportingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null)
            {
                MessageBox.Show("No application selected.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText.Text = "Opening installation report...";

                var reportingWindow = new Views.ReportingWindow(_currentApp.Id, _currentApp.DisplayName)
                {
                    Owner = Window.GetWindow(this)
                };

                reportingWindow.ShowDialog();

                StatusText.Text = $"Viewing report for {_currentApp.DisplayName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening reporting window:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to open reporting window";
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentApp?.NetworkSharePath))
            {
                MessageBox.Show("Network share path not found for this application.", "Path Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start("explorer.exe", _currentApp.NetworkSharePath);
                StatusText.Text = $"Opened folder: {_currentApp.DisplayName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error opening folder";
            }
        }

        private void CopyPathButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentApp?.NetworkSharePath))
            {
                Clipboard.SetText(_currentApp.NetworkSharePath);
                StatusText.Text = "Network path copied to clipboard";
            }
        }

        private void BrowsePackageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Browsing for Deploy-Application.exe...";

            var packagePath = BrowseForDeployApplicationFile();
            if (!string.IsNullOrEmpty(packagePath))
            {
                RefreshPathValidation(packagePath);
                StatusText.Text = $"Package path set manually: {Path.GetFileName(packagePath)}";
            }
            else
            {
                StatusText.Text = "Browse cancelled";
            }
        }

        private void PrivacyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentApp?.PrivacyInformationUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentApp.PrivacyInformationUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening URL: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void InfoUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentApp?.InformationUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentApp.InformationUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening URL: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Helper Methods

        private string BrowseForDeployApplicationFile()
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select Deploy-Application.exe",
                    Filter = "Deploy-Application|Deploy-Application.exe|Executable Files|*.exe|All Files|*.*",
                    FileName = "Deploy-Application.exe",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (_currentApp != null && !string.IsNullOrEmpty(_currentApp.NetworkSharePath))
                {
                    try
                    {
                        var possiblePath = Path.Combine(_currentApp.NetworkSharePath, "Application");
                        if (Directory.Exists(possiblePath))
                        {
                            openFileDialog.InitialDirectory = possiblePath;
                        }
                        else if (Directory.Exists(_currentApp.NetworkSharePath))
                        {
                            openFileDialog.InitialDirectory = _currentApp.NetworkSharePath;
                        }
                    }
                    catch { }
                }

                if (openFileDialog.ShowDialog() == true)
                {
                    var selectedFile = openFileDialog.FileName;

                    if (!Path.GetFileName(selectedFile).Equals("Deploy-Application.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("Please select the Deploy-Application.exe file.", "Invalid File",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    var applicationFolder = Path.GetDirectoryName(selectedFile);
                    if (string.IsNullOrEmpty(applicationFolder))
                    {
                        MessageBox.Show("Could not determine application folder from selected file.", "Invalid Path",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    var packageRoot = Path.GetDirectoryName(applicationFolder);
                    if (string.IsNullOrEmpty(packageRoot))
                    {
                        MessageBox.Show("Could not determine package root folder. Please ensure Deploy-Application.exe is in an 'Application' subfolder.",
                            "Invalid Structure", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    if (!ValidateManuallySelectedPackage(packageRoot))
                        return "";

                    if (_currentApp != null)
                    {
                        _currentApp.NetworkSharePath = packageRoot;
                    }

                    return packageRoot;
                }

                return "";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing for file: {ex.Message}", "Browse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return "";
            }
        }

        private bool ValidateManuallySelectedPackage(string packageRoot)
        {
            try
            {
                var applicationFolder = Path.Combine(packageRoot, "Application");
                if (!Directory.Exists(applicationFolder))
                {
                    MessageBox.Show($"Application folder not found at:\n{applicationFolder}\n\nPlease ensure Deploy-Application.exe is located in an 'Application' subfolder.",
                                  "Invalid Package Structure", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var deployAppPath = Path.Combine(applicationFolder, "Deploy-Application.exe");
                if (!File.Exists(deployAppPath))
                {
                    MessageBox.Show($"Deploy-Application.exe not found at:\n{deployAppPath}",
                                  "Deploy-Application.exe Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var fileInfo = new FileInfo(deployAppPath);
                var message = $"✅ Package validated successfully!\n\n" +
                             $"📁 Package Root: {packageRoot}\n" +
                             $"📄 Deploy-Application.exe: {fileInfo.Length:N0} bytes\n" +
                             $"📅 Last Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                             $"Continue with the update?";

                var result = MessageBox.Show(message, "Package Validation Success",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                return result == MessageBoxResult.Yes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error validating package: {ex.Message}", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private bool ValidatePackageForUpdate(string networkSharePath)
        {
            try
            {
                if (!Directory.Exists(networkSharePath))
                    return false;

                var applicationFolder = Path.Combine(networkSharePath, "Application");
                if (!Directory.Exists(applicationFolder))
                    return false;

                var deployAppPath = Path.Combine(applicationFolder, "Deploy-Application.exe");
                if (!File.Exists(deployAppPath))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void RefreshPathValidation(string path)
        {
            if (!string.IsNullOrEmpty(path) && ValidatePackageForUpdate(path))
            {
                SourcePathText.Text = path;
                ActionButtonsPanel.Visibility = Visibility.Visible;
                ManualBrowsePanel.Visibility = Visibility.Collapsed;
                PathHelpText.Text = "✅ Package path validated - ready for updates";
            }
            else
            {
                SourcePathText.Text = string.IsNullOrEmpty(path) ? "❌ No path specified" : "❌ Invalid package structure";
                ActionButtonsPanel.Visibility = Visibility.Collapsed;
                ManualBrowsePanel.Visibility = Visibility.Visible;
                PathHelpText.Text = "⚠️ Browse for Deploy-Application.exe";
            }
        }

        private async Task PerformPackageUpdate(string packagePath)
        {
            if (_currentApp == null)
            {
                MessageBox.Show("No application selected.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var currentApp = _currentApp;

            try
            {
                UpdateApplicationButton.IsEnabled = false;
                UpdateApplicationButton.Content = "⏳ Updating...";

                StatusText.Text = "Starting package update...";

                var deployAppPath = Path.Combine(packagePath, "Application", "Deploy-Application.exe");
                var fileInfo = new FileInfo(deployAppPath);

                var confirmMessage = $"Update '{currentApp.DisplayName}' with package from:\n\n" +
                                   $"📁 {packagePath}\n" +
                                   $"📄 Deploy-Application.exe ({fileInfo.Length:N0} bytes)\n" +
                                   $"📅 Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n\n" +
                                   $"📦 The .intunewin package will be recreated and uploaded\n" +
                                   $"⚠️  Application metadata will NOT be changed\n\n" +
                                   $"Continue with update?";

                var result = MessageBox.Show(confirmMessage, "Confirm Package Update",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                var progressTracker = new UpdateProgressTracker(this);

                // Initialize Intune service with settings
                var settingsService = new IntunePackagingTool.Services.SettingsService();
                var settings = settingsService.Settings;
                var intuneService = new IntunePackagingTool.Services.IntuneService(
                    settings.Authentication.TenantId,
                    settings.Authentication.ClientId,
                    settings.Authentication.CertificateThumbprint
                );
                using var uploadService = new IntunePackagingTool.Services.IntuneUploadService(intuneService);

                var success = await uploadService.UpdateExistingApplicationAsync(
                    currentApp.Id,
                    packagePath,
                    progressTracker
                );

                if (success)
                {
                    intuneService.InvalidateApplicationCache(currentApp.Id);

                    StatusText.Text = $"Package updated successfully • {DateTime.Now:HH:mm:ss}";
                    MessageBox.Show($"✅ Package for '{currentApp.DisplayName}' updated successfully!\n\n" +
                                  $"📦 New .intunewin file uploaded to Intune\n" +
                                  $"📁 Source: {packagePath}",
                                  "Update Complete",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Information);
                }
                else
                {
                    StatusText.Text = "Package update failed";
                    MessageBox.Show("❌ Failed to update application package", "Update Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Package update failed";
                MessageBox.Show($"❌ Error updating application package:\n\n{ex.Message}", "Update Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateApplicationButton.IsEnabled = true;
                UpdateApplicationButton.Content = "🔄 Update";
            }
        }

        private void BrowseForScriptManually()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = $"Locate Deploy-Application.ps1 for {_currentApp?.DisplayName}",
                Filter = "PowerShell Scripts (*.ps1)|*.ps1|All Files (*.*)|*.*",
                FileName = "Deploy-Application.ps1",
                InitialDirectory = @"\\nbb.local\sys\SCCMData"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var selectedFile = openFileDialog.FileName;

                var directory = Path.GetDirectoryName(selectedFile);
                if (directory != null)
                {
                    if (directory.EndsWith("Application", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_currentApp != null)
                        {
                            _currentApp.NetworkSharePath = Path.GetDirectoryName(directory) ?? directory;
                        }
                    }
                    else
                    {
                        if (_currentApp != null)
                        {
                            _currentApp.NetworkSharePath = directory;
                        }
                    }

                    if (_currentApp != null)
                    {
                        SourcePathText.Text = _currentApp.NetworkSharePath;
                        ActionButtonsPanel.Visibility = Visibility.Visible;
                    }
                }

                OpenScriptInEditor(selectedFile);
            }
        }

        private void OpenScriptInEditor(string scriptPath)
        {
            try
            {
                var vsCodePath = FindVSCodePath();
                if (!string.IsNullOrEmpty(vsCodePath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = vsCodePath,
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    StatusText.Text = $"Opened script in VS Code: {Path.GetFileName(scriptPath)}";
                    return;
                }
            }
            catch { }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell_ise.exe",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = true
                };
                Process.Start(psi);
                StatusText.Text = $"Opened script in PowerShell ISE: {Path.GetFileName(scriptPath)}";
            }
            catch
            {
                try
                {
                    Process.Start("notepad.exe", $"\"{scriptPath}\"");
                    StatusText.Text = $"Opened script in Notepad: {Path.GetFileName(scriptPath)}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not open script in any editor: {ex.Message}",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string? FindVSCodePath()
        {
            string[] possiblePaths = {
                @"C:\Program Files\Microsoft VS Code\Code.exe",
                @"C:\Program Files (x86)\Microsoft VS Code\Code.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Microsoft VS Code\Code.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        public void ResetScrollPosition()
        {
            DetailScrollViewer?.ScrollToTop();
        }

        #endregion

        #region Progress Tracker

        private class UpdateProgressTracker : IntunePackagingTool.Services.IUploadProgress
        {
            private readonly ApplicationDetailView _view;

            public UpdateProgressTracker(ApplicationDetailView view)
            {
                _view = view;
            }

            public void UpdateProgress(int percentage, string message)
            {
                _view.Dispatcher.Invoke(() =>
                {
                    _view.StatusText.Text = $"{message} ({percentage}%)";
                });
            }
        }

        #endregion
    }
}