using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using Microsoft.Win32;

namespace IntunePackagingTool.Views
{
    public partial class ReportingWindow : Window
    {
        private readonly string _applicationId;
        private readonly string _applicationName;
        private readonly IntuneService _intuneService;
        private ObservableCollection<DeviceInstallStatus> _allStatuses;
        private ObservableCollection<DeviceInstallStatus> _filteredStatuses;
        private CollectionViewSource _viewSource;

        public ReportingWindow(string applicationId, string applicationName)
        {
            InitializeComponent();
            _applicationId = applicationId;
            _applicationName = applicationName;

            // Initialize Intune service with settings
            var settingsService = new SettingsService();
            var settings = settingsService.Settings;
            _intuneService = new IntuneService(
                settings.Authentication.TenantId,
                settings.Authentication.ClientId,
                settings.Authentication.CertificateThumbprint
            );

            _allStatuses = new ObservableCollection<DeviceInstallStatus>();
            _filteredStatuses = new ObservableCollection<DeviceInstallStatus>();

            AppNameHeader.Text = _applicationName;

            _viewSource = new CollectionViewSource { Source = _filteredStatuses };
            DeviceStatusGrid.ItemsSource = _viewSource.View;

            Loaded += async (s, e) => await LoadReportData();
        }

        private async Task LoadReportData()
        {
            try
            {
                StatusText.Text = "Loading installation statistics from Microsoft Intune...";

                // Get statistics - use GetInstallationStatisticsAsync (returns InstallationStatistics)
                var stats = await _intuneService.GetInstallationStatisticsAsync(_applicationId);
                UpdateStatisticsDisplay(stats);

                // Get detailed device status - use GetApplicationInstallStatusAsync (returns List<DeviceInstallStatus>)
                var deviceStatuses = await _intuneService.GetApplicationInstallStatusAsync(_applicationId);

                _allStatuses.Clear();
                foreach (var status in deviceStatuses.OrderBy(s => s.DeviceName))
                {
                    _allStatuses.Add(status);
                }

                ApplyFilter();

                StatusText.Text = $"Showing {_filteredStatuses.Count} device installation records • Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading report data:\n\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to load installation statistics";
            }
        }

        private void UpdateStatisticsDisplay(InstallationStatistics stats)
        {
            TotalDevicesText.Text = stats.TotalDevices.ToString();
            SuccessfulText.Text = stats.SuccessfulInstalls.ToString();
            FailedText.Text = stats.FailedInstalls.ToString();
            PendingText.Text = stats.PendingInstalls.ToString();
            NotInstalledText.Text = stats.NotInstalled.ToString();

            SuccessRateText.Text = $"Installed ({stats.SuccessRate}%)";
            FailureRateText.Text = $"Failed ({stats.FailureRate}%)";
        }

        private void ApplyFilter()
        {
            _filteredStatuses.Clear();

            var filtered = _allStatuses.AsEnumerable();

            // Apply status filter
            if (StatusFilter.SelectedIndex > 0)
            {
                var filterText = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                if (filterText.Contains("Installed Only"))
                    filtered = filtered.Where(s => s.InstallState?.ToLower() == "installed");
                else if (filterText.Contains("Failed Only"))
                    filtered = filtered.Where(s => s.InstallState?.ToLower() == "failed");
                else if (filterText.Contains("Pending Only"))
                    filtered = filtered.Where(s => s.InstallState?.ToLower() == "pending");
                else if (filterText.Contains("Not Installed"))
                    filtered = filtered.Where(s => s.InstallState?.ToLower() == "notinstalled");
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                var searchText = SearchBox.Text.ToLower();
                filtered = filtered.Where(s =>
                    s.DeviceName?.ToLower().Contains(searchText) == true ||
                    s.UserPrincipalName?.ToLower().Contains(searchText) == true ||
                    s.UserName?.ToLower().Contains(searchText) == true);
            }

            foreach (var status in filtered)
            {
                _filteredStatuses.Add(status);
            }

            StatusText.Text = $"Showing {_filteredStatuses.Count} of {_allStatuses.Count} devices";
        }

        private void StatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allStatuses != null)
                ApplyFilter();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_allStatuses != null)
                ApplyFilter();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshButton.IsEnabled = false;
            RefreshButton.Content = "Refreshing...";

            await LoadReportData();

            RefreshButton.IsEnabled = true;
            RefreshButton.Content = "🔄 Refresh";
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    FileName = $"{_applicationName}_InstallReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = "csv"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    using (var writer = new StreamWriter(saveDialog.FileName))
                    {
                        // Write header
                        writer.WriteLine("Device Name,User,Install Status,Error Code,Last Sync");

                        // Write data
                        foreach (var status in _filteredStatuses)
                        {
                            writer.WriteLine($"\"{status.DeviceName}\",\"{status.UserPrincipalName}\",\"{status.InstallState}\",\"{status.ErrorCode}\",\"{status.FormattedLastSync}\"");
                        }
                    }

                    MessageBox.Show($"Report exported successfully to:\n{saveDialog.FileName}",
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting report:\n\n{ex.Message}",
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}