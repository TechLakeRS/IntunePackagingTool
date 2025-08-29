using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace IntunePackagingTool
{
    public partial class ApplicationDetailView : UserControl
    {
        public event EventHandler BackToListRequested;
        private ApplicationDetail _currentApp;

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

            // Additional Information
            IsAssignedText.Text = app.IsAssigned ? "Yes" : "No";
            AllowUninstallText.Text = app.AllowAvailableUninstall ? "Yes" : "No";

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
                // Look for Deploy-Application.ps1 in the network share folder
                var scriptPath = Path.Combine(_currentApp.NetworkSharePath, "Deploy-Application.ps1");

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
      public static class NetworkShareHelper
    {
        private static readonly string SharePath = @"\\nbb.local\sys\sccmdata\intuneapplications";

        public static string FindApplicationPath(string appName, string version = null)
        {
            try
            {
                if (!Directory.Exists(SharePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Network share not accessible: {SharePath}");
                    return null;
                }

                var folders = Directory.GetDirectories(SharePath);
                
                // Create multiple search patterns to find the folder
                var searchPatterns = CreateSearchPatterns(appName, version);

                foreach (var pattern in searchPatterns)
                {
                    // Try exact match first
                    var exactMatch = folders.FirstOrDefault(f => 
                        string.Equals(Path.GetFileName(f), pattern, StringComparison.OrdinalIgnoreCase));
                    
                    if (exactMatch != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found exact match: {exactMatch}");
                        return exactMatch;
                    }
                }

                // If no exact match, try partial matching
                foreach (var pattern in searchPatterns)
                {
                    var partialMatch = folders.FirstOrDefault(f => 
                        Path.GetFileName(f).ToLower().Contains(pattern.ToLower()));
                    
                    if (partialMatch != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found partial match: {partialMatch}");
                        return partialMatch;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"No network share folder found for: {appName}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding app path: {ex.Message}");
                return null;
            }
        }

        private static List<string> CreateSearchPatterns(string appName, string version)
        {
            var patterns = new List<string>();
            
            if (string.IsNullOrEmpty(appName))
                return patterns;

            // Clean app name variations
            var cleanName = appName.Trim();
            var dashName = cleanName.Replace(" ", "-");
            var noSpaceName = cleanName.Replace(" ", "");
            
            // Add patterns with and without version
            patterns.Add(cleanName);
            patterns.Add(dashName);
            patterns.Add(noSpaceName);
            
            if (!string.IsNullOrEmpty(version))
            {
                var cleanVersion = version.Replace("v", "").Replace("V", "");
                patterns.Add($"{cleanName}-{version}");
                patterns.Add($"{dashName}-{version}");
                patterns.Add($"{cleanName}-{cleanVersion}");
                patterns.Add($"{dashName}-{cleanVersion}");
                patterns.Add($"{noSpaceName}-{cleanVersion}");
            }
            
            return patterns.Distinct().ToList();
        }

        public static bool HasPSADTScript(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return false;
                
            return File.Exists(Path.Combine(folderPath, "Deploy-Application.ps1"));
        }
    }
}