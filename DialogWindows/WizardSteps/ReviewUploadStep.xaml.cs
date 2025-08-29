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

namespace IntunePackagingTool
{
    public partial class ReviewUploadStep : UserControl, IUploadProgress
    {
        public event EventHandler BackRequested;
        public event EventHandler<UploadCompleteEventArgs> UploadComplete;
        public ApplicationInfo ApplicationInfo { get; set; }
        public ObservableCollection<DetectionRule> DetectionRules { get; set; }
        public string PackagePath { get; set; }
        public delegate void UploadRequestedEventHandler(string installCommand, string uninstallCommand, string description, string installContext);
        public event UploadRequestedEventHandler UploadRequested;

        private ApplicationDetail _applicationDetail;
        public string? SelectedIconPath { get; set; }
        private string? _selectedIconPath;  // ADD THIS LINE
        private IntuneService _intuneService;
        private ObservableCollection<ProgressStep> _progressSteps;

        public ReviewUploadStep()
        {
            InitializeComponent();
            _progressSteps = new ObservableCollection<ProgressStep>();
            ProgressSteps.ItemsSource = _progressSteps;
            DetectionRules = new ObservableCollection<DetectionRule>();
        }

        
        public void UpdateSummaryData(string installCommand, string uninstallCommand, string description, string installContext)
        {
            // Update the application detail with the provided data
            if (_applicationDetail == null)
            {
                _applicationDetail = new ApplicationDetail();
            }

            _applicationDetail.InstallCommand = installCommand;
            _applicationDetail.UninstallCommand = uninstallCommand;
            _applicationDetail.Description = description;
            _applicationDetail.InstallContext = installContext;

            
            RefreshUI();
        }


        // Use ApplicationDetail which has DetectionRules
        public void LoadApplication(ApplicationDetail appDetail)
        {
            _applicationDetail = appDetail;
            _intuneService = new IntuneService();

            // Populate application details
            AppNameText.Text = appDetail.DisplayName;
            VersionText.Text = appDetail.Version;
            PublisherText.Text = appDetail.Publisher;
            PackageSizeText.Text = appDetail.SizeFormatted;

            // Show detection rules from ApplicationDetail
            if (appDetail.DetectionRules != null && appDetail.DetectionRules.Any())
            {
                var detectionRulesList = appDetail.DetectionRules.Select(r =>
                    $"• {GetDetectionRuleTitle(r.Type.ToString())}: {r.Path}").ToList();
                DetectionRulesList.ItemsSource = detectionRulesList;
            }
            else
            {
                DetectionRulesList.ItemsSource = new[] { "• No detection rules configured" };
            }

            // Generate and display group names
            GenerateGroupNames();
        }

        // Alternative: Create from ApplicationInfo
        public void LoadFromApplicationInfo(ApplicationInfo appInfo)
        {
            ApplicationInfo = appInfo;

            // Create ApplicationDetail from ApplicationInfo
            _applicationDetail = new ApplicationDetail
            {
                DisplayName = appInfo.Name,
                Version = appInfo.Version,
                Publisher = appInfo.Manufacturer,
                InstallContext = appInfo.InstallContext,
                DetectionRules = DetectionRules?.ToList() ?? new List<DetectionRule>()
            };

            _intuneService = new IntuneService();
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (_applicationDetail == null) return;

            // Update UI elements
            AppNameText.Text = _applicationDetail.DisplayName;
            VersionText.Text = _applicationDetail.Version;
            PublisherText.Text = _applicationDetail.Publisher;
            InstallContextText.Text = _applicationDetail.InstallContext;
            DescriptionText.Text = _applicationDetail.Description;
            InstallCommandText.Text = _applicationDetail.InstallCommand;
            UninstallCommandText.Text = _applicationDetail.UninstallCommand;

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
            else
            {
                PackageSizeText.Text = _applicationDetail.SizeFormatted;
            }

            // Show detection rules
            if (DetectionRules != null && DetectionRules.Any())
            {
                var detectionRulesList = DetectionRules.Select(r =>
                    $"• {GetDetectionRuleTitle(r.Type.ToString())}: {r.Path}").ToList();
                DetectionRulesList.ItemsSource = detectionRulesList;
            }
            else if (_applicationDetail.DetectionRules != null && _applicationDetail.DetectionRules.Any())
            {
                var detectionRulesList = _applicationDetail.DetectionRules.Select(r =>
                    $"• {GetDetectionRuleTitle(r.Type.ToString())}: {r.Path}").ToList();
                DetectionRulesList.ItemsSource = detectionRulesList;
            }
            else
            {
                DetectionRulesList.ItemsSource = new[] { "• No detection rules configured" };
            }

            GenerateGroupNames();
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

            // Replace spaces and any non-alphanumeric characters (except dash and dot) with underscores
            var cleaned = Regex.Replace(input, @"[^a-zA-Z0-9\-\.]", "_");

            // Remove multiple consecutive underscores
            cleaned = Regex.Replace(cleaned, @"_{2,}", "_");

            // Trim underscores from start and end
            cleaned = cleaned.Trim('_');

            // Limit length
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
            if (bytes > 1024 * 1024 * 1024)
                return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
            if (bytes > 1024 * 1024)
                return $"{bytes / (1024.0 * 1024):F1} MB";
            if (bytes > 1024)
                return $"{bytes / 1024.0:F1} KB";
            return $"{bytes} bytes";
        }

      
        public async Task<bool> StartUploadAsync()
        {
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
                MessageBox.Show($"Upload failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                AddProgressStep("❌", $"Upload failed: {ex.Message}");
                return false;
            }
        }

