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
using System.Threading.Tasks;

namespace IntunePackagingTool
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Private Fields

        // Timer and Search
        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private string _currentSearchFilter = "";
        private string _currentCategoryFilter = "All Categories";

        // Application State
        private List<IntuneApplication> _allApplications = new List<IntuneApplication>();
        private ObservableCollection<IntuneApplication> _applications = new ObservableCollection<IntuneApplication>();
        private bool _applicationsLoaded = false;

        // Services
        private IntuneService? _intuneService;
        private PSADTGenerator? _psadtGenerator;
        private IntuneUploadService? _uploadService;
        private SettingsService? _settingsService;
        private BatchFileSigner? _batchSigner;
        private WDACToolsPage? _wdacToolsPage;


        // Pagination
        private int _currentPage = 1;
        private int PageSize = 50;
        private int _totalApplicationCount = 0;

        // Package Management
        private string _currentPackagePath = "";
        private string _generatedCatalogPath = "";
        private bool _catalogGenerated = false;

        #endregion

        #region Properties

        private int MaxPage
        {
            get
            {
                return _totalApplicationCount > 0 ? (int)Math.Ceiling((double)_totalApplicationCount / PageSize) : 1;
            }
        }

        public string DetectedPackageType
        {
            get
            {
                if (DetectedPackageTypeText.Text == "MSI Package") return "MSI";
                if (DetectedPackageTypeText.Text == "EXE Installer") return "EXE";
                return "Unknown";
            }
        }

        #endregion

        #region Constructor and Initialization

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();
            InitializeServices();
            InitializeUI();
           

        }

        private void InitializeTimer()
        {
            _searchTimer = new System.Windows.Threading.DispatcherTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(500);
            _searchTimer.Tick += SearchTimer_Tick;
        }

        private void InitializeServices()
        {
            try
            {
               
                _intuneService = new IntuneService();
               
            }
            catch (Exception ex)
            {
               
                MessageBox.Show($"Failed to initialize Intune Service: {ex.Message}", "Error", MessageBoxButton.OK);
                _intuneService = null;
            }

            _settingsService = new SettingsService();
            _psadtGenerator = new PSADTGenerator();

            if (_intuneService != null)
            {
                _uploadService = new IntuneUploadService(_intuneService);
            }

            _batchSigner = new BatchFileSigner(
                certificateName: "NBB Digital Workplace",
                certificateThumbprint: "B74452FD21BE6AD24CA9D61BCE156FD75E774716"
            );
        }

        private void InitializeUI()
        {
            ShowPage("CreateApplication");
            SetActiveNavButton(CreateAppNavButton);
            ApplicationsList.ItemsSource = _applications;
            LoadCategoriesDropdown();
            ApplicationDetailView.BackToListRequested += ApplicationDetailView_BackToListRequested;
            _wdacToolsPage = new WDACToolsPage();
            this.DataContext = this;

        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Any additional loading logic here
        }

        #endregion

        #region INotifyPropertyChanged Implementation

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Navigation

        private void CreateAppNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("CreateApplication");
            UpdateNavigation(CreateAppNavButton);
        }

        private async void ViewAppsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("ViewApplications");
            UpdateNavigation(ViewAppsNavButton);

            if (!_applicationsLoaded)
            {
                await LoadAllApplicationsAsync();
            }
            else
            {
                ApplyFiltersAndPagination();
            }
        }

        private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("Settings");
            LoadConfigurationDisplay();
            UpdateNavigation(SettingsNavButton);
        }

        private void ShowPage(string pageName)
        {
            // Hide all existing pages
            CreateApplicationPage.Visibility = Visibility.Collapsed;
            ViewApplicationsPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PaginationPanel.Visibility = Visibility.Collapsed;

            // Hide WDAC Tools if it exists
            if (_wdacToolsPage != null)
            {
                _wdacToolsPage.Visibility = Visibility.Collapsed;
            }

            switch (pageName)
            {
                case "CreateApplication":
                    CreateApplicationPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Create Package";
                    PageSubtitle.Text = "Build new application packages for Intune deployment";
                    break;

                case "ViewApplications":
                    ViewApplicationsPage.Visibility = Visibility.Visible;
                    PaginationPanel.Visibility = Visibility.Visible;
                    PageTitle.Text = "Applications";
                    PageSubtitle.Text = "Browse and manage Microsoft Intune applications";
                    SearchTextBox.Focus();
                    break;

                case "Settings":
                    SettingsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Settings";
                    PageSubtitle.Text = "Configure application settings and preferences";
                    break;

                case "WDACTools":
                    // Ensure the UserControl is added to the main content area
                    if (!ContentAreaGrid.Children.Contains(_wdacToolsPage))
                    {
                        ContentAreaGrid.Children.Add(_wdacToolsPage);
                    }
                    _wdacToolsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "WDAC Security Tools";
                    PageSubtitle.Text = "Generate and manage security catalogs";
                    break;
            }
        }

        private void UpdateNavigation(Button activeButton)
        {
            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");
            WDACToolsNavButton.Style = (Style)FindResource("SidebarNavButton");

            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }

        private void SetActiveNavButton(Button activeButton)
        {
            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");
            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }

        private void WDACToolsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage("WDACTools");
            UpdateNavigation(WDACToolsNavButton);
        }

        #endregion

        #region Application List Management

        private async Task LoadAllApplicationsAsync()
        {
            try
            {
                if (_intuneService == null) return;

                ShowStatus("Loading all applications from Microsoft Intune...");
                ShowProgress(true);

                _allApplications = await _intuneService.GetApplicationsAsync(forceRefresh: false);
                _applicationsLoaded = true;

                Debug.WriteLine($"Loaded {_allApplications.Count} applications into cache");

                _currentPage = 1;
                ApplyFiltersAndPagination();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading applications: {ex.Message}");
                ShowStatus($"Error: {ex.Message}");
                MessageBox.Show($"Failed to load applications:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private void ApplyFiltersAndPagination()
        {
            try
            {
                var filtered = _allApplications.AsEnumerable();

                // Apply search filter
                if (!string.IsNullOrEmpty(_currentSearchFilter))
                {
                    filtered = filtered.Where(app =>
                        app.DisplayName.Contains(_currentSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                        app.Publisher.Contains(_currentSearchFilter, StringComparison.OrdinalIgnoreCase));
                }

                // Apply category filter
                if (!string.IsNullOrEmpty(_currentCategoryFilter) && _currentCategoryFilter != "All Categories")
                {
                    filtered = filtered.Where(app =>
                        app.Category.Contains(_currentCategoryFilter, StringComparison.OrdinalIgnoreCase));
                }

                // Sort
                filtered = filtered.OrderBy(app => app.DisplayName);

                // Get total count after filtering
                var filteredList = filtered.ToList();
                _totalApplicationCount = filteredList.Count;

                // Apply pagination
                var skip = (_currentPage - 1) * PageSize;
                var pagedApps = filteredList.Skip(skip).Take(PageSize).ToList();

                // Update UI
                _applications.Clear();
                foreach (var app in pagedApps)
                {
                    _applications.Add(app);
                }

                // Update pagination controls
                int maxPage = _totalApplicationCount > 0 ? (int)Math.Ceiling((double)_totalApplicationCount / PageSize) : 1;

                PageDisplayText.Text = $"Page {_currentPage} of {maxPage}";
                PreviousPageButton.IsEnabled = _currentPage > 1;
                NextPageButton.IsEnabled = _currentPage < maxPage;

                ShowStatus($"Showing {pagedApps.Count} of {_totalApplicationCount} applications (Page {_currentPage} of {maxPage})");

                Debug.WriteLine($"Filter applied: {_totalApplicationCount} apps match, showing page {_currentPage}/{maxPage}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying filters: {ex.Message}");
                ShowStatus($"Error applying filters: {ex.Message}");
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

            CategoryFilter.SelectedIndex = 0;
        }

        #endregion

        #region Application List Event Handlers

        private void ApplicationsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Infinite scroll disabled - using pagination buttons instead
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
                        if (_intuneService == null)
                        {
                            MessageBox.Show("Intune service is not initialized", "Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
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
                        MessageBox.Show($"Error loading application details from Intune:\n\n{ex.Message}",
                            "Intune API Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

        #endregion

        #region Search and Filter Event Handlers

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchTimer.Stop();
            _currentSearchFilter = SearchTextBox.Text?.Trim() ?? "";
            _searchTimer.Start();
        }

        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchTimer.Stop();
            _currentPage = 1;

            if (_applicationsLoaded)
            {
                ApplyFiltersAndPagination();
            }
            else
            {
                await LoadAllApplicationsAsync();
            }
        }

        private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentCategoryFilter = CategoryFilter.SelectedItem?.ToString() ?? "All Categories";
            _currentPage = 1;

            if (_applicationsLoaded)
            {
                ApplyFiltersAndPagination();
            }
            else
            {
                await LoadAllApplicationsAsync();
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _applicationsLoaded = false;
            _currentPage = 1;
            await LoadAllApplicationsAsync();
        }

        #endregion

        #region Pagination Event Handlers

        private void NextPage_Click(object sender, RoutedEventArgs e)
        {
            var maxPage = _totalApplicationCount > 0 ?
                (int)Math.Ceiling((double)_totalApplicationCount / PageSize) : 1;

            if (_currentPage < maxPage)
            {
                _currentPage++;
                ApplyFiltersAndPagination();
            }
        }

        private void PreviousPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                ApplyFiltersAndPagination();
            }
        }

        private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (PageSizeCombo.SelectedItem is ComboBoxItem item &&
                int.TryParse(item.Tag.ToString(), out int newPageSize))
            {
                PageSize = newPageSize;
                _currentPage = 1;

                if (_applicationsLoaded)
                {
                    ApplyFiltersAndPagination();
                }
            }
        }

        #endregion

        #region Package Creation

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

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ProgressBar.Visibility = Visibility.Visible;
                StatusText.Text = "Generating package...";

                if (!ValidatePackageInputs()) return;

                var appInfo = CreateApplicationInfo();
                var psadtOptions = CollectPSADTOptions();

                var generator = new PSADTGenerator();
                string packagePath = await generator.CreatePackageAsync(appInfo, psadtOptions);
                _currentPackagePath = packagePath;

                ShowPackageSuccess(appInfo, psadtOptions);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating package: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Package generation failed";
            }
            finally
            {
                ProgressBar.Visibility = Visibility.Collapsed;
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

        #endregion

        #region WDAC CAT
        
        #endregion

        #region Package Creation Helper Methods

        private void DetectPackageType(string fileExtension, string fileName)
        {
            switch (fileExtension)
            {
                case ".msi":
                    DetectedPackageTypeIcon.Text = "📦";
                    DetectedPackageTypeText.Text = "MSI Package";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                    PSADTOptionsPanel.Visibility = Visibility.Visible;
                    AdvancedOptionsExpander.IsExpanded = true;
                    break;

                case ".exe":
                    DetectedPackageTypeIcon.Text = "⚙️";
                    DetectedPackageTypeText.Text = "EXE Installer";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219));
                    PSADTOptionsPanel.Visibility = Visibility.Visible;
                    AdvancedOptionsExpander.IsExpanded = true;
                    break;

                default:
                    DetectedPackageTypeIcon.Text = "❓";
                    DetectedPackageTypeText.Text = "Unknown file type";
                    DetectedPackageTypeText.Foreground = new SolidColorBrush(Color.FromRgb(241, 196, 15));
                    PSADTOptionsPanel.Visibility = Visibility.Collapsed;
                    break;
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
                Console.WriteLine($"Metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(filePath);
            }
        }

        private void ExtractMsiMetadata(string msiPath)
        {
            try
            {
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

                string productName = GetMsiProperty(database, "ProductName");
                string manufacturer = GetMsiProperty(database, "Manufacturer");
                string version = GetMsiProperty(database, "ProductVersion");

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

                string? productName = versionInfo.ProductName;
                string? companyName = versionInfo.CompanyName;
                string? version = versionInfo.ProductVersion ?? versionInfo.FileVersion;

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

            return productName
                .Replace(" Setup", "")
                .Replace(" Installer", "")
                .Replace(" Installation", "")
                .Trim();
        }

        private void ExtractMetadataFromFilename(string filePath)
        {
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

        private bool ValidatePackageInputs()
        {
            if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
            {
                MessageBox.Show("Please enter an Application Name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(SourcesPathTextBox.Text))
            {
                MessageBox.Show("Please select a Sources Path.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (DetectedPackageType == "Unknown")
            {
                MessageBox.Show("Please select a valid installer file (.msi or .exe).", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
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

        private ApplicationInfo CreateApplicationInfo()
        {
            return new ApplicationInfo
            {
                Name = AppNameTextBox.Text.Trim(),
                Manufacturer = string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) ? "Unknown" : ManufacturerTextBox.Text.Trim(),
                Version = string.IsNullOrWhiteSpace(VersionTextBox.Text) ? "1.0.0" : VersionTextBox.Text.Trim(),
                SourcesPath = SourcesPathTextBox.Text.Trim(),
                ServiceNowSRI = ""
            };
        }

        private PSADTOptions? CollectPSADTOptions()
        {
            if (!AdvancedOptionsExpander.IsExpanded || PSADTOptionsPanel.Visibility != Visibility.Visible)
                return null;

            return new PSADTOptions
            {
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

        private void ShowPackageSuccess(ApplicationInfo appInfo, PSADTOptions? psadtOptions)
        {
            PackageStatusPanel.Visibility = Visibility.Visible;
            PackageStatusText.Text = "✅ Package created successfully!";
            StatusText.Text = $"Package created successfully • {DateTime.Now:HH:mm:ss}";
            ProgressBar.IsIndeterminate = true;

            PackagePathText.Text = _currentPackagePath;
            OpenPackageFolderButton.Visibility = Visibility.Visible;

            int enabledFeatures = psadtOptions != null ? CountEnabledFeatures(psadtOptions) : 0;
            string featuresText = enabledFeatures > 0 ? $" with {enabledFeatures} PSADT cheatsheet functions" : "";

            MessageBox.Show($"Package '{appInfo.Manufacturer}_{appInfo.Name}' v{appInfo.Version} created successfully!\n\n" +
                           $"📦 Package Type: {DetectedPackageType}\n" +
                           $"⚙️ PSADT Features: {enabledFeatures} enabled\n" +
                           $"📁 Location: {_currentPackagePath}\n\n" +
                           $"The Deploy-Application.ps1 script has been generated{featuresText}.",
                           "Package Generation Complete",
                           MessageBoxButton.OK,
                           MessageBoxImage.Information);

            StatusText.Text = $"Package created successfully{featuresText} • {DateTime.Now:HH:mm:ss}";
        }

        private int CountEnabledFeatures(PSADTOptions options)
        {
            int count = 0;
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

        #endregion

        #region Package Actions Event Handlers

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

        private void btnRemoteTest_Click(object sender, RoutedEventArgs e)
        {
            string packagePath = _currentPackagePath;
            var remoteTest = new RemoteTestWindow(packagePath)
            {
                Owner = this
            };
            remoteTest.ShowDialog();
        }

        #endregion

        #region Settings Management

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

            // Get values from IntuneUploadService
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

                var tenantId = _intuneService.TenantId;
                var clientId = _intuneService.ClientId;

                MessageBox.Show($"IntuneService initialized successfully\nTenant: {tenantId}\nClient: {clientId}", "Debug Info");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"IntuneService diagnostic failed: {ex.Message}", "Debug Info");
            }
        }

        #endregion

        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _intuneService?.Dispose();
        }

        #endregion
    }
}