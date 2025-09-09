
using IntunePackagingTool.Models;
using IntunePackagingTool.Utilities;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;


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
                Debug.WriteLine($"DataContext changed from {e.OldValue?.GetType()?.Name} to {e.NewValue?.GetType()?.Name}");

                if (e.NewValue is ApplicationDetail app)
                {
                    Debug.WriteLine($"App: {app.DisplayName}");
                    Debug.WriteLine($"IconImage null? {app.IconImage == null}");

                    if (app.IconImage != null)
                    {
                        Debug.WriteLine($"Icon size: {app.IconImage.PixelWidth}x{app.IconImage.PixelHeight}");

                        AppIconImage.Source = app.IconImage;
                    }
                }
            };
        }

        public async void LoadApplicationDetail(ApplicationDetail app)
        {
            _currentApp = app;
            this.DataContext = app;

            // Update UI with all the comprehensive app details
            AppTitleText.Text = app.DisplayName;

            // Basic Information
            AppNameText.Text = app.DisplayName;
            AppVersionText.Text = app.Version;
            PublisherText.Text = app.Publisher;
            CategoryText.Text = app.Category;
            InstallContextText.Text = app.InstallContext;
            OwnerText.Text = string.IsNullOrEmpty(app.Owner) ? "Not specified" : app.Owner;
            DeveloperText.Text = string.IsNullOrEmpty(app.Developer) ? "Not specified" : app.Developer;
            FileSizeText.Text = app.Size > 0 ? app.SizeFormatted : "Unknown";

            // Description
            DescriptionText.Text = string.IsNullOrEmpty(app.Description) ? "No description provided" : app.Description;

            // File & Publishing Information
            FileNameText.Text = string.IsNullOrEmpty(app.FileName) ? "Not specified" : app.FileName;
            SetupFilePathText.Text = string.IsNullOrEmpty(app.SetupFilePath) ? "Not specified" : app.SetupFilePath;
            UploadStateText.Text = string.IsNullOrEmpty(app.UploadState) ? "Unknown" : app.UploadState;
            PublishingStateText.Text = string.IsNullOrEmpty(app.PublishingState) ? "Unknown" : app.PublishingState;
            IsFeaturedText.Text = app.IsFeatured ? "Yes" : "No";
            CreatedDateText.Text = app.CreatedDateTime != DateTime.MinValue ? app.CreatedDateFormatted : "Unknown";
            LastModifiedText.Text = app.LastModifiedDateTime != DateTime.MinValue ? app.LastModifiedFormatted : "Unknown";

            // System Requirements
            DiskSpaceText.Text = app.MinimumFreeDiskSpaceInMB > 0 ? $"{app.MinimumFreeDiskSpaceInMB} MB" : "Not specified";
            MemoryText.Text = app.MinimumMemoryInMB > 0 ? $"{app.MinimumMemoryInMB} MB" : "Not specified";
            ProcessorsText.Text = app.MinimumNumberOfProcessors > 0 ? $"{app.MinimumNumberOfProcessors}" : "Not specified";
            CpuSpeedText.Text = app.MinimumCpuSpeedInMHz > 0 ? $"{app.MinimumCpuSpeedInMHz} MHz" : "Not specified";
            ArchitecturesText.Text = string.IsNullOrEmpty(app.ApplicableArchitectures) ? "Not specified" : app.ApplicableArchitectures;
            MinWindowsText.Text = string.IsNullOrEmpty(app.MinimumSupportedWindowsRelease) ? "Not specified" : app.MinimumSupportedWindowsRelease;



            // Install Commands
            InstallCommandText.Text = app.InstallCommand;
            UninstallCommandText.Text = app.UninstallCommand;

            // URLs (show/hide panels based on availability)
            if (!string.IsNullOrEmpty(app.PrivacyInformationUrl))
            {
                PrivacyUrlPanel.Visibility = Visibility.Visible;
                PrivacyUrlText.Text = app.PrivacyInformationUrl;
            }
            else
            {
                PrivacyUrlPanel.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrEmpty(app.InformationUrl))
            {
                InfoUrlPanel.Visibility = Visibility.Visible;
                InfoUrlText.Text = app.InformationUrl;
            }
            else
            {
                InfoUrlPanel.Visibility = Visibility.Collapsed;
            }

            // Load collections
            DetectionRulesList.ItemsSource = app.DetectionRules;
            AssignedGroupsList.ItemsSource = app.AssignedGroups;
            ReturnCodesList.ItemsSource = app.ReturnCodes;

            // Find network share path asynchronously
            StatusText.Text = "Finding network share path...";
            SourcePathText.Text = "Searching for network path...";
            ActionButtonsPanel.Visibility = Visibility.Collapsed;

            await Task.Run(() =>
            {
                var networkPath = NetworkShareHelper.FindApplicationPath(app.DisplayName, app.Version);

                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(networkPath))
                    {
                        _currentApp.NetworkSharePath = networkPath;
                        SourcePathText.Text = networkPath;
                        ActionButtonsPanel.Visibility = Visibility.Visible;
                        StatusText.Text = $"Loaded comprehensive details for {app.DisplayName} from Intune";
                    }
                    else
                    {
                        SourcePathText.Text = "Network share folder not found";
                        ActionButtonsPanel.Visibility = Visibility.Collapsed;
                        StatusText.Text = $"Loaded details for {app.DisplayName} - Network path not found";
                    }
                });
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackToListRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OpenScriptButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentApp?.NetworkSharePath))
            {
                MessageBox.Show("Network share path not found for this application.", "Path Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {

                var scriptPath = Path.Combine(_currentApp.NetworkSharePath, "application", "Deploy-Application.ps1");

                if (File.Exists(scriptPath))
                {
                    // Try PowerShell ISE first
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "powershell_ise.exe",
                            Arguments = $"\"{scriptPath}\"",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                        StatusText.Text = $"Opened script in PowerShell ISE: {_currentApp.DisplayName}";
                    }
                    catch
                    {
                        // Fallback to notepad if PowerShell ISE not available
                        Process.Start("notepad.exe", scriptPath);
                        StatusText.Text = $"Opened script in Notepad: {_currentApp.DisplayName}";
                    }
                }
                else
                {
                    // Script not found, offer to open the folder instead
                    var result = MessageBox.Show(
                        $"Deploy-Application.ps1 not found in:\n{_currentApp.NetworkSharePath}\n\nWould you like to open the folder to browse for scripts?",
                        "Script Not Found",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start("explorer.exe", _currentApp.NetworkSharePath);
                        StatusText.Text = "Opened network folder for manual browsing";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening script: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error opening script";
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
                PathHelpText.Text = "⚠️ Please browse for Deploy-Application.exe to enable package updates";
            }
        }
        // URL click handlers
        private void PrivacyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentApp?.PrivacyInformationUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentApp.PrivacyInformationUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void UpdateApplicationButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentApp == null)
            {
                MessageBox.Show("No application selected.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string packagePath = "";

            // Try to use the network share path first
            if (!string.IsNullOrEmpty(_currentApp.NetworkSharePath) &&
                ValidatePackageForUpdate(_currentApp.NetworkSharePath))
            {
                packagePath = _currentApp.NetworkSharePath;
            }
            else
            {
                // Network share not found or invalid, offer manual selection
                var result = MessageBox.Show(
                    $"Network share path for '{_currentApp.DisplayName}' was not found or is invalid.\n\n" +
                    $"Would you like to manually browse for the Deploy-Application.exe file?",
                    "Network Path Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                packagePath = BrowseForDeployApplicationFile();
                if (string.IsNullOrEmpty(packagePath))
                    return; // User cancelled
            }

            await PerformPackageUpdate(packagePath);
        }

        // MANUAL BROWSE METHOD - Allows user to select Deploy-Application.exe manually
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

                // Try to set initial directory to a reasonable location
                if (!string.IsNullOrEmpty(_currentApp?.NetworkSharePath))
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

                    // Validate that it's actually Deploy-Application.exe
                    if (!Path.GetFileName(selectedFile).Equals("Deploy-Application.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("Please select the Deploy-Application.exe file.", "Invalid File",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    // Get the parent directory (should be the Application folder)
                    var applicationFolder = Path.GetDirectoryName(selectedFile);
                    if (string.IsNullOrEmpty(applicationFolder))
                    {
                        MessageBox.Show("Could not determine application folder from selected file.", "Invalid Path",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    // Get the package root (parent of Application folder)
                    var packageRoot = Path.GetDirectoryName(applicationFolder);
                    if (string.IsNullOrEmpty(packageRoot))
                    {
                        MessageBox.Show("Could not determine package root folder. Please ensure Deploy-Application.exe is in an 'Application' subfolder.", "Invalid Structure",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return "";
                    }

                    // Validate the structure
                    if (!ValidateManuallySelectedPackage(packageRoot))
                        return "";

                    // Update the current app's network share path for future use
                    _currentApp.NetworkSharePath = packageRoot;
                    StatusText.Text = $"Using manually selected package: {packageRoot}";

                    return packageRoot;
                }

                return ""; // User cancelled
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error browsing for file: {ex.Message}", "Browse Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return "";
            }
        }

        // VALIDATION METHOD - For manually selected packages
        private bool ValidateManuallySelectedPackage(string packageRoot)
        {
            try
            {
                // Check if Application folder exists
                var applicationFolder = Path.Combine(packageRoot, "Application");
                if (!Directory.Exists(applicationFolder))
                {
                    MessageBox.Show($"Application folder not found at:\n{applicationFolder}\n\nPlease ensure Deploy-Application.exe is located in an 'Application' subfolder.",
                                  "Invalid Package Structure", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // Check if Deploy-Application.exe exists
                var deployAppPath = Path.Combine(applicationFolder, "Deploy-Application.exe");
                if (!File.Exists(deployAppPath))
                {
                    MessageBox.Show($"Deploy-Application.exe not found at:\n{deployAppPath}",
                                  "Deploy-Application.exe Missing", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                // Show success message with package info
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

        // VALIDATION METHOD - For automatic network path validation (silent)
        private bool ValidatePackageForUpdate(string networkSharePath)
        {
            try
            {
                // Check if network path exists
                if (!Directory.Exists(networkSharePath))
                {
                    return false; // Don't show error message here, let caller handle it
                }

                // Check if Application folder exists
                var applicationFolder = Path.Combine(networkSharePath, "Application");
                if (!Directory.Exists(applicationFolder))
                {
                    return false;
                }

                // Check if Deploy-Application.exe exists
                var deployAppPath = Path.Combine(applicationFolder, "Deploy-Application.exe");
                if (!File.Exists(deployAppPath))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        // PACKAGE UPDATE METHOD - Performs the actual update process
        private async Task PerformPackageUpdate(string packagePath)
        {
            try
            {
                // Disable the button and show progress
                UpdateApplicationButton.IsEnabled = false;
                UpdateApplicationButton.Content = "Updating...";

                StatusText.Text = "Starting package update...";

                // Show confirmation with package details
                var deployAppPath = Path.Combine(packagePath, "Application", "Deploy-Application.exe");
                var fileInfo = new FileInfo(deployAppPath);

                var confirmMessage = $"Update '{_currentApp.DisplayName}' with package from:\n\n" +
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

                // Create progress tracker
                var progressTracker = new UpdateProgressTracker(this);

                // Create upload service and update
                var intuneService = new IntunePackagingTool.Services.IntuneService();
                using var uploadService = new IntunePackagingTool.Services.IntuneUploadService(intuneService);

                var success = await uploadService.UpdateExistingApplicationAsync(
                    _currentApp.Id,
                    packagePath,
                    progressTracker
                );

                if (success)
                {
                    StatusText.Text = $"Package updated successfully • {DateTime.Now:HH:mm:ss}";
                    MessageBox.Show($"✅ Package for '{_currentApp.DisplayName}' updated successfully!\n\n" +
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
                UpdateApplicationButton.Content = "🔄 Update Package";
            }
        }

        // BROWSE BUTTON HANDLER - For manual package folder selection
        private void BrowsePackageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Browsing for Deploy-Application.exe...";

            var packagePath = BrowseForDeployApplicationFile();
            if (!string.IsNullOrEmpty(packagePath))
            {
                // Update the UI to show the manually selected path
                RefreshPathValidation(packagePath);
                StatusText.Text = $"Package path set manually: {Path.GetFileName(packagePath)}";
            }
            else
            {
                StatusText.Text = "Browse cancelled";
            }
        }

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


        private void InfoUrl_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_currentApp?.InformationUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new ProcessStartInfo
                    {
                        FileName = _currentApp.InformationUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

}