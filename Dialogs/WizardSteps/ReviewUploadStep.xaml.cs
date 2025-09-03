using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;

namespace IntunePackagingTool.WizardSteps
{
    public partial class ReviewUploadStep : UserControl, IUploadProgress, IDisposable
    {
        public event EventHandler BackRequested;
        public event EventHandler<UploadCompleteEventArgs> UploadComplete;

        // Required properties for wizard
        public ApplicationInfo ApplicationInfo { get; set; }
        public ObservableCollection<DetectionRule> DetectionRules { get; set; }
        public string PackagePath { get; set; }
        public string? SelectedIconPath { get; set; }

        // Events
        public delegate void UploadRequestedEventHandler(string installCommand, string uninstallCommand, string description, string installContext);
        

        // Private fields
        private ApplicationDetail _applicationDetail;
        private IntuneService _intuneService;
        private ObservableCollection<ProgressStep> _progressSteps;
        private bool _isDisposed;

        public ReviewUploadStep()
        {
            InitializeComponent();
            _progressSteps = new ObservableCollection<ProgressStep>();
            ProgressSteps.ItemsSource = _progressSteps;
            DetectionRules = new ObservableCollection<DetectionRule>();
        }

        public void UpdateSummaryData(string installCommand, string uninstallCommand, string description, string installContext)
        {
            // Initialize if needed
            _applicationDetail ??= new ApplicationDetail();

            _applicationDetail.InstallCommand = installCommand;
            _applicationDetail.UninstallCommand = uninstallCommand;
            _applicationDetail.Description = description;
            _applicationDetail.InstallContext = installContext;

            RefreshUI();
        }

        public void LoadApplication(ApplicationDetail appDetail)
        {
            if (appDetail == null)
            {
                Debug.WriteLine("Warning: LoadApplication called with null appDetail");
                return;
            }

            _applicationDetail = appDetail;
            EnsureIntuneService();

            // Populate application details with null checks
            AppNameText.Text = appDetail.DisplayName ?? "Unknown";
            VersionText.Text = appDetail.Version ?? "Unknown";
            PublisherText.Text = appDetail.Publisher ?? "Unknown";
            PackageSizeText.Text = appDetail.SizeFormatted ?? "Unknown";

            // Show detection rules from ApplicationDetail
            UpdateDetectionRulesDisplay(appDetail.DetectionRules);
            GenerateGroupNames();
        }

        public void LoadFromApplicationInfo(ApplicationInfo appInfo)
        {
            if (appInfo == null)
            {
                Debug.WriteLine("Warning: LoadFromApplicationInfo called with null appInfo");
                return;
            }

            ApplicationInfo = appInfo;

            // Create ApplicationDetail from ApplicationInfo
            _applicationDetail = new ApplicationDetail
            {
                DisplayName = appInfo.Name ?? "Unknown",
                Version = appInfo.Version ?? "1.0.0",
                Publisher = appInfo.Manufacturer ?? "Unknown",
                InstallContext = appInfo.InstallContext ?? "System",
                DetectionRules = DetectionRules?.ToList() ?? new List<DetectionRule>()
            };

            EnsureIntuneService();
            RefreshUI();
        }

        private void EnsureIntuneService()
        {
            if (_intuneService == null)
            {
                _intuneService = new IntuneService();
            }
        }

        private void RefreshUI()
        {
            if (_applicationDetail == null)
            {
                Debug.WriteLine("Warning: RefreshUI called with null _applicationDetail");
                return;
            }

            // Update UI elements with null checks
            AppNameText.Text = _applicationDetail.DisplayName ?? "Unknown";
            VersionText.Text = _applicationDetail.Version ?? "Unknown";
            PublisherText.Text = _applicationDetail.Publisher ?? "Unknown";
            InstallContextText.Text = _applicationDetail.InstallContext ?? "System";
            DescriptionText.Text = _applicationDetail.Description ?? "";
            InstallCommandText.Text = _applicationDetail.InstallCommand ?? "";
            UninstallCommandText.Text = _applicationDetail.UninstallCommand ?? "";

            UpdatePackageSizeDisplay();
            UpdateDetectionRulesDisplay();
            GenerateGroupNames();
        }

        private void UpdatePackageSizeDisplay()
        {
            if (!string.IsNullOrEmpty(PackagePath))
            {
                string fileToCheck = PackagePath;

                // If PackagePath is a directory, look for .intunewin file
                if (Directory.Exists(PackagePath))
                {
                    var intuneFolder = Path.Combine(PackagePath, "Intune");
                    if (Directory.Exists(intuneFolder))
                    {
                        var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
                        if (intuneWinFiles.Length > 0)
                        {
                            fileToCheck = intuneWinFiles[0];
                        }
                    }
                }

                if (File.Exists(fileToCheck))
                {
                    var fileInfo = new FileInfo(fileToCheck);
                    PackageSizeText.Text = GetFileSizeString(fileInfo.Length);
                }
                else
                {
                    PackageSizeText.Text = "File not found";
                }
            }
            else if (_applicationDetail != null)
            {
                PackageSizeText.Text = _applicationDetail.SizeFormatted ?? "Unknown";
            }
            else
            {
                PackageSizeText.Text = "Unknown";
            }
        }

