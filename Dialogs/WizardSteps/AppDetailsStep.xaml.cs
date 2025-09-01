using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.WizardSteps
{
    public partial class AppDetailsStep : UserControl
    {
        public event ValidationChangedEventHandler? ValidationChanged;
        public event DataChangedEventHandler? DataChanged;

        private ApplicationInfo? _applicationInfo;
        public ApplicationInfo? ApplicationInfo
        {
            get => _applicationInfo;
            set
            {
                _applicationInfo = value;
                LoadApplicationData();
            }
        }

        private string _selectedIconPath = "";
        public string SelectedIconPath => _selectedIconPath;

        public AppDetailsStep()
        {
            InitializeComponent();

            // ✅ PERFORMANCE: Wire up validation events for real-time feedback
            InstallCommandTextBox.TextChanged += (s, e) => ValidateAndNotify();
            UninstallCommandTextBox.TextChanged += (s, e) => ValidateAndNotify();
            DescriptionTextBox.TextChanged += (s, e) => ValidateAndNotify();
            InstallContextCombo.SelectionChanged += (s, e) => ValidateAndNotify();

            // Initial validation
            ValidateAndNotify();
        }

        private void LoadApplicationData()
        {
            if (_applicationInfo != null)
            {
                // Pre-populate description based on app info
                if (string.IsNullOrWhiteSpace(DescriptionTextBox.Text) ||
                    DescriptionTextBox.Text == "Application packaged with NBB PSADT Tools")
                {
                    DescriptionTextBox.Text = $"{_applicationInfo.Name} packaged with NBB PSADT Tools";
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
                    DefaultIconText.Text = "✅";
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

        // ✅ PERFORMANCE: Fast validation with immediate feedback
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
            // ✅ PERFORMANCE: Simple field validation - ~1ms execution time
            return !string.IsNullOrWhiteSpace(InstallCommandTextBox.Text) &&
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
                    Manufacturer = _applicationInfo.Manufacturer,
                    Name = _applicationInfo.Name,
                    Version = _applicationInfo.Version,
                    InstallContext = ((ComboBoxItem)InstallContextCombo.SelectedItem)?.Content?.ToString()?.ToLower() ?? "system",
                    SourcesPath = _applicationInfo.SourcesPath,
                    ServiceNowSRI = _applicationInfo.ServiceNowSRI
                };

                DataChanged?.Invoke(updatedInfo);
            }
        }

        // ✅ PERFORMANCE: Public methods for wizard to access data quickly
        public string GetInstallCommand() => InstallCommandTextBox.Text.Trim();
        public string GetUninstallCommand() => UninstallCommandTextBox.Text.Trim();
        public string GetDescription() => DescriptionTextBox.Text.Trim();
        public string GetInstallContext() => ((ComboBoxItem)InstallContextCombo.SelectedItem)?.Content?.ToString()?.ToLower() ?? "system";
        public string GetSelectedIconPath() => _selectedIconPath;

        // ✅ PERFORMANCE: Allow wizard to set values directly (faster than binding)
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
    }
    
}