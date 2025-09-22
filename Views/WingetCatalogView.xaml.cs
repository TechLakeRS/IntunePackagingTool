// Views/WingetCatalogView.xaml.cs
using System;
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

            Loaded += async (s, e) => await LoadCatalog();
        }

        private async Task LoadCatalog()
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Loading Winget catalog...";

                var packages = await _catalogService.GetFullCatalogAsync();

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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading catalog: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = "Searching...";

                var searchTerm = SearchTextBox.Text;
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    await LoadCatalog();
                    return;
                }

                var packages = await _catalogService.SearchPackagesAsync(searchTerm);

                _packages.Clear();
                foreach (var pkg in packages)
                {
                    _packages.Add(pkg);
                }

                StatusText.Text = $"Found {packages.Count} packages";
                ShowingCountText.Text = packages.Count.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Search failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadCatalog();
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

            try
            {
                LoadingPanel.Visibility = Visibility.Visible;
                StatusText.Text = $"Getting details for {package.Name}...";

                var details = await _catalogService.GetPackageDetailsAsync(package.Id);

                // Show details in a message box for now
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
                MessageBox.Show($"Error getting details: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                StatusText.Text = "Ready";
            }
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

        private void PackageDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PackageDataGrid.SelectedItem is WingetPackage package)
            {
                DetailsButton_Click(null, null);
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