        private void UpdateDetectionRulesDisplay(List<DetectionRule> rules = null)
        {
            var rulesToDisplay = rules ?? DetectionRules?.ToList() ?? _applicationDetail?.DetectionRules;

            if (rulesToDisplay != null && rulesToDisplay.Any())
            {
                var detectionRulesList = rulesToDisplay.Select(r =>
                    $"• {GetDetectionRuleTitle(r.Type.ToString())}: {r.Path}").ToList();
                DetectionRulesList.ItemsSource = detectionRulesList;
            }
            else
            {
                DetectionRulesList.ItemsSource = new[] { "• No detection rules configured" };
            }
        }

        private void GenerateGroupNames()
        {
            if (_applicationDetail == null) return;

            var cleanPublisher = CleanForGroupName(_applicationDetail.Publisher);
            var cleanAppName = CleanForGroupName(_applicationDetail.DisplayName);
            var cleanVersion = CleanForGroupName(_applicationDetail.Version);

            SysInstallGroupText.Text = $"SY_AZNBB_Intune_WksApp_{cleanPublisher}_{cleanAppName}_{cleanVersion}_Install";
            UsrInstallGroupText.Text = $"USR_AZNBB_Intune_WksApp_{cleanPublisher}_{cleanAppName}_{cleanVersion}_Install";
            SysUninstallGroupText.Text = $"SY_AZNBB_Intune_WksApp_{cleanPublisher}_{cleanAppName}_{cleanVersion}_Uninstall";
            UsrUninstallGroupText.Text = $"USR_AZNBB_Intune_WksApp_{cleanPublisher}_{cleanAppName}_{cleanVersion}_Uninstall";
        }

        private string CleanForGroupName(string input)
        {
            if (string.IsNullOrEmpty(input)) return "Unknown";

            var cleaned = Regex.Replace(input, @"[^a-zA-Z0-9\-\.]", "_");
            cleaned = Regex.Replace(cleaned, @"_{2,}", "_");
            cleaned = cleaned.Trim('_');

            if (cleaned.Length > 50)
                cleaned = cleaned.Substring(0, 50);

            return cleaned;
        }

        private string GetDetectionRuleTitle(string type)
        {
            return type?.ToLower() switch
            {
                "msi" => "MSI Product Code",
                "file" => "File System",
                "registry" => "Registry Key",
                "script" => "PowerShell Script",
                _ => "Detection Rule"
            };
        }

        private string GetFileSizeString(long bytes)
        {
            return bytes switch
            {
                > 1073741824 => $"{bytes / 1073741824.0:F1} GB",
                > 1048576 => $"{bytes / 1048576.0:F1} MB",
                > 1024 => $"{bytes / 1024.0:F1} KB",
                > 0 => $"{bytes} bytes",
                _ => "Unknown"
            };
        }

        public async Task<bool> StartUploadAsync()
        {
            if (_applicationDetail == null)
            {
                MessageBox.Show("Application details not loaded.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            ProgressPanel.Visibility = Visibility.Visible;
            _progressSteps.Clear();

            try
            {
                // Step 1: Upload application
                AddProgressStep("⏳", "Uploading application package to Intune...");
                await Task.Delay(500);

                var appId = await UploadApplication();
                UpdateProgressStep(0, "✅", "Application uploaded successfully");

                // Step 2: Create groups
                AddProgressStep("⏳", "Creating assignment groups in Azure AD...");
                await Task.Delay(500);

                var groupIds = await CreateAssignmentGroups();
                UpdateProgressStep(1, "✅", $"Created {groupIds.Count} assignment groups");

                // Step 3: Assign groups to app
                AddProgressStep("⏳", "Assigning groups to application...");
                await Task.Delay(500);

                await AssignGroupsToApplication(appId, groupIds);
                UpdateProgressStep(2, "✅", "Groups assigned to application");

                // Step 4: Complete
                AddProgressStep("🎉", "Deployment completed successfully!");
                UpdateProgress(100, "Upload complete!");

                UploadComplete?.Invoke(this, new UploadCompleteEventArgs
                {
                    ApplicationId = appId,
                    GroupIds = groupIds
                });

                await Task.Delay(1000);

                MessageBox.Show(
                    $"Application '{_applicationDetail.DisplayName}' has been successfully deployed to Intune!\n\n" +
                    "✅ Application uploaded\n" +
                    "✅ 4 assignment groups created\n" +
                    "✅ Groups assigned to application\n\n" +
                    "You can now add members to the groups from Azure AD.",
                    "Deployment Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Upload failed: {ex}");
                MessageBox.Show($"Upload failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                AddProgressStep("❌", $"Upload failed: {ex.Message}");
                return false;
            }
        }

        public void UpdateProgress(int percentage, string message)
        {
            UpdateProgress(percentage, message, 0, 0);
        }

        public void UpdateProgress(int percentage, string message, int currentChunk = 0, int totalChunks = 0)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ProgressPanel.Visibility != Visibility.Visible)
                    ProgressPanel.Visibility = Visibility.Visible;

                UploadProgressBar.Value = percentage;
                ProgressPercentageText.Text = $"{percentage}%";

                if (currentChunk > 0 && totalChunks > 0)
                {
                    ProgressStatusText.Text = $"Chunk {currentChunk}/{totalChunks}";
                }
                else
                {
                    ProgressStatusText.Text = message;
                }

                Debug.WriteLine($"Progress: {percentage}% - {message} (Chunk {currentChunk}/{totalChunks})");
            });
        }


