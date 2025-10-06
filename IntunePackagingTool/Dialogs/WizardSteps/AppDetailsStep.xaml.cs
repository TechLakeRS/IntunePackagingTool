using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.WizardSteps
{
    public partial class AppDetailsStep : UserControl
    {
        public event ValidationChangedEventHandler? ValidationChanged;
        public event DataChangedEventHandler? DataChanged;

        private ApplicationInfo? _applicationInfo;
        private string _selectedIconPath = "";

        public ApplicationInfo? ApplicationInfo
        {
            get => _applicationInfo;
            set
            {
                _applicationInfo = value;
                LoadApplicationData();
            }
        }

        public string SelectedIconPath => _selectedIconPath;

        public AppDetailsStep()
        {
            InitializeComponent();

            // Wire up validation events for real-time feedback
            AppNameTextBox.TextChanged += (s, e) => ValidateAndNotify();
            PublisherTextBox.TextChanged += (s, e) => ValidateAndNotify();
            VersionTextBox.TextChanged += (s, e) => ValidateAndNotify();
            InstallCommandTextBox.TextChanged += (s, e) => ValidateAndNotify();
            UninstallCommandTextBox.TextChanged += (s, e) => ValidateAndNotify();
            DescriptionTextBox.TextChanged += (s, e) => ValidateAndNotify();
            InstallContextCombo.SelectionChanged += (s, e) => ValidateAndNotify();

            // Set default values
            SetDefaultValues();

            // Initial validation
            ValidateAndNotify();
        }

        private void SetDefaultValues()
        {
            InstallCommandTextBox.Text = "Deploy-Application.exe Install";
            UninstallCommandTextBox.Text = "Deploy-Application.exe Uninstall";
            DescriptionTextBox.Text = "Application packaged with NBB PSADT Tools";
        }

        private void LoadApplicationData()
        {
            if (_applicationInfo != null)
            {
                // Populate fields from ApplicationInfo
                if (!string.IsNullOrWhiteSpace(_applicationInfo.Name))
                    AppNameTextBox.Text = _applicationInfo.Name;

                if (!string.IsNullOrWhiteSpace(_applicationInfo.Manufacturer))
                    PublisherTextBox.Text = _applicationInfo.Manufacturer;

                if (!string.IsNullOrWhiteSpace(_applicationInfo.Version))
                    VersionTextBox.Text = _applicationInfo.Version;

                // Update description with app info if default
                if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ||
                    DescriptionTextBox.Text == "Application packaged with NBB PSADT Tools")
                {
                    DescriptionTextBox.Text = $"{_applicationInfo.Name} {_applicationInfo.Version} - Packaged with NBB PSADT Tools";
                }

                // Set install context if provided
                if (!string.IsNullOrWhiteSpace(_applicationInfo.InstallContext))
                {
                    SetInstallContext(_applicationInfo.InstallContext);
                }
            }
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Application Icon",
                    Filter = "Image files (*.png;*.ico;*.jpg)|*.png;*.ico;*.jpg|All files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    _selectedIconPath = dialog.FileName;

                    // Try to load and display the icon
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(dialog.FileName);
                        bitmap.DecodePixelWidth = 48;
                        bitmap.EndInit();

                        SelectedIconImage.Source = bitmap;
                        SelectedIconImage.Visibility = Visibility.Visible;
                        DefaultIconText.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        // If image fails to load, just show checkmark
                        DefaultIconText.Text = "✅";
                    }

                    SelectedIconText.Text = $"Selected: {Path.GetFileName(dialog.FileName)}";
                    SelectedIconText.Visibility = Visibility.Visible;

                    // Notify parent of data change
                    NotifyDataChanged();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting icon: {ex.Message}", "Icon Selection Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ValidateAndNotify()
        {
            var isValid = ValidateForm();
            ValidationChanged?.Invoke(isValid);

            if (isValid)
            {
                NotifyDataChanged();
            }
        }

        private bool ValidateForm()
        {
            // Check required fields
            return !string.IsNullOrWhiteSpace(AppNameTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(PublisherTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(VersionTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(InstallCommandTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(UninstallCommandTextBox.Text) &&
                   !string.IsNullOrWhiteSpace(DescriptionTextBox.Text);
        }

        private void NotifyDataChanged()
        {
            if (_applicationInfo != null)
            {
                // Create updated ApplicationInfo with current form values
                var updatedInfo = new ApplicationInfo
                {
                    Name = AppNameTextBox.Text.Trim(),
                    Manufacturer = PublisherTextBox.Text.Trim(),
                    Version = VersionTextBox.Text.Trim(),
                    InstallContext = GetInstallContext(),
                    SourcesPath = _applicationInfo.SourcesPath,
                    ServiceNowSRI = _applicationInfo.ServiceNowSRI,
                    // Preserve MSI information if present
                    MsiProductCode = _applicationInfo.MsiProductCode,
                    MsiProductVersion = _applicationInfo.MsiProductVersion,
                    MsiUpgradeCode = _applicationInfo.MsiUpgradeCode
                    // IsMsiPackage is read-only and will be automatically determined by the presence of MsiProductCode
                };

                DataChanged?.Invoke(updatedInfo);
            }
        }

        // Public methods for wizard to access data
        public string GetInstallCommand() => InstallCommandTextBox.Text.Trim();
        public string GetUninstallCommand() => UninstallCommandTextBox.Text.Trim();
        public string GetDescription() => DescriptionTextBox.Text.Trim();
        public string GetInstallContext() => ((ComboBoxItem)InstallContextCombo.SelectedItem)?.Content?.ToString()?.ToLower() ?? "system";
        public string GetSelectedIconPath() => _selectedIconPath;

        // Public methods to set values
        public void SetInstallCommand(string command)
        {
            InstallCommandTextBox.Text = command;
        }

        public void SetUninstallCommand(string command)
        {
            UninstallCommandTextBox.Text = command;
        }

        public void SetDescription(string description)
        {
            DescriptionTextBox.Text = description;
        }

        public void SetInstallContext(string context)
        {
            var targetItem = context.ToLower() switch
            {
                "system" => 0,
                "user" => 1,
                _ => 0
            };
            InstallContextCombo.SelectedIndex = targetItem;
        }

        // Populate data from ApplicationInfo
        public void LoadFromApplicationInfo(ApplicationInfo appInfo)
        {
            if (appInfo != null)
            {
                _applicationInfo = appInfo;
                LoadApplicationData();
            }
        }
    }
}