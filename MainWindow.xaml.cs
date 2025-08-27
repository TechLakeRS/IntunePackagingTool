using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool
{
    public partial class MainWindow : Window
    {
        private IntuneService _intuneService = new IntuneService();
        private PSADTGenerator _psadtGenerator = new PSADTGenerator();
        private ObservableCollection<IntuneApplication> _applications = new ObservableCollection<IntuneApplication>();
        private string _currentPackagePath = "";

        public MainWindow()
        {
            InitializeComponent();
            ApplicationsList.ItemsSource = _applications;
            LoadCategoriesDropdown();
            Loaded += async (s, e) => await LoadAllApplicationsAsync();

        }

        #region Navigation Methods
        private void DashboardNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Dashboard");
            UpdateNavigation(DashboardNavButton);
        }

        private void CreateAppNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("CreateApplication");
            UpdateNavigation(CreateAppNavButton);
        }

        private void ViewAppsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("ViewApplications");
            UpdateNavigation(ViewAppsNavButton);
        }

        private void HistoryNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("History");
            UpdateNavigation(HistoryNavButton);
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Settings");
            UpdateNavigation(SettingsNavButton);
        }

        private void ShowPage(string pageName)
        {
            // Hide all pages
            DashboardPage.Visibility = Visibility.Collapsed;
            CreateApplicationPage.Visibility = Visibility.Collapsed;
            ViewApplicationsPage.Visibility = Visibility.Collapsed;
            HistoryPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;

            // Show selected page and update header
            switch (pageName)
            {
                case "Dashboard":
                    DashboardPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Dashboard";
                    PageSubtitle.Text = "Application packaging overview and quick actions";
                    break;
                case "CreateApplication":
                    CreateApplicationPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Create Package";
                    PageSubtitle.Text = "Build new application packages for Intune deployment";
                    break;
                case "ViewApplications":
                    ViewApplicationsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Applications";
                    PageSubtitle.Text = "Browse and manage Microsoft Intune applications";
                    break;
                case "History":
                    HistoryPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Upload History";
                    PageSubtitle.Text = "View recent package uploads and deployment logs";
                    break;
                case "Settings":
                    SettingsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Settings";
                    PageSubtitle.Text = "Configure application settings and preferences";
                    break;
            }
        }

        private void UpdateNavigation(Button activeButton)
        {
            // Reset all nav buttons to normal style
            DashboardNavButton.Style = (Style)FindResource("SidebarNavButton");
            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            HistoryNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");

            // Set active button to active style
            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }
        #endregion

        #region Quick Action Methods
        private void QuickCreatePackage_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("CreateApplication");
            UpdateNavigation(CreateAppNavButton);
        }

        private void QuickBrowsePackages_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("ViewApplications");
            UpdateNavigation(ViewAppsNavButton);
        }

        private void QuickUploadToIntune_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentPackagePath))
            {
                MessageBox.Show("Please generate a package first by going to 'Create Package'.", "No Package",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                ShowPage("CreateApplication");
                UpdateNavigation(CreateAppNavButton);
                return;
            }

            UploadButton_Click(sender, e);
        }
        #endregion

        #region Application Loading Methods

        private async Task LoadAllApplicationsAsync()
        {
            try
            {
                ShowStatus("Loading applications from Intune...");
                ShowProgress(true);

                // Load ALL apps once
                var apps = await _intuneService.GetApplicationsAsync();

                _applications.Clear();
                foreach (var app in apps)
                {
                    _applications.Add(app);
                }

                ShowStatus($"Loaded {apps.Count} applications");
                ShowProgress(false);
            }
            catch (Exception ex)
            {
                ShowStatus("Failed to load applications");
                ShowProgress(false);
                MessageBox.Show($"Error loading applications: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadCategoriesDropdown()
        {
            CategoryFilter.Items.Clear();
            CategoryFilter.Items.Add("All Categories");
            CategoryFilter.Items.Add("Business");
            CategoryFilter.Items.Add("Hidden");
            CategoryFilter.Items.Add("OSD");
            CategoryFilter.Items.Add("Retired");
            CategoryFilter.Items.Add("Retired, Hidden");
            CategoryFilter.Items.Add("Technical");
            CategoryFilter.Items.Add("Test");
            CategoryFilter.Items.Add("Test, Business");
            CategoryFilter.Items.Add("Uncategorized");

            CategoryFilter.SelectedIndex = 0; // Default to "All Categories"
        }



        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterApplications(); // Just filter, don't reload
        }

        private void FilterApplications()
        {
            if (CategoryFilter.SelectedItem == null) return;

            var selectedCategory = CategoryFilter.SelectedItem.ToString();
            if (selectedCategory == "All Categories")
            {
                ApplicationsList.ItemsSource = _applications;
            }
            else
            {
                var filteredApps = _applications.Where(app =>
                    !string.IsNullOrWhiteSpace(app.Category) &&
                    app.Category.Equals(selectedCategory, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                ApplicationsList.ItemsSource = filteredApps;
            }
        }


        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAllApplicationsAsync(); // Reload ALL apps, then current filter applies
        }

        private async void DebugTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _intuneService.RunFullDebugTestAsync();

                if (!string.IsNullOrEmpty(_currentPackagePath))
                {
                    var intuneFolder = Path.Combine(_currentPackagePath, "Intune");
                    if (Directory.Exists(intuneFolder))
                    {
                        var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
                        if (intuneWinFiles.Length > 0)
                        {
                            MessageBox.Show("Found .intunewin file! Inspecting...", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                            IntuneWinDebugger.InspectIntuneWinFile(intuneWinFiles[0]);
                        }
                        else
                        {
                            MessageBox.Show("No .intunewin files found. Generate a package first.", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("No current package. Generate a package first to test .intunewin inspection.", "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Debug test failed: {ex.Message}", "Debug Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion

        #region Package Creation Methods
        private void OpenPackageFolderButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPackagePath))
                {
                    MessageBox.Show("No package path available.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(_currentPackagePath))
                {
                    MessageBox.Show("Package folder no longer exists.", "Folder Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{_currentPackagePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExtractFileMetadata(string filePath)
        {
            try
            {
                string extension = Path.GetExtension(filePath).ToLower();

                if (extension == ".msi")
                {
                    ExtractMsiMetadata(filePath);
                }
                else if (extension == ".exe")
                {
                    ExtractExeMetadata(filePath);
                }
            }
            catch (Exception ex)
            {
                // If metadata extraction fails, fall back to filename parsing
                Console.WriteLine($"Metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(filePath);
            }
        }

        private void ExtractMsiMetadata(string msiPath)
        {
            try
            {
                // Create Windows Installer object
                Type installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                dynamic installer = Activator.CreateInstance(installerType);
                dynamic database = installer.OpenDatabase(msiPath, 0);

                // Extract metadata
                string productName = GetMsiProperty(database, "ProductName");
                string manufacturer = GetMsiProperty(database, "Manufacturer");
                string version = GetMsiProperty(database, "ProductVersion");

                // Populate UI fields if they're empty
                if (string.IsNullOrWhiteSpace(AppNameTextBox.Text) && !string.IsNullOrWhiteSpace(productName))
                    AppNameTextBox.Text = productName;

                if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) && !string.IsNullOrWhiteSpace(manufacturer))
                    ManufacturerTextBox.Text = manufacturer;

                if (string.IsNullOrWhiteSpace(VersionTextBox.Text) && !string.IsNullOrWhiteSpace(version))
                    VersionTextBox.Text = version;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MSI metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(msiPath);
            }
        }

        private string GetMsiProperty(dynamic database, string property)
        {
            try
            {
                dynamic view = database.OpenView($"SELECT Value FROM Property WHERE Property = '{property}'");
                view.Execute();
                dynamic record = view.Fetch();
                return record?.StringData[1] ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void ExtractExeMetadata(string exePath)
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);

                // Extract metadata
                string productName = versionInfo.ProductName;
                string companyName = versionInfo.CompanyName;
                string version = versionInfo.ProductVersion ?? versionInfo.FileVersion;

                // Populate UI fields if they're empty
                if (string.IsNullOrWhiteSpace(AppNameTextBox.Text) && !string.IsNullOrWhiteSpace(productName))
                    AppNameTextBox.Text = CleanProductName(productName);

                if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) && !string.IsNullOrWhiteSpace(companyName))
                    ManufacturerTextBox.Text = companyName;

                if (string.IsNullOrWhiteSpace(VersionTextBox.Text) && !string.IsNullOrWhiteSpace(version))
                    VersionTextBox.Text = version;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXE metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(exePath);
            }
        }

        private string CleanProductName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return "";

            // Remove common installer/setup suffixes
            return productName
                .Replace(" Setup", "")
                .Replace(" Installer", "")
                .Replace(" Installation", "")
                .Trim();
        }

        private void ExtractMetadataFromFilename(string filePath)
        {
            // Your existing filename parsing logic as fallback
            if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
            {
                string appNameFromFile = Path.GetFileNameWithoutExtension(filePath);
                appNameFromFile = appNameFromFile.Replace("_setup", "")
                                               .Replace("Setup", "")
                                               .Replace("_installer", "")
                                               .Replace("Installer", "")
                                               .Replace("_install", "")
                                               .Replace("Install", "");
                AppNameTextBox.Text = appNameFromFile;
            }
        }

        private void BrowseSourcesButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Application Source File",
                Filter = "All Supported Files (*.msi;*.exe)|*.msi;*.exe|MSI Files (*.msi)|*.msi|EXE Files (*.exe)|*.exe|All Files (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFile = openFileDialog.FileName;
                string fileName = Path.GetFileName(selectedFile);
                string fileExtension = Path.GetExtension(selectedFile).ToLower();

                // Set the sources path to the selected FILE
                SourcesPathTextBox.Text = selectedFile;

                // Detect package type based on file extension
                DetectPackageType(fileExtension, fileName);

                // ✨ NEW: Extract metadata from the file
                ExtractFileMetadata(selectedFile);
            }
        }

        private void DetectPackageType(string fileExtension, string fileName)
        {
            switch (fileExtension)
            {
                case ".msi":
                    DetectedPackageTypeIcon.Text = "📦";
                    DetectedPackageTypeText.Text = "MSI Package";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Success green

                    // Show PSADT options and expand Advanced Options
                    PSADTOptionsPanel.Visibility = Visibility.Visible;
                    AdvancedOptionsExpander.IsExpanded = true;
                    break;

                case ".exe":
                    DetectedPackageTypeIcon.Text = "⚙️";
                    DetectedPackageTypeText.Text = "EXE Installer";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)); // Primary blue

                    // Show PSADT options and expand Advanced Options
                    PSADTOptionsPanel.Visibility = Visibility.Visible;
                    AdvancedOptionsExpander.IsExpanded = true;
                    break;

                default:
                    DetectedPackageTypeIcon.Text = "❓";
                    DetectedPackageTypeText.Text = "Unknown file type";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15)); // Warning yellow

                    // Hide PSADT options for unknown types
                    PSADTOptionsPanel.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        // Optional: Property to get the detected package type for use in generation logic
        public string DetectedPackageType
        {
            get
            {
                if (DetectedPackageTypeText.Text == "MSI Package") return "MSI";
                if (DetectedPackageTypeText.Text == "EXE Installer") return "EXE";
                return "Unknown";
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Show progress
                ProgressBar.Visibility = Visibility.Visible;
                StatusText.Text = "Generating package...";

                // Validate inputs
                if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
                {
                    MessageBox.Show("Please enter an Application Name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(SourcesPathTextBox.Text))
                {
                    MessageBox.Show("Please select a Sources Path.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (DetectedPackageType == "Unknown")
                {
                    MessageBox.Show("Please select a valid installer file (.msi or .exe).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create ApplicationInfo object
                var appInfo = new ApplicationInfo
                {
                    Name = AppNameTextBox.Text.Trim(),
                    Manufacturer = string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) ? "Unknown" : ManufacturerTextBox.Text.Trim(),
                    Version = string.IsNullOrWhiteSpace(VersionTextBox.Text) ? "1.0.0" : VersionTextBox.Text.Trim(),
                    SourcesPath = SourcesPathTextBox.Text.Trim(),
                    ServiceNowSRI = "" // You can add this field to your UI if needed
                };

                // Collect PSADT options from UI checkboxes (only if Advanced Options are expanded and visible)
                PSADTOptions psadtOptions = null;

                if (AdvancedOptionsExpander.IsExpanded && PSADTOptionsPanel.Visibility == Visibility.Visible)
                {
                    psadtOptions = new PSADTOptions
                    {
                        // Package Info
                        PackageType = DetectedPackageType,

                        // Installation Options
                        SilentInstall = SilentInstallCheck?.IsChecked ?? false,
                        SuppressRestart = SuppressRestartCheck?.IsChecked ?? false,
                        AllUsersInstall = AllUsersInstallCheck?.IsChecked ?? false,
                        VerboseLogging = VerboseLoggingCheck?.IsChecked ?? false,

                        // User Interaction
                        CloseRunningApps = CloseRunningAppsCheck?.IsChecked ?? false,
                        AllowUserDeferrals = AllowUserDeferralsCheck?.IsChecked ?? false,
                        CheckDiskSpace = CheckDiskSpaceCheck?.IsChecked ?? false,
                        ShowProgress = ShowProgressCheck?.IsChecked ?? false,

                        // Prerequisites
                        CheckDotNet = CheckDotNetCheck?.IsChecked ?? false,
                        ImportCertificates = ImportCertificatesCheck?.IsChecked ?? false,
                        CheckVCRedist = CheckVCRedistCheck?.IsChecked ?? false,
                        RegisterDLLs = RegisterDLLsCheck?.IsChecked ?? false,

                        // File & Registry Operations
                        CopyToAllUsers = CopyToAllUsersCheck?.IsChecked ?? false,
                        SetHKCUAllUsers = SetHKCUAllUsersCheck?.IsChecked ?? false,
                        SetCustomRegistry = SetCustomRegistryCheck?.IsChecked ?? false,
                        CopyConfigFiles = CopyConfigFilesCheck?.IsChecked ?? false,

                        // Shortcuts & Cleanup
                        DesktopShortcut = DesktopShortcutCheck?.IsChecked ?? false,
                        StartMenuEntry = StartMenuEntryCheck?.IsChecked ?? false,
                        RemovePreviousVersions = RemovePreviousVersionsCheck?.IsChecked ?? false,
                        CreateInstallMarker = CreateInstallMarkerCheck?.IsChecked ?? false
                    };
                }

                // Use your existing PSADTGenerator
                var generator = new PSADTGenerator();
                string packagePath = await generator.CreatePackageAsync(appInfo, psadtOptions);
                _currentPackagePath = packagePath;

                // Show success and update UI
                PackageStatusPanel.Visibility = Visibility.Visible;
                PackageStatusText.Text = "✅ Package created successfully!";
                PackagePathText.Text = packagePath;
                OpenPackageFolderButton.Visibility = Visibility.Visible;

                // Count enabled features
                int enabledFeatures = psadtOptions != null ? CountEnabledFeatures(psadtOptions) : 0;
                string featuresText = enabledFeatures > 0 ? $" with {enabledFeatures} PSADT cheatsheet functions" : "";

                MessageBox.Show($"Package '{appInfo.Manufacturer}_{appInfo.Name}' v{appInfo.Version} created successfully!\n\n" +
                               $"📦 Package Type: {DetectedPackageType}\n" +
                               $"⚙️ PSADT Features: {enabledFeatures} enabled\n" +
                               $"📁 Location: {packagePath}\n\n" +
                               $"The Deploy-Application.ps1 script has been generated{featuresText}.",
                               "Package Generation Complete",
                               MessageBoxButton.OK,
                               MessageBoxImage.Information);

                StatusText.Text = $"Package created successfully{featuresText} • {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating package: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Package generation failed";
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private int CountEnabledFeatures(PSADTOptions options)
        {
            int count = 0;

            // Use reflection to count enabled boolean properties
            var properties = typeof(PSADTOptions).GetProperties();
            foreach (var prop in properties)
            {
                if (prop.PropertyType == typeof(bool) && (bool)prop.GetValue(options))
                {
                    count++;
                }
            }

            return count;
        }

        private void OpenScriptButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPackagePath))
                {
                    MessageBox.Show("Please generate a package first.", "No Package",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var scriptPath = Path.Combine(_currentPackagePath, "Application", "Deploy-Application.ps1");

                if (!File.Exists(scriptPath))
                {
                    MessageBox.Show("Script file not found.", "File Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell_ise.exe",
                    Arguments = $"\"{scriptPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening script: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentPackagePath))
                {
                    MessageBox.Show("Please generate a package first.", "No Package",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var uploadWindow = new UploadToIntuneWindow
                {
                    Owner = this,
                    ApplicationInfo = new ApplicationInfo
                    {
                        Manufacturer = ManufacturerTextBox.Text.Trim(),
                        Name = AppNameTextBox.Text.Trim(),
                        Version = VersionTextBox.Text.Trim()
                    },
                    PackagePath = _currentPackagePath
                };

                uploadWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening upload window: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateInput()
        {
            if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text))
            {
                MessageBox.Show("Please enter a manufacturer name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ManufacturerTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
            {
                MessageBox.Show("Please enter an application name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                AppNameTextBox.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(VersionTextBox.Text))
            {
                MessageBox.Show("Please enter a version number.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                VersionTextBox.Focus();
                return false;
            }

            return true;
        }
        #endregion

        #region Utility Methods
        private void ShowStatus(string message)
        {
            StatusText.Text = message;
        }

        private void ShowProgress(bool show)
        {
            ProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                ProgressBar.IsIndeterminate = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _intuneService?.Dispose();
        }
        #endregion
    }
}