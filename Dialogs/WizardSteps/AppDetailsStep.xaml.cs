using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace IntunePackagingTool.WizardSteps
{
    public partial class AppDetailsStep : UserControl
    {
        private ApplicationInfo? _applicationInfo;
        private bool _isLoadingData = false;

        // Public property for ApplicationInfo
        public ApplicationInfo? ApplicationInfo
        {
            get => _applicationInfo;
            set
            {
                _applicationInfo = value;
                if (value != null)
                {
                    LoadFromApplicationInfo(value);
                }
            }
        }

        public string? SelectedIconPath { get; set; }

        // Event delegates
        public event Action<bool>? ValidationChanged;
        public event Action<ApplicationInfo>? DataChanged;

        public AppDetailsStep()
        {
            InitializeComponent();

            // Delay event attachment to prevent firing during initialization
            this.Loaded += (s, e) =>
            {
                // Set up event handlers for validation AFTER the control is loaded
                AppNameTextBox.TextChanged += OnFieldChanged;
                VersionTextBox.TextChanged += OnFieldChanged;
                PublisherTextBox.TextChanged += OnFieldChanged;
                InstallCommandTextBox.TextChanged += OnFieldChanged;
                UninstallCommandTextBox.TextChanged += OnFieldChanged;
                DescriptionTextBox.TextChanged += OnFieldChanged;
                InstallContextCombo.SelectionChanged += OnFieldChanged;
                CategoryCombo.SelectionChanged += OnFieldChanged;

                // Update display name when app name or version changes
                AppNameTextBox.TextChanged += UpdateDisplayName;
                VersionTextBox.TextChanged += UpdateDisplayName;
                PublisherTextBox.TextChanged += UpdateDisplayName;
            };
        }

        public void LoadFromApplicationInfo(ApplicationInfo info)
        {
            System.Diagnostics.Debug.WriteLine($"=== LoadFromApplicationInfo called ===");

            if (info == null) return;

            // Use Dispatcher to ensure UI thread and controls are ready
            Dispatcher.InvokeAsync(() =>
            {
                System.Diagnostics.Debug.WriteLine($"=== In Dispatcher ===");
                System.Diagnostics.Debug.WriteLine($"AppNameTextBox is null? {AppNameTextBox == null}");

                _isLoadingData = true;
                _applicationInfo = info;

                if (AppNameTextBox != null && !string.IsNullOrEmpty(info.Name))
                {
                    System.Diagnostics.Debug.WriteLine($"Setting AppNameTextBox to: '{info.Name}'");
                    AppNameTextBox.Text = info.Name;
                    System.Diagnostics.Debug.WriteLine($"AppNameTextBox.Text is now: '{AppNameTextBox.Text}'");
                }

                if (VersionTextBox != null && !string.IsNullOrEmpty(info.Version))
                {
                    System.Diagnostics.Debug.WriteLine($"Setting VersionTextBox to: '{info.Version}'");
                    VersionTextBox.Text = info.Version;
                }

                if (PublisherTextBox != null && !string.IsNullOrEmpty(info.Manufacturer))
                {
                    System.Diagnostics.Debug.WriteLine($"Setting PublisherTextBox to: '{info.Manufacturer}'");
                    PublisherTextBox.Text = info.Manufacturer;
                }

                _isLoadingData = false;

                ValidateFields();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void UpdateDisplayName(object? sender, EventArgs? e)
        {
            // Auto-generate display name
            var publisher = PublisherTextBox?.Text?.Trim() ?? "";
            var appName = AppNameTextBox?.Text?.Trim() ?? "";
            var version = VersionTextBox?.Text?.Trim() ?? "";

            if (!string.IsNullOrEmpty(publisher) && !string.IsNullOrEmpty(appName))
            {
                DisplayNameTextBox.Text = $"{publisher} {appName} {version}".Trim();
            }
        }

        private void OnFieldChanged(object sender, EventArgs e)
        {
            ValidateFields();
            UpdateApplicationInfo();
        }

        private void ValidateFields()
        {
            bool isValid = !string.IsNullOrWhiteSpace(AppNameTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(VersionTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(PublisherTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(InstallCommandTextBox.Text) &&
                          !string.IsNullOrWhiteSpace(UninstallCommandTextBox.Text);

            ValidationChanged?.Invoke(isValid);
        }

        private void UpdateApplicationInfo()
        {
            if (_applicationInfo != null)
            {
                // Get current values
                var newName = AppNameTextBox.Text.Trim();
                var newVersion = VersionTextBox.Text.Trim();
                var newManufacturer = PublisherTextBox.Text.Trim();
                var newInstallContext = (InstallContextCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "System";

                // Only update if values are not empty (to prevent overwriting during initialization)
                if (!string.IsNullOrEmpty(newName))
                    _applicationInfo.Name = newName;

                if (!string.IsNullOrEmpty(newVersion))
                    _applicationInfo.Version = newVersion;

                if (!string.IsNullOrEmpty(newManufacturer))
                    _applicationInfo.Manufacturer = newManufacturer;

                _applicationInfo.InstallContext = newInstallContext;

                // Only fire DataChanged if we have valid data
                if (!string.IsNullOrEmpty(_applicationInfo.Name) ||
                    !string.IsNullOrEmpty(_applicationInfo.Version) ||
                    !string.IsNullOrEmpty(_applicationInfo.Manufacturer))
                {
                    DataChanged?.Invoke(_applicationInfo);
                }
            }
        }

        public void EnableSmartMode()
        {
            // Show auto-detected panel
            AutoDetectedPanel.Visibility = Visibility.Visible;

            // Show auto-detected indicators next to field labels
            AppNameIndicator.Visibility = Visibility.Visible;
            VersionIndicator.Visibility = Visibility.Visible;
            PublisherIndicator.Visibility = Visibility.Visible;
            InstallContextIndicator.Visibility = Visibility.Visible;
            InstallCommandIndicator.Visibility = Visibility.Visible;
            UninstallCommandIndicator.Visibility = Visibility.Visible;

            // Change background color of auto-filled fields to light green
            var autoFilledBrush = new SolidColorBrush(Color.FromRgb(240, 255, 244));
            AppNameTextBox.Background = autoFilledBrush;
            VersionTextBox.Background = autoFilledBrush;
            PublisherTextBox.Background = autoFilledBrush;
            InstallCommandTextBox.Background = autoFilledBrush;
            UninstallCommandTextBox.Background = autoFilledBrush;
            InstallContextCombo.Background = autoFilledBrush;

            // Optional: Also update the description if it was auto-generated
            if (!string.IsNullOrEmpty(DescriptionTextBox.Text))
            {
                DescriptionTextBox.Background = autoFilledBrush;
            }
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.ico)|*.png;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*",
                Title = "Select Application Icon"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    var fileInfo = new FileInfo(openFileDialog.FileName);
                    if (fileInfo.Length > 500 * 1024) // 500KB limit
                    {
                        MessageBox.Show("Icon file must be less than 500KB", "File Too Large",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    SelectedIconPath = openFileDialog.FileName;

                    // Display the icon
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(openFileDialog.FileName);
                    bitmap.DecodePixelWidth = 64;
                    bitmap.EndInit();

                    SelectedIconImage.Source = bitmap;
                    SelectedIconImage.Visibility = Visibility.Visible;
                    DefaultIconText.Visibility = Visibility.Collapsed;

                    // Update UI
                    SelectedIconText.Text = $"✅ {Path.GetFileName(openFileDialog.FileName)}";
                    SelectedIconText.Visibility = Visibility.Visible;
                    ClearIconButton.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading icon: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedIconPath = null;
            SelectedIconImage.Visibility = Visibility.Collapsed;
            DefaultIconText.Visibility = Visibility.Visible;
            SelectedIconText.Visibility = Visibility.Collapsed;
            ClearIconButton.Visibility = Visibility.Collapsed;
        }

        // Public methods for getting values
        public string GetInstallCommand() => InstallCommandTextBox.Text.Trim();
        public string GetUninstallCommand() => UninstallCommandTextBox.Text.Trim();
        public string GetDescription() => DescriptionTextBox.Text.Trim();
        public string GetInstallContext() => (InstallContextCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "System";
        public string GetCategory() => (CategoryCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Business";
    }

    // If these delegates don't exist in your project, define them:
    

    public delegate void DataChangedEventHandler(ApplicationInfo applicationInfo);
}