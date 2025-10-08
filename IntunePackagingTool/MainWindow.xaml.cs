using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using IntunePackagingTool.Views;
using IntunePackagingTool.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IntunePackagingTool
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        
        #region Constants

        private const int DefaultPageSize = 50;
        private const int SearchDebounceMilliseconds = 500;
        private const string PageNameCreateApplication = "CreateApplication";
        private const string PageNameUploadExisting = "UploadExisting";
        private const string PageNameViewApplications = "ViewApplications";
        private const string PageNameSettings = "Settings";
        private const string PageNameLogs = "Logs";
        private const string PageNameWDACTools = "WDACTools";
        private const string AllCategoriesFilter = "All Categories";

        #endregion

        #region Private Fields

        // Timer and Search
        private System.Windows.Threading.DispatcherTimer? _searchTimer;
        private string _currentSearchFilter = "";
        private string _currentCategoryFilter = AllCategoriesFilter;

        // Application State
        private List<IntuneApplication> _allApplications = new List<IntuneApplication>();
        private ObservableCollection<IntuneApplication> _applications = new ObservableCollection<IntuneApplication>();
        private bool _applicationsLoaded = false;
        private DateTime? _lastSyncTime = null;

        // Services
        private IntuneService? _intuneService;
        private PSADTGenerator? _psadtGenerator;
        private IntuneUploadService? _uploadService;
        private SettingsService? _settingsService;
        private BatchFileSigner? _batchSigner;
        private WDACToolsPage? _wdacToolsPage;
        private MsiInfoService.MsiInfo? _currentMsiInfo = null;
        private LogsView? _logsView;



        // Pagination
        private int _currentPage = 1;
        private int PageSize = DefaultPageSize;
        private int _totalApplicationCount = 0;

        // Package Management
        private string _currentPackagePath = "";
        private PSADTOptions? _currentPSADTOptions = null;
        private string _detectedPackageType = "";
        private string _msiProductCode = "";
        private string _msiProductVersion = "";
        private string _existingPackageFolder = "";
        private string _selectedIconPath = "";
        private ObservableCollection<DetectionRule> _existingDetectionRules = new ObservableCollection<DetectionRule>();
        #endregion

        #region Properties
        public IntuneService? GetIntuneService()
        {
            return _intuneService;
        }

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
            private set
            {
                // Store the value if needed
                _detectedPackageType = value;
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
            _searchTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMilliseconds);
            _searchTimer.Tick += SearchTimer_Tick;
        }

        private void InitializeServices()
        {
            try
            {
                // Load settings first
                _settingsService = new SettingsService();
                var settings = _settingsService.Settings;

                // Initialize Intune service with settings
                _intuneService = new IntuneService(
                    settings.Authentication.TenantId,
                    settings.Authentication.ClientId,
                    settings.Authentication.CertificateThumbprint
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize Intune Service: {ex.Message}", "Error", MessageBoxButton.OK);
                _intuneService = null;
            }

            _psadtGenerator = new PSADTGenerator();

            if (_intuneService != null)
            {
                _uploadService = new IntuneUploadService(_intuneService);
            }

            // BatchFileSigner now loads settings automatically from appsettings.json
            _batchSigner = new BatchFileSigner();
        }

        private void InitializeUI()
        {
            ShowPage(PageNameCreateApplication);
            SetActiveNavButton(CreateAppNavButton);
            ApplicationsList.ItemsSource = _applications;
            LoadCategoriesDropdown();
            ApplicationDetailView.BackToListRequested += ApplicationDetailView_BackToListRequested;
            _wdacToolsPage = new WDACToolsPage();

            this.DataContext = this;

            // Set current user
            SetCurrentUser();
        }

        private void SetCurrentUser()
        {
            try
            {
                string userName = Environment.UserName;
                string domainUser = Environment.UserDomainName + "\\" + userName;

                // Set display name
                UserNameDisplay.Text = userName;

                // Set initials (first 2 letters of username)
                if (!string.IsNullOrEmpty(userName))
                {
                    UserInitials.Text = userName.Length >= 2 ? userName.Substring(0, 2).ToUpper() : userName.ToUpper();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting current user: {ex.Message}");
                UserNameDisplay.Text = "User";
                UserInitials.Text = "U";
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Any additional loading logic here
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from event handlers to prevent memory leaks
            if (ApplicationDetailView != null)
            {
                ApplicationDetailView.BackToListRequested -= ApplicationDetailView_BackToListRequested;
            }

            // Cancel any pending operations
            _loadCancellation?.Cancel();
            _loadCancellation?.Dispose();
            _loadCancellation = null;

            // Stop search timer
            if (_searchTimer != null)
            {
                _searchTimer.Stop();
                _searchTimer.Tick -= SearchTimer_Tick;
            }

            base.OnClosed(e);
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
        private void UploadExistingNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(PageNameUploadExisting);
            SetActiveNavButton(UploadExistingNavButton);
        }

        private void BrowseDeployExeButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Deploy-Application.exe",
                Filter = "Deploy-Application.exe|Deploy-Application.exe|Executable Files (*.exe)|*.exe",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                ProcessSelectedDeployExe(openFileDialog.FileName);
            }
        }


        private void CreateAppNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(PageNameCreateApplication);
            SetActiveNavButton(CreateAppNavButton);
        }

        private async void ViewAppsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(PageNameViewApplications);
            SetActiveNavButton(ViewAppsNavButton);

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
            ShowPage(PageNameSettings);
            LoadConfigurationDisplay();
            SetActiveNavButton(SettingsNavButton);
        }

        private void ShowPage(string pageName)
        {
            // Hide all existing pages
            CreateApplicationPage.Visibility = Visibility.Collapsed;
            UploadExistingPage.Visibility = Visibility.Collapsed;
            ViewApplicationsPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PaginationPanel.Visibility = Visibility.Collapsed;
            if (_wdacToolsPage != null)
            {
                _wdacToolsPage.Visibility = Visibility.Collapsed;
            }
            if (_logsView != null)
            {
                _logsView.Visibility = Visibility.Collapsed;
            }

            switch (pageName)
            {
                case PageNameCreateApplication:
                    CreateApplicationPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Create Package";
                    PageSubtitle.Text = "Build new application packages for Intune deployment";
                    break;

                case PageNameUploadExisting:
                    UploadExistingPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Upload Package";
                    PageSubtitle.Text = "Upload an existing package to Intune";
                    break;

                case PageNameViewApplications:
                    ViewApplicationsPage.Visibility = Visibility.Visible;
                    PaginationPanel.Visibility = Visibility.Visible;
                    PageTitle.Text = "Applications";
                    PageSubtitle.Text = "Browse and manage Microsoft Intune applications";
                    SearchTextBox.Focus();
                    break;

                case PageNameLogs:
                    if (_logsView == null)
                    {
                        _logsView = new LogsView();
                        MainContentGrid.Children.Add(_logsView);
                    }

                    _logsView.Visibility = Visibility.Visible;
                    PageTitle.Text = "Intune Logs & Diagnostics";
                    PageSubtitle.Text = "View and troubleshoot Intune Management Extension logs";
                    break;

                case PageNameSettings:
                    SettingsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "Settings";
                    PageSubtitle.Text = "Configure application settings and preferences";
                    break;

                case PageNameWDACTools:
                    // Ensure the UserControl is added to the main content area if not already
                    if (_wdacToolsPage == null)
                    {
                        _wdacToolsPage = new WDACToolsPage();
                    }

                    // Add to MainContentGrid if not already a child
                    if (!MainContentGrid.Children.Contains(_wdacToolsPage))
                    {
                        MainContentGrid.Children.Add(_wdacToolsPage);
                    }

                    _wdacToolsPage.Visibility = Visibility.Visible;
                    PageTitle.Text = "WDAC Security Tools";
                    PageSubtitle.Text = "Generate and manage Windows Defender Application Control catalogs";
                    break;
            }
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        // Mouse wheel scrolling is now handled by outer ScrollViewer
        private void ApplicationsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;
                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta);
                eventArg.RoutedEvent = UIElement.MouseWheelEvent;
                eventArg.Source = sender;
                var parent = ((Control)sender).Parent as UIElement;
                parent?.RaiseEvent(eventArg);
            }
        }

        private async void UploadToIntune_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UploadToIntuneButton.IsEnabled = false;
                UploadToIntuneButton.Content = "⏳ Uploading...";

                // Get the parent folder (one level up from Application folder)
                var packagePath = Path.GetDirectoryName(_existingPackageFolder);

                var appInfo = new ApplicationInfo
                {
                    Name = ExistingAppName.Text.Trim(),
                    Manufacturer = ExistingVendor.Text.Trim(),
                    Version = ExistingVersion.Text.Trim(),
                    SourcesPath = _existingPackageFolder,

                };

                // Open upload wizard with all the data
                var uploadWizard = new IntuneUploadWizard
                {
                    Owner = this,
                    ApplicationInfo = appInfo,
                    PackagePath = packagePath
                };

                // Pre-populate detection rules
                uploadWizard.UpdateDetectionRules(_existingDetectionRules);

                // If we have an icon, pass it to the wizard
                if (!string.IsNullOrEmpty(_selectedIconPath))
                {
                    // The wizard will handle the icon through the AppDetailsStep
                }

                if (uploadWizard.ShowDialog() == true)
                {
                    MessageBox.Show("Package uploaded successfully to Microsoft Intune!",
                        "Upload Successful", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Reset form
                    ResetUploadExistingForm();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UploadToIntuneButton.IsEnabled = true;
                UploadToIntuneButton.Content = "☁️ Upload to Intune";
                ValidateUploadButton();
            }
        }


        private void SetActiveNavButton(Button activeButton)
        {
            CreateAppNavButton.Style = (Style)FindResource("SidebarNavButton");
            UploadExistingNavButton.Style = (Style)FindResource("SidebarNavButton");
            ViewAppsNavButton.Style = (Style)FindResource("SidebarNavButton");
            SettingsNavButton.Style = (Style)FindResource("SidebarNavButton");
            LogsNavButton.Style = (Style)FindResource("SidebarNavButton");
            WDACToolsNavButton.Style = (Style)FindResource("SidebarNavButton");
            activeButton.Style = (Style)FindResource("ActiveSidebarNavButton");
        }

        private void WDACToolsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(PageNameWDACTools);
            SetActiveNavButton(WDACToolsNavButton);
        }

        private void LogsNavButton_Click(object sender, RoutedEventArgs e)
        {
            ShowPage(PageNameLogs);
            SetActiveNavButton(LogsNavButton);
        }

        private void ShowAllSections()
        {
            AppInfoSection.Visibility = Visibility.Visible;

            UploadSection.Visibility = Visibility.Visible;
        }

        private void HideAllSections()
        {
            AppInfoSection.Visibility = Visibility.Collapsed;

            UploadSection.Visibility = Visibility.Collapsed;
        }



        #region Validation and Upload
        private void ShowValidationSuccess()
        {
            PackageValidationStatus.Visibility = Visibility.Visible;
            ValidationIcon.Text = "✅";
            ValidationIcon.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
            ValidationTitle.Text = "Valid Package Detected";
            ValidationMessage.Text = "Successfully extracted metadata from Deploy-Application.ps1";
        }
        private void ShowValidationError(string message)
        {
            PackageValidationStatus.Visibility = Visibility.Visible;
            ValidationIcon.Text = "❌";
            ValidationIcon.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));
            ValidationTitle.Text = "Invalid Selection";
            ValidationMessage.Text = message;
        }

        private void ValidateUploadButton()
        {
            bool isValid = !string.IsNullOrWhiteSpace(ExistingAppName.Text) &&
                           !string.IsNullOrWhiteSpace(ExistingVendor.Text) &&
                           !string.IsNullOrWhiteSpace(ExistingVersion.Text);


            UploadToIntuneButton.IsEnabled = isValid;

            if (isValid)
            {
                ReadyStatusText.Text = "✅ Ready to upload to Microsoft Intune";

                ValidationSummary.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));
            }

        }


        private void ResetUploadExistingForm()
        {
            _existingPackageFolder = "";
            _selectedIconPath = "";
            _existingDetectionRules.Clear();

            ExistingDeployExePath.Text = "";
            ExistingAppName.Text = "";
            ExistingVendor.Text = "";
            ExistingVersion.Text = "";
            ExistingScriptDate.Text = "";
            ExistingScriptAuthor.Text = "";




            PackageValidationStatus.Visibility = Visibility.Collapsed;
            HideAllSections();
        }

        #endregion

        #endregion




        #region Application List Management

        private CancellationTokenSource? _loadCancellation;

        private async Task LoadAllApplicationsAsync()
        {
            // Cancel and dispose any existing load operation before creating new one
            var oldCancellation = _loadCancellation;
            _loadCancellation = null;

            try
            {
                oldCancellation?.Cancel();
            }
            catch { /* Ignore cancellation errors */ }
            finally
            {
                oldCancellation?.Dispose();
            }

            try
            {
                if (_intuneService == null) return;

                // Create new cancellation token
                _loadCancellation = new CancellationTokenSource();

                ShowStatus("Loading all applications from Microsoft Intune...");
                ShowProgress(true);

                _allApplications = await _intuneService.GetApplicationsAsync(
                    forceRefresh: false,
                    cancellationToken: _loadCancellation.Token);

                _applicationsLoaded = true;
                _lastSyncTime = DateTime.Now;

                Debug.WriteLine($"Loaded {_allApplications.Count} applications into cache");

                _currentPage = 1;
                ApplyFiltersAndPagination();
                UpdateLastSyncDisplay();
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Application loading was cancelled");
                ShowStatus("Loading cancelled");
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
                _loadCancellation?.Dispose();
                _loadCancellation = null;
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
                if (!string.IsNullOrEmpty(_currentCategoryFilter) && _currentCategoryFilter != AllCategoriesFilter)
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
            CategoryFilter.Items.Add(AllCategoriesFilter);
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

        // Infinite scroll disabled - using pagination buttons instead
        private void ApplicationsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // No-op: Pagination buttons are used instead
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

                        // Get data on background thread
                        var appDetail = await _intuneService.GetApplicationDetailAsync(selectedApp.Id)
                            .ConfigureAwait(false);

                        // ALL UI updates must go through Dispatcher after ConfigureAwait(false)
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ProgressBar.Visibility = Visibility.Collapsed;

                            if (appDetail != null)
                            {
                                ApplicationsListPanel.Visibility = Visibility.Collapsed;
                                ApplicationDetailView.Visibility = Visibility.Visible;
                                ApplicationDetailView.LoadApplicationDetail(appDetail);
                                ApplicationDetailView.ResetScrollPosition();

                                PageTitle.Text = $"Application Details: {selectedApp.DisplayName}";
                                PageSubtitle.Text = "Live data from Microsoft Intune";
                                StatusText.Text = $"Loaded live details for {selectedApp.DisplayName} from Intune";
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Even error handling needs Dispatcher after ConfigureAwait(false)
                        await Dispatcher.InvokeAsync(() =>
                        {
                            ProgressBar.Visibility = Visibility.Collapsed;
                            StatusText.Text = "Error loading application details";
                            MessageBox.Show($"Error loading application details from Intune:\n\n{ex.Message}",
                                "Intune API Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        });
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
            _currentCategoryFilter = CategoryFilter.SelectedItem?.ToString() ?? AllCategoriesFilter;
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
            try
            {
                if (_intuneService == null) return;

                ShowStatus("Refreshing applications from Microsoft Intune...");
                ShowProgress(true);

                // Force refresh from Intune
                _allApplications = await _intuneService.GetApplicationsAsync(forceRefresh: true);
                _applicationsLoaded = true;
                _lastSyncTime = DateTime.Now;

                _currentPage = 1;
                ApplyFiltersAndPagination();
                UpdateLastSyncDisplay();

                ShowStatus($"Refreshed {_allApplications.Count} applications from Intune");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing applications: {ex.Message}");
                ShowStatus($"Error: {ex.Message}");
                MessageBox.Show($"Failed to refresh applications:\n\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowProgress(false);
            }
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
        private void ProcessSelectedDeployExe(string deployExePath)
        {
            try
            {
                // Validate it's Deploy-Application.exe
                if (!Path.GetFileName(deployExePath).Equals("Deploy-Application.exe", StringComparison.OrdinalIgnoreCase))
                {
                    ShowValidationError("Please select Deploy-Application.exe file");
                    return;
                }

                // Get the folder path
                _existingPackageFolder = Path.GetDirectoryName(deployExePath);
                ExistingDeployExePath.Text = deployExePath;

                // Look for Deploy-Application.ps1 in the same folder
                var scriptPath = Path.Combine(_existingPackageFolder, "Deploy-Application.ps1");

                if (File.Exists(scriptPath))
                {
                    ExtractMetadataFromScript(scriptPath);
                    ShowValidationSuccess();
                    ShowAllSections();
                    CheckForMsiInFiles();
                    ValidateUploadButton();
                }
                else
                {
                    ShowValidationError("Deploy-Application.ps1 not found in the same folder");
                    HideAllSections();
                }
            }
            catch (Exception ex)
            {
                ShowValidationError($"Error processing file: {ex.Message}");
                HideAllSections();
            }
        }

        private void ExtractMetadataFromScript(string scriptPath)
        {
            try
            {
                var metadata = MetadataExtractor.ExtractMetadataFromScript(scriptPath);

                ExistingVendor.Text = metadata.GetValueOrDefault("Vendor", "");
                ExistingAppName.Text = metadata.GetValueOrDefault("AppName", "");
                ExistingVersion.Text = metadata.GetValueOrDefault("Version", "");
                ExistingScriptDate.Text = metadata.GetValueOrDefault("ScriptDate", "");
                ExistingScriptAuthor.Text = metadata.GetValueOrDefault("ScriptAuthor", "");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting metadata: {ex.Message}");
                MessageBox.Show($"Warning: Could not extract all metadata from script.\n\nPlease fill in the missing fields manually.",
                    "Partial Data Extraction", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }


        private void CheckForMsiInFiles()
        {
            try
            {
                var filesFolder = Path.Combine(_existingPackageFolder, "Files");
                if (Directory.Exists(filesFolder))
                {
                    var msiFiles = Directory.GetFiles(filesFolder, "*.msi", SearchOption.TopDirectoryOnly);
                    if (msiFiles.Length > 0)
                    {
                        var msiInfo = MsiInfoService.ExtractMsiInfo(msiFiles[0]);
                        if (msiInfo != null && msiInfo.IsValid)
                        {
                            // Auto-add MSI detection rule
                            _existingDetectionRules.Clear();
                            _existingDetectionRules.Add(new DetectionRule
                            {
                                Type = DetectionRuleType.MSI,
                                Path = msiInfo.ProductCode,
                                FileOrFolderName = $"Greater than or equal to:{msiInfo.ProductVersion}",
                                CheckVersion = true
                            });

                            Debug.WriteLine($"Auto-added MSI detection rule: {msiInfo.ProductCode}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for MSI: {ex.Message}");
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

                // Enhanced metadata extraction with MSI detection
                if (fileExtension == ".msi")
                {
                    ExtractMsiMetadataEnhanced(selectedFile);
                }
                else
                {
                    ExtractFileMetadata(selectedFile);
                }
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable button to prevent double-click
                GenerateButton.IsEnabled = false;
                ProgressBar.Visibility = Visibility.Visible;
                StatusText.Text = "Generating package...";

                // Validate inputs on UI thread (needs to read UI controls)
                if (!ValidatePackageInputs())
                {
                    return;
                }

                // Collect data from UI controls while on UI thread
                var appInfo = CreateApplicationInfo();
                var psadtOptions = CollectPSADTOptions();

                // Check if validation failed
                if (psadtOptions == null)
                {
                    StatusText.Text = "Package generation cancelled - fix conflicts";
                    return;
                }

                // Store the options for later use
                _currentPSADTOptions = psadtOptions;

                // Run package creation on background thread
                var generator = new PSADTGenerator();
                string packagePath = null;

                await Task.Run(async () =>
                {
                    packagePath = await generator.CreatePackageAsync(appInfo, psadtOptions)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Store the result
                _currentPackagePath = packagePath;

                // Update UI on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    ShowPackageSuccess(appInfo, psadtOptions);
                    StatusText.Text = $"Package created successfully • {DateTime.Now:HH:mm:ss}";
                });
            }
            catch (Exception ex)
            {
                // Handle errors on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error generating package: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Package generation failed";
                });
            }
            finally
            {
                // Ensure UI cleanup happens on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    GenerateButton.IsEnabled = true;
                    ProgressBar.Visibility = Visibility.Collapsed;
                });
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

                var appInfo = CreateApplicationInfo(); // This now includes MSI data

                var uploadWizard = new IntuneUploadWizard
                {
                    Owner = this,
                    ApplicationInfo = appInfo,
                    PackagePath = _currentPackagePath
                };

                // If MSI, show a tooltip about auto-detection
                if (appInfo.IsMsiPackage)
                {
                    Debug.WriteLine($"Opening upload wizard with MSI Product Code: {appInfo.MsiProductCode}");
                }

                uploadWizard.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening upload wizard: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private PSADTOptions? CollectPSADTOptions()
        {
            return PackageCreationHelper.CollectPSADTOptions(
                UserInstallCheckBox.IsChecked ?? false,
                _currentPSADTOptions,
                DetectedPackageType
            );
        }

        private void SourcesPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(SourcesPathTextBox.Text) && File.Exists(SourcesPathTextBox.Text))
            {
                PSADTConfigSection.Visibility = Visibility.Visible;

                var extension = Path.GetExtension(SourcesPathTextBox.Text).ToLower();
                DetectedPackageTypeIcon.Text = extension switch
                {
                    ".msi" => "📦",
                    ".exe" => "⚙️",
                    _ => "❓"
                };

                DetectedPackageTypeText.Text = extension switch
                {
                    ".msi" => "MSI Package",
                    ".exe" => "EXE Installer",
                    _ => "Unknown Package Type"
                };
            }
        }

        private void ConfigurePSADT_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PSADTConfigDialog();
            dialog.Owner = this;

            // Pass package info
            dialog.SetPackageInfo(
                ManufacturerTextBox.Text,
                AppNameTextBox.Text,
                VersionTextBox.Text,
                DetectedPackageType);

            // Load existing options if any
            if (_currentPSADTOptions != null)
                dialog.LoadOptions(_currentPSADTOptions);

            if (dialog.ShowDialog() == true)
            {
                _currentPSADTOptions = dialog.SelectedOptions;
                UpdatePSADTSummary();
            }
        }
        private void UpdatePSADTSummary()
        {
            PackageCreationHelper.UpdatePSADTSummary(
                _currentPSADTOptions,
                PSADTStatusText,
                SelectedOptionsCount,
                EstimatedScriptSize,
                InjectionCount,
                PSADTSummaryPanel,
                ConfigurePSADTButton
            );
        }

        #endregion

        #region Package Creation Helper Methods

        private void DetectPackageType(string extension, string fileName)
        {
            PSADTConfigSection.Visibility = Visibility.Visible;  // Changed from PSADTOptionsPanel

            if (extension == ".msi")
            {
                DetectedPackageTypeIcon.Text = "📦";
                DetectedPackageTypeText.Text = "MSI Package";

            }
            else if (extension == ".exe")
            {
                DetectedPackageTypeIcon.Text = "⚙️";
                DetectedPackageTypeText.Text = "EXE Installer";

            }
            else
            {
                PSADTConfigSection.Visibility = Visibility.Collapsed;  // Changed
                DetectedPackageTypeIcon.Text = "❓";
                DetectedPackageTypeText.Text = "Unknown package type";
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
                Debug.WriteLine($"Metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(filePath);
            }
        }

        private void ExtractMsiMetadata(string msiPath)
        {
            try
            {
                var (productName, manufacturer, version, productCode) =
                    MetadataExtractor.ExtractMsiMetadata(msiPath);

                if (string.IsNullOrWhiteSpace(AppNameTextBox.Text) && !string.IsNullOrWhiteSpace(productName))
                    AppNameTextBox.Text = productName;

                if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) && !string.IsNullOrWhiteSpace(manufacturer))
                    ManufacturerTextBox.Text = manufacturer;

                if (string.IsNullOrWhiteSpace(VersionTextBox.Text) && !string.IsNullOrWhiteSpace(version))
                    VersionTextBox.Text = version;

                if (!string.IsNullOrWhiteSpace(productCode))
                {
                    _msiProductCode = productCode;
                    _msiProductVersion = version;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSI metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(msiPath);
            }
        }
        private void ExtractMsiMetadataEnhanced(string msiPath)
        {
            try
            {
                // Use the new MsiInfoService
                _currentMsiInfo = MsiInfoService.ExtractMsiInfo(msiPath);

                if (_currentMsiInfo != null && _currentMsiInfo.IsValid)
                {
                    // Populate UI fields
                    if (string.IsNullOrWhiteSpace(AppNameTextBox.Text) && !string.IsNullOrWhiteSpace(_currentMsiInfo.ProductName))
                        AppNameTextBox.Text = _currentMsiInfo.ProductName;

                    if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) && !string.IsNullOrWhiteSpace(_currentMsiInfo.Manufacturer))
                        ManufacturerTextBox.Text = _currentMsiInfo.Manufacturer;

                    if (string.IsNullOrWhiteSpace(VersionTextBox.Text) && !string.IsNullOrWhiteSpace(_currentMsiInfo.ProductVersion))
                        VersionTextBox.Text = _currentMsiInfo.ProductVersion;

                    // Update the package detection display with MSI info
                    DetectedPackageTypeText.Text = $"MSI Package (Product Code: {_currentMsiInfo.ProductCode})";
                    DetectedPackageTypeText.ToolTip = $"Product Code: {_currentMsiInfo.ProductCode}\n" +
                                                      $"Version: {_currentMsiInfo.ProductVersion}\n" +
                                                      $"Upgrade Code: {_currentMsiInfo.UpgradeCode}";

                    Debug.WriteLine($"✅ MSI detected with Product Code: {_currentMsiInfo.ProductCode}");
                }
                else
                {
                    // Fallback to generic MSI detection
                    DetectedPackageTypeText.Text = "MSI Package (Product Code not found)";
                    ExtractMetadataFromFilename(msiPath);

                    Debug.WriteLine("⚠️ MSI detected but could not extract Product Code");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in enhanced MSI extraction: {ex.Message}");
                ExtractMetadataFromFilename(msiPath);
            }
        }


        private void ExtractExeMetadata(string exePath)
        {
            try
            {
                var (productName, companyName, version) =
                    MetadataExtractor.ExtractExeMetadata(exePath);

                if (string.IsNullOrWhiteSpace(AppNameTextBox.Text) && !string.IsNullOrWhiteSpace(productName))
                    AppNameTextBox.Text = productName;

                if (string.IsNullOrWhiteSpace(ManufacturerTextBox.Text) && !string.IsNullOrWhiteSpace(companyName))
                    ManufacturerTextBox.Text = companyName;

                if (string.IsNullOrWhiteSpace(VersionTextBox.Text) && !string.IsNullOrWhiteSpace(version))
                    VersionTextBox.Text = version;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EXE metadata extraction failed: {ex.Message}");
                ExtractMetadataFromFilename(exePath);
            }
        }

        private void ExtractMetadataFromFilename(string filePath)
        {
            if (string.IsNullOrWhiteSpace(AppNameTextBox.Text))
            {
                AppNameTextBox.Text = MetadataExtractor.ExtractNameFromFilename(filePath);
            }
        }

        private bool ValidatePackageInputs()
        {
            return PackageCreationHelper.ValidatePackageInputs(AppNameTextBox.Text);
        }



        private ApplicationInfo CreateApplicationInfo()
        {
            return PackageCreationHelper.CreateApplicationInfo(
                AppNameTextBox.Text,
                ManufacturerTextBox.Text,
                VersionTextBox.Text,
                SourcesPathTextBox.Text,
                _currentMsiInfo
            );
        }

        private void ShowPackageSuccess(ApplicationInfo appInfo, PSADTOptions? psadtOptions)
        {
            PackageCreationHelper.ShowPackageSuccess(
                PackageStatusPanel,
                PackageStatusText,
                StatusText,
                PackagePathText,
                OpenPackageFolderButton,
                ProgressBar,
                _currentPackagePath,
                psadtOptions
            );
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

                var vsCodePath = FindVSCodePath();
                if (string.IsNullOrEmpty(vsCodePath))
                {
                    // Fall back to default shell handler
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = scriptPath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = vsCodePath,
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening script: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string? FindVSCodePath()
        {
            // Check common installation paths
            string[] possiblePaths = {
                @"C:\Program Files\Microsoft VS Code\Code.exe",
                @"C:\Program Files (x86)\Microsoft VS Code\Code.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\Microsoft VS Code\Code.exe")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return null;
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

        private void UpdateLastSyncDisplay()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateLastSyncDisplay());
                return;
            }

            if (_lastSyncTime.HasValue)
            {
                var elapsed = DateTime.Now - _lastSyncTime.Value;
                string timeText;

                if (elapsed.TotalMinutes < 1)
                    timeText = "just now";
                else if (elapsed.TotalMinutes < 60)
                    timeText = $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes > 1 ? "s" : "")} ago";
                else if (elapsed.TotalHours < 24)
                    timeText = $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours > 1 ? "s" : "")} ago";
                else
                    timeText = $"{(int)elapsed.TotalDays} day{((int)elapsed.TotalDays > 1 ? "s" : "")} ago";

                LastSyncText.Text = $"Last sync: {timeText}";
            }
        }

        #endregion

    }
}