        private async Task<string> UploadApplication()
        {
            EnsureIntuneService();
            var uploadService = new IntuneUploadService(_intuneService);

            try
            {
                // Check PackagePath early
                if (string.IsNullOrEmpty(PackagePath))
                {
                    throw new InvalidOperationException("Package path is required for upload. Please ensure a package has been selected.");
                }
                var appInfo = ApplicationInfo ?? new ApplicationInfo
                {
                    Name = _applicationDetail?.DisplayName ?? "Unknown",
                    Version = _applicationDetail?.Version ?? "1.0.0",
                    Manufacturer = _applicationDetail?.Publisher ?? "Unknown",
                    InstallContext = _applicationDetail?.InstallContext ?? "System",
                    SourcesPath = PackagePath
                };

                var installCommand = _applicationDetail?.InstallCommand ?? "Deploy-Application.exe -DeploymentType Install";
                var uninstallCommand = _applicationDetail?.UninstallCommand ?? "Deploy-Application.exe -DeploymentType Uninstall";
                var description = _applicationDetail?.Description ?? "";
                var installContext = _applicationDetail?.InstallContext ?? "System";

                // Combine detection rules from both sources
                var allDetectionRules = new List<DetectionRule>();
                if (DetectionRules != null) allDetectionRules.AddRange(DetectionRules);
                if (_applicationDetail?.DetectionRules != null)
                    allDetectionRules.AddRange(_applicationDetail.DetectionRules);

                // Remove duplicates based on Type and Path
                allDetectionRules = allDetectionRules
                    .GroupBy(r => new { r.Type, r.Path })
                    .Select(g => g.First())
                    .ToList();

                return await uploadService.UploadWin32ApplicationAsync(
                    appInfo,
                    PackagePath,
                    allDetectionRules,
                    installCommand,
                    uninstallCommand,
                    description,
                    installContext,
                    SelectedIconPath,
                    this
                );
            }
            finally
            {
                uploadService?.Dispose();
            }
        }

        private async Task<GroupAssignmentIds> CreateAssignmentGroups()
        {
            EnsureIntuneService();
            var groupIds = new GroupAssignmentIds();

            var tasks = new[]
            {
                CreateOrGetGroup(SysInstallGroupText.Text, "System devices for required installation")
                    .ContinueWith(t => groupIds.SystemInstallId = t.Result),
                CreateOrGetGroup(UsrInstallGroupText.Text, "Users for required installation")
                    .ContinueWith(t => groupIds.UserInstallId = t.Result),
                CreateOrGetGroup(SysUninstallGroupText.Text, "System devices for uninstallation")
                    .ContinueWith(t => groupIds.SystemUninstallId = t.Result),
                CreateOrGetGroup(UsrUninstallGroupText.Text, "Users for uninstallation")
                    .ContinueWith(t => groupIds.UserUninstallId = t.Result)
            };

            await Task.WhenAll(tasks);
            return groupIds;
        }

        private async Task<string> CreateOrGetGroup(string displayName, string description)
        {
            var fullDescription = $"{description} - {_applicationDetail?.DisplayName ?? "Unknown"} v{_applicationDetail?.Version ?? "1.0.0"}";
            return await _intuneService.CreateOrGetGroupAsync(displayName, fullDescription);
        }

        private async Task AssignGroupsToApplication(string appId, GroupAssignmentIds groupIds)
        {
            EnsureIntuneService();
            await _intuneService.AssignGroupsToApplicationAsync(appId, groupIds);
        }

        private void AddProgressStep(string icon, string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _progressSteps.Add(new ProgressStep { Icon = icon, Text = text });
            });
        }

        private void UpdateProgressStep(int index, string icon, string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (index >= 0 && index < _progressSteps.Count)
                {
                    _progressSteps[index].Icon = icon;
                    _progressSteps[index].Text = text;
                }
            });
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _intuneService?.Dispose();
                }
                _isDisposed = true;
            }
        }
    }

    // Supporting classes
    public class ProgressStep : System.ComponentModel.INotifyPropertyChanged
    {
        private string _icon = "";
        private string _text = "";

        public string Icon
        {
            get => _icon;
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    OnPropertyChanged(nameof(Text));
                }
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }

    public class UploadCompleteEventArgs : EventArgs
    {
        public string ApplicationId { get; set; } = "";
        public GroupAssignmentIds GroupIds { get; set; } = new();
    }
}