        public void UpdateProgress(int percentage, string message)
{
    // Call the overloaded version with default chunk values
    UpdateProgress(percentage, message, 0, 0);
}

        public void UpdateProgress(int percentage, string message, int currentChunk = 0, int totalChunks = 0)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Show panel if hidden
                if (ProgressPanel.Visibility != Visibility.Visible)
                    ProgressPanel.Visibility = Visibility.Visible;

                // Update progress bar
                UploadProgressBar.Value = percentage;

                // Update percentage text
                ProgressPercentageText.Text = $"{percentage}%";

                // Update chunk status
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
            // Use the same upload service as the main wizard
            var uploadService = new IntuneUploadService(_intuneService);

            // Use ApplicationInfo if set, otherwise create from _applicationDetail
            var appInfo = ApplicationInfo ?? new ApplicationInfo
            {
                Name = _applicationDetail.DisplayName,
                Version = _applicationDetail.Version,
                Manufacturer = _applicationDetail.Publisher,
                InstallContext = _applicationDetail.InstallContext,
                SourcesPath = PackagePath ?? ""
            };

            // Get commands from _applicationDetail
            var installCommand = _applicationDetail?.InstallCommand ?? "Deploy-Application.exe -DeploymentType Install";
            var uninstallCommand = _applicationDetail?.UninstallCommand ?? "Deploy-Application.exe -DeploymentType Uninstall";
            var description = _applicationDetail?.Description ?? "";
            var installContext = _applicationDetail?.InstallContext ?? "System";
            

            // Combine detection rules from both sources
            var allDetectionRules = new List<DetectionRule>();
            if (DetectionRules != null) allDetectionRules.AddRange(DetectionRules);
            if (_applicationDetail?.DetectionRules != null) allDetectionRules.AddRange(_applicationDetail.DetectionRules);

            var progressReporter = new Progress<(int percentage, string message)>(report =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // Update or add progress steps based on the message
                    if (report.message.Contains("chunk"))
                    {
                        UpdateProgressStep(0, "📦", report.message);
                    }
                    else if (report.message.Contains("Committing"))
                    {
                        AddProgressStep("⏳", report.message);
                    }
                    // Update a progress bar if you have one
                    // ProgressBar.Value = report.percentage;
                });
            });
            // Use the actual upload service that works
            return await uploadService.UploadWin32ApplicationAsync(
                appInfo,
                PackagePath,
                allDetectionRules.Distinct().ToList(),
                installCommand,
                uninstallCommand,
                description,
                installContext,
                iconPath,
                this // Pass 'this' if ReviewUploadStep implements IUploadProgress for progress updates
            );
        }

        private async Task<GroupAssignmentIds> CreateAssignmentGroups()
        {
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
            var fullDescription = $"{description} - {_applicationDetail.DisplayName} v{_applicationDetail.Version}";
            return await _intuneService.CreateOrGetGroupAsync(displayName, fullDescription);
        }

        private async Task AssignGroupsToApplication(string appId, GroupAssignmentIds groupIds)
        {
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
                if (index < _progressSteps.Count)
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
    }

    // Supporting classes remain the same...
    public class ProgressStep : System.ComponentModel.INotifyPropertyChanged
    {
        private string _icon;
        private string _text;

        public string Icon
        {
            get => _icon;
            set
            {
                _icon = value;
                OnPropertyChanged(nameof(Icon));
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged(nameof(Text));
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
        public string ApplicationId { get; set; }
        public GroupAssignmentIds GroupIds { get; set; }
    }
}