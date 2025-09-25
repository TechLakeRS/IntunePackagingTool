// Views/WingetCatalogView.xaml.cs - FIXED VERSION
using System;
using System.Collections.Generic; // ADD THIS
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;

namespace IntunePackagingTool.Views
{
    public partial class WingetCatalogView : UserControl
    {
        private readonly WingetCatalogService _catalogService;
        private readonly WingetPackageCreator _packageCreator;
        private ObservableCollection<WingetPackage> _packages;

        public WingetCatalogView()
        {
            InitializeComponent();
            _catalogService = new WingetCatalogService();
            _packageCreator = new WingetPackageCreator();
            _packages = new ObservableCollection<WingetPackage>();

            // Don't auto-load on startup - wait for user action
            Loaded += (s, e) => InitializeView();
        }

        private void InitializeView()
        {
            PackageDataGrid.ItemsSource = _packages;
            StatusText.Text = "Ready - Enter a search term or click Search to browse popular apps";
            TotalPackagesText.Text = "0";
            ShowingCountText.Text = "0";
            TotalCountText.Text = "0";

            // Show a helpful message
            NoResultsText.Text = "Enter a search term above to find packages";
            NoResultsText.Visibility = Visibility.Visible;
        }

        private async Task LoadCatalog()
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Loading Winget catalog...";

                // First, let's test if winget works at all
                await TestWingetAvailability();

                var packages = await _catalogService.GetFullCatalogAsync();

                System.Diagnostics.Debug.WriteLine($"LoadCatalog: Received {packages.Count} packages");

                _packages.Clear();
                foreach (var pkg in packages)
                {
                    _packages.Add(pkg);
                }

                PackageDataGrid.ItemsSource = _packages;
                StatusText.Text = $"Loaded {packages.Count} packages from Winget";
                TotalPackagesText.Text = packages.Count.ToString();
                ShowingCountText.Text = packages.Count.ToString();
                TotalCountText.Text = packages.Count.ToString();

                if (packages.Count == 0)
                {
                    NoResultsText.Visibility = Visibility.Visible;
                }
                else
                {
                    NoResultsText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadCatalog ERROR: {ex.Message}");
                MessageBox.Show($"Error loading catalog: {ex.Message}\n\nCheck the Output window for debug information.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task TestWingetAvailability()
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var version = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                System.Diagnostics.Debug.WriteLine($"Winget version: {version.Trim()}");
                StatusText.Text = $"Winget version: {version.Trim()}";

                if (process.ExitCode != 0)
                {
                    throw new Exception("Winget is not available or not working properly");
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Winget not found: {ex.Message}");

                var result = MessageBox.Show(
                    "Windows Package Manager (winget) is not installed on this system.\n\n" +
                    "To use the Winget Catalog feature, you need to install winget first.\n\n" +
                    "Would you like to open the Microsoft Store to install it?\n\n" +
                    "Look for 'App Installer' in the Store.",
                    "Winget Not Installed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Open Microsoft Store to App Installer page
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1",
                        UseShellExecute = true
                    });
                }

                throw new Exception("Winget is not installed. Please install 'App Installer' from Microsoft Store.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Winget test failed: {ex.Message}");
                throw new Exception($"Winget is not available: {ex.Message}");
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                NoResultsText.Visibility = Visibility.Collapsed;

                var searchTerm = SearchTextBox.Text?.Trim();

                // If empty, search for popular Microsoft apps as a default
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    StatusText.Text = "Loading popular applications...";
                    searchTerm = "Microsoft";
                }
                else
                {
                    StatusText.Text = $"Searching for '{searchTerm}'...";
                }

                var packages = await _catalogService.SearchPackagesAsync(searchTerm);

                _packages.Clear();
                foreach (var pkg in packages.Take(50)) // Limit to 50 results
                {
                    _packages.Add(pkg);
                }

                if (packages.Count == 0)
                {
                    NoResultsText.Text = $"No packages found for '{searchTerm}'";
                    NoResultsText.Visibility = Visibility.Visible;
                    StatusText.Text = "No results found";
                }
                else
                {
                    NoResultsText.Visibility = Visibility.Collapsed;
                    StatusText.Text = $"Found {packages.Count} packages (showing {Math.Min(50, packages.Count)})";
                }

                ShowingCountText.Text = Math.Min(50, packages.Count).ToString();
                TotalCountText.Text = packages.Count.ToString();
                TotalPackagesText.Text = packages.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Search failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Search failed";
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // Clear search and load popular apps
            SearchTextBox.Text = "";
            await SearchForPopularApps();
        }

        private async Task SearchForPopularApps()
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                NoResultsText.Visibility = Visibility.Collapsed;
                StatusText.Text = "Loading popular applications...";

                // Get a mix of popular apps
                var popularSearches = new[] { "Microsoft", "Google", "Mozilla", "Adobe", "7zip" };
                var allPackages = new List<WingetPackage>();

                foreach (var search in popularSearches)
                {
                    var packages = await _catalogService.SearchPackagesAsync(search);
                    allPackages.AddRange(packages.Take(10)); // Take top 10 from each
                }

                // Remove duplicates and limit
                var uniquePackages = allPackages
                    .GroupBy(p => p.Id)
                    .Select(g => g.First())
                    .Take(50)
                    .ToList();

                _packages.Clear();
                foreach (var pkg in uniquePackages)
                {
                    _packages.Add(pkg);
                }

                StatusText.Text = $"Loaded {uniquePackages.Count} popular packages";
                ShowingCountText.Text = uniquePackages.Count.ToString();
                TotalCountText.Text = uniquePackages.Count.ToString();
                TotalPackagesText.Text = uniquePackages.Count.ToString();

                if (uniquePackages.Count == 0)
                {
                    NoResultsText.Text = "No packages found";
                    NoResultsText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading packages: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to load packages";
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SearchTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                SearchButton_Click(sender, e);
            }
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var package = button?.Tag as WingetPackage;

            if (package == null) return;

            var result = MessageBox.Show(
                $"Install {package.Name} directly on this machine using Winget?",
                "Install Package",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    LoadingPanel.Visibility = Visibility.Visible;
                    StatusText.Text = $"Installing {package.Name}...";

                    var success = await _catalogService.InstallPackageAsync(package.Id);

                    if (success)
                    {
                        MessageBox.Show($"{package.Name} installed successfully!", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        package.IsInstalled = true;
                    }
                    else
                    {
                        MessageBox.Show($"Failed to install {package.Name}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Installation failed: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    StatusText.Text = "Ready";
                }
            }
        }

        private async void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var package = button?.Tag as WingetPackage;

            if (package == null) return;

            await ShowPackageDetails(package);
        }

        private async Task CreateIntunePackage(WingetPackage package)
        {
            try
            {
                // Simple options dialog
                var options = new PackageOptions
                {
                    RemoveOldVersions = true,
                    CloseApps = true,
                    CreateShortcuts = false
                };

                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = $"Creating package for {package.Name}...";

                // FIX: Use package.Id instead of undefined wingetId
                var result = await _packageCreator.CreatePackageFromWinget(package.Id, options);

                if (result.Success)
                {
                    var message = $"Package created successfully!\n\nPath: {result.PackagePath}\n\n" +
                                 "Would you like to open the package folder?";

                    var openResult = MessageBox.Show(message, "Success",
                        MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (openResult == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", result.PackagePath);
                    }
                }
                else
                {
                    MessageBox.Show($"Failed to create package: {result.ErrorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating package: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = "Ready";
            }
        }

        // Rest of the methods remain the same...
        private void SourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void SortBy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplySorting();
        }

        private void ShowInstalledOnly_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void PackageDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle selection if needed
        }

        private async Task ShowPackageDetails(WingetPackage package)
        {
            if (package == null) return;

            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = $"Getting details for {package.Name}...";

                System.Diagnostics.Debug.WriteLine($"Getting details for package ID: {package.Id}");

                var details = await _catalogService.GetPackageDetailsAsync(package.Id);

                // Check if details is null
                if (details == null)
                {
                    System.Diagnostics.Debug.WriteLine("GetPackageDetailsAsync returned null");

                    // Use the basic package info we already have
                    var basicMessage = $"Package: {package.Name}\n" +
                                     $"ID: {package.Id}\n" +
                                     $"Version: {package.Version ?? "Unknown"}\n" +
                                     $"Publisher: {package.Publisher ?? "Unknown"}\n\n" +
                                     $"Unable to retrieve full details from winget.\n" +
                                     $"Would you like to create an Intune package from this anyway?";

                    var basicResult = MessageBox.Show(basicMessage, "Package Information",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (basicResult == MessageBoxResult.Yes)
                    {
                        await CreateIntunePackage(package);
                    }
                    return;
                }

                // Show full details
                var message = $"Package: {details.Name}\n" +
                             $"Version: {details.Version}\n" +
                             $"Publisher: {details.Publisher}\n" +
                             $"Description: {details.Description}\n\n" +
                             $"Would you like to create an Intune package from this?";

                var result = MessageBox.Show(message, "Package Details",
                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    await CreateIntunePackage(package);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ShowPackageDetails error: {ex}");

                // Provide more informative error message
                var errorMessage = $"Error getting details for {package.Name}:\n{ex.Message}\n\n" +
                                  $"Would you like to try creating an Intune package anyway?";

                var result = MessageBox.Show(errorMessage, "Error",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await CreateIntunePackage(package);
                }
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = "Ready";
            }
        }

        private async void PackageDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PackageDataGrid.SelectedItem is WingetPackage package)
            {
                await ShowPackageDetails(package);
            }
        }

        private void ApplyFilters()
        {
            if (_packages == null || PackageDataGrid == null) return;

            // Simple filtering logic - can be enhanced
            var filtered = _packages.AsEnumerable();

            if (ShowInstalledOnly?.IsChecked == true)
            {
                filtered = filtered.Where(p => p.IsInstalled);
            }

            // Update the DataGrid
            PackageDataGrid.ItemsSource = new ObservableCollection<WingetPackage>(filtered);

            ShowingCountText.Text = filtered.Count().ToString();
        }

        private void ApplySorting()
        {
            if (_packages == null || PackageDataGrid == null) return;

            var sorted = _packages.AsEnumerable();

            if (SortBy?.SelectedItem is ComboBoxItem item)
            {
                var sortBy = item.Content.ToString();
                sorted = sortBy switch
                {
                    "Name" => sorted.OrderBy(p => p.Name),
                    "Publisher" => sorted.OrderBy(p => p.Publisher),
                    "Version" => sorted.OrderBy(p => p.Version),
                    _ => sorted
                };
            }

            PackageDataGrid.ItemsSource = new ObservableCollection<WingetPackage>(sorted);
        }
    }
}