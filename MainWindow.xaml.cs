using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;



namespace IntunePackagingTool
{
    public partial class MainWindow : Window, INotifyPropertyChanged

    {
        private System.Windows.Threading.DispatcherTimer _searchTimer;
        private bool _isInitialized = false;
        private IntuneService? _intuneService;
        private PSADTGenerator _psadtGenerator;
        private IntuneUploadService? _uploadService;
        private ObservableCollection<IntuneApplication> _applications = new ObservableCollection<IntuneApplication>();
        private string _currentPackagePath = "";
        private SettingsService _settingsService;
        private BatchFileSigner? _batchSigner;
        private int _currentPage = 1;
        private int PageSize = 50;
        private bool _isLoading = false;
        private string _currentSearchFilter = "";
        private string _currentCategoryFilter = "All Categories";
        public int CurrentPage => _currentPage;
        public string CurrentPageDisplay => $"Page {_currentPage} of {MaxPage}";
        public bool CanGoToPreviousPage => _currentPage > 1;
        public bool CanGoToNextPage => _currentPage < MaxPage;

        private int MaxPage
        {
            get
            {
                var filteredCount = GetFilteredApplications().Count();
                return filteredCount > 0 ? (int)Math.Ceiling((double)filteredCount / PageSize) : 1;
            }
        }
        private CancellationTokenSource? _loadCancellation;

        public MainWindow()
        {
            InitializeComponent();
            _searchTimer = new System.Windows.Threading.DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchTimer.Tick += SearchTimer_Tick;
            ShowPage("CreateApplication");
            SetActiveNavButton(CreateAppNavButton);
            Debug.WriteLine("=== STARTING INTUNE SERVICE INITIALIZATION ===");

            try
            {
                Debug.WriteLine("About to create IntuneService...");
                _intuneService = new IntuneService();
                Debug.WriteLine($"IntuneService created successfully: {_intuneService != null}");

                if (_intuneService != null)
                {
                    Debug.WriteLine($"TenantId: {_intuneService.TenantId}");
                    Debug.WriteLine($"ClientId: {_intuneService.ClientId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CONSTRUCTOR EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Constructor failed: {ex.Message}", "Debug", MessageBoxButton.OK);
                _intuneService = null;
            }

            Debug.WriteLine($"Final _intuneService state: {_intuneService != null}");
            Debug.WriteLine("=== END INTUNE SERVICE INITIALIZATION ===");

            ApplicationsList.ItemsSource = _applications;
            LoadCategoriesDropdown();
            ApplicationDetailView.BackToListRequested += ApplicationDetailView_BackToListRequested;
            _settingsService = new SettingsService();
            _psadtGenerator = new PSADTGenerator();
            if (_intuneService != null)
            {
                _uploadService = new IntuneUploadService(_intuneService);
            }
           
            _batchSigner = new BatchFileSigner(certificateName: "NBB Digital Workplace",certificateThumbprint: "B74452FD21BE6AD24CA9D61BCE156FD75E774716");
            this.DataContext = this;
            DiagnoseIntuneService();
            _isInitialized = true;

        }

        #region Navigation Methods

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Now it's safe to load data that might show progress/status
            if (ViewApplicationsPage.Visibility == Visibility.Visible)
            {
                await LoadApplicationsPagedAsync();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Settings");
            LoadConfigurationDisplay();
            UpdateNavigation(SettingsNavButton);
        }

        private void ShowPage(string pageName)
        {
            CreateApplicationPage.Visibility = Visibility.Collapsed;
            ViewApplicationsPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PaginationPanel.Visibility = Visibility.Collapsed; // Hide by default

            switch (pageName)
            {
                case "CreateApplication":
                    CreateApplicationPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Create Package";
                    PageSubtitle.Text = "Build new application packages for Intune deployment";
                    break;
                case "ViewApplications":
                    ViewApplicationsPage.Visibility = Visibility.Visible;
                    PaginationPanel.Visibility = Visibility.Visible; // Show pagination
                    PageTitle.Text = "Applications";
                    PageSubtitle.Text = "Browse and manage Microsoft Intune applications";
                    SearchTextBox.Focus();
                    break;
                case "Settings":
                    SettingsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Settings";
                    PageSubtitle.Text = "Configure application settings and preferences";
                    break;
            }
        }

        private void btnRemoteTest_Click(object sender, RoutedEventArgs e)
        {
            string packagePath = _currentPackagePath;
            var remoteTest = new RemoteTestWindow(packagePath)
            {
                Owner = this
            };
            remoteTest.ShowDialog();
        }



        private void LoadConfigurationDisplay()
        {
            // Get values from IntuneService
            if (_intuneService != null)
            {
                TenantIdDisplay.Text = _intuneService.TenantId;
                ClientIdDisplay.Text = _intuneService.ClientId;
                ThumbprintDisplay.Text = _intuneService.CertificateThumbprint;
            }

            // Get values from PSADTGenerator
            if (_psadtGenerator != null)
            {
                PSADTPathDisplay.Text = _psadtGenerator.TemplatePath;
                OutputPathDisplay.Text = _psadtGenerator.BaseOutputPath;
            }

            // Get values from IntuneUploadService (fixed underscore)
            if (_uploadService != null)
            {
                
                UtilPathDisplay.Text = _uploadService.ConverterPath;
            }
        }


        private void ShowSettingsPage()
        {
            
            CreateApplicationPage.Visibility = Visibility.Collapsed;
            LoadConfigurationDisplay();
            SettingsPage.Visibility = Visibility.Visible;
        }
        private async void ApplicationsList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ApplicationsList.SelectedItem != null)
            {
                var selectedApp = ApplicationsList.SelectedItem as IntuneApplication;

                if (selectedApp != null)
                {
                    try
                    {
                       
                        StatusText.Text = $"Loading details for {selectedApp.DisplayName} from Microsoft Intune...";
                        ProgressBar.Visibility = Visibility.Visible;
                        ProgressBar.IsIndeterminate = true;
                        var appDetail = await _intuneService.GetApplicationDetailAsync(selectedApp.Id);
                        ProgressBar.Visibility = Visibility.Collapsed;

                        if (appDetail != null)
                        {
                            
                            ApplicationsListPanel.Visibility = Visibility.Collapsed;
                            ApplicationDetailView.Visibility = Visibility.Visible;
                            ApplicationDetailView.LoadApplicationDetail(appDetail);

                            
                            PageTitle.Text = $"Application Details: {selectedApp.DisplayName}";
                            PageSubtitle.Text = "Live data from Microsoft Intune";
                            StatusText.Text = $"Loaded live details for {selectedApp.DisplayName} from Intune";
                        }
                    }
                    catch (Exception ex)
                    {
                        ProgressBar.Visibility = Visibility.Collapsed;
                        StatusText.Text = "Error loading application details";
                        MessageBox.Show($"Error loading application details from Intune:\n\n{ex.Message}", "Intune API Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }


        private void ApplicationDetailView_BackToListRequested(object? sender, EventArgs e)
        {

            ApplicationDetailView.Visibility = Visibility.Collapsed;
            ApplicationsListPanel.Visibility = Visibility.Visible;
            PageTitle.Text = "Microsoft Intune Applications";
            PageSubtitle.Text = "Browse and manage your application packages";
        }




        private void UpdateNavigation(Button activeButton)
        {
            // Reset all nav buttons to normal style

            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");
            // Set active button to active style
            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }

      
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Stop the timer if it's running
            _searchTimer.Stop();

            // Store the search text
            _currentSearchFilter = SearchTextBox.Text?.Trim() ?? ""; 

            // Start the timer
            _searchTimer.Start();
        }


      
        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            _currentPage = 1; 
            await LoadApplicationsPagedAsync(); 
        }
        #endregion


        #region Application Loading Methods
       
        private List<IntuneApplication> _allApplications = new List<IntuneApplication>();

        private async Task LoadApplicationsPagedAsync()
        {
            try
            {
                if (_isLoading) return;

                // Check if initialization is complete
                if (!_isInitialized)
                {
                    Debug.WriteLine("LoadApplicationsPagedAsync called before initialization complete - skipping");
                    return;
                }

                if (_intuneService == null)
                {
                    Debug.WriteLine("_intuneService is null after initialization - this should not happen");
                    ShowStatus("IntuneService not available");
                    MessageBox.Show("IntuneService is not initialized. Please restart the application.", "Service Error");
                    return;
                }

                _isLoading = true;
                ShowStatus($"Loading applications (page {_currentPage})...");
                ShowProgress(true);

                // Apply filters to the full dataset
                var filteredApps = GetFilteredApplications();

                // Convert to 0-based for Skip calculation
                var skipCount = (_currentPage - 1) * PageSize;

                // Get current page from filtered results
                var pagedApps = filteredApps
                    .Skip(skipCount)
                    .Take(PageSize);

                // Update UI
                _applications.Clear();
                foreach (var app in pagedApps)
                {
                    _applications.Add(app);
                }

                var totalFiltered = filteredApps.Count();
                ShowStatus($"Showing page {_currentPage} of {MaxPage} ({_applications.Count} of {totalFiltered} applications)");

                // Notify property changed for pagination buttons
                OnPropertyChanged(nameof(CurrentPageDisplay));
                OnPropertyChanged(nameof(CanGoToPreviousPage));
                OnPropertyChanged(nameof(CanGoToNextPage));
            }
            finally
            {
                _isLoading = false;
                ShowProgress(false);
            }
        }



        private async Task LoadMoreApplicationsAsync()
        {
            if (_isLoading) return;

            _currentPage++;
            await LoadApplicationsPagedAsync();
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

        private void SetActiveNavButton(Button activeButton)
        {
            // Reset all navigation buttons to inactive style

            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");

            // Set the active button to active style
            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }

        private bool _initialLoadComplete = false;

        private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentCategoryFilter = CategoryFilter.SelectedItem?.ToString() ?? "All Categories";
            _currentPage = 1; 
            await LoadApplicationsPagedAsync(); 
        }

        private IEnumerable<IntuneApplication> GetFilteredApplications()
        {
            var filtered = _allApplications.AsEnumerable();

            // Search filter
            if (!string.IsNullOrEmpty(_currentSearchFilter))
            {
                filtered = filtered.Where(app =>
                    app.DisplayName.Contains(_currentSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                    app.Publisher.Contains(_currentSearchFilter, StringComparison.OrdinalIgnoreCase));
            }

            // Category filter
            if (_currentCategoryFilter != "All Categories")
            {
                filtered = filtered.Where(app => app.Category == _currentCategoryFilter);
            }

            return filtered;
        }



        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _allApplications.Clear();
            _currentPage = 1; 
            await LoadApplicationsPagedAsync();
        }

        private async void PageSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (PageSizeCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag.ToString(), out int newPageSize))
            {
                PageSize = newPageSize;
                _currentPage = 1; // Reset to first page
                await LoadApplicationsPagedAsync();
            }
        }
        private async void ApplicationsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Infinite scroll disabled - using pagination buttons instead
        }

        
        private async void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var filteredCount = GetFilteredApplications().Count();
            var maxPage = (int)Math.Ceiling((double)filteredCount / PageSize) - 1;

            if (_currentPage < maxPage)
            {
                _currentPage++;
                await LoadApplicationsPagedAsync();
            }
        }

        private async void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 0)
            {
                _currentPage--;
                await LoadApplicationsPagedAsync();
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
                Type? installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                if (installerType == null)
                {
                    Console.WriteLine("Windows Installer not available");
                    ExtractMetadataFromFilename(msiPath);
                    return;
                }
                dynamic? installer = Activator.CreateInstance(installerType);
                if (installer == null)
                {
                    Console.WriteLine("Failed to create Windows Installer instance");
                    ExtractMetadataFromFilename(msiPath);
                    return;
                }
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
                string? productName = versionInfo.ProductName;
                string? companyName = versionInfo.CompanyName;
                string? version = versionInfo.ProductVersion ?? versionInfo.FileVersion;

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

                
                SourcesPathTextBox.Text = selectedFile;
                DetectPackageType(fileExtension, fileName);
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
                PSADTOptions? psadtOptions = null;

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
                StatusText.Text = $"Package created successfully • {DateTime.Now:HH:mm:ss}";
                ProgressBar.IsIndeterminate = true;


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
                if (prop.PropertyType == typeof(bool) &&
                    prop.GetValue(options) is bool value &&
                    value)
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
                    FileName = "C:\\Program Files\\Microsoft VS Code\\Code.exe",
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


                var uploadWizard = new IntuneUploadWizard
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


                uploadWizard.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening upload wizard: {ex.Message}", "Error",
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
            if (StatusText != null)
            {
                StatusText.Text = message;
            }
        }

        private void ShowProgress(bool show)
        {
            if (ProgressBar != null)
            {
                ProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    ProgressBar.IsIndeterminate = true;
                }
            }
        }
        private void DiagnoseIntuneService()
        {
            try
            {
                if (_intuneService == null)
                {
                    MessageBox.Show("IntuneService is null after initialization attempt", "Debug Info");
                    return;
                }

                // Test basic properties
                var tenantId = _intuneService.TenantId;
                var clientId = _intuneService.ClientId;

                MessageBox.Show($"IntuneService initialized successfully\nTenant: {tenantId}\nClient: {clientId}", "Debug Info");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"IntuneService diagnostic failed: {ex.Message}", "Debug Info");
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