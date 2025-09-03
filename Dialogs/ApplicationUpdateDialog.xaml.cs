using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace IntunePackagingTool.Dialogs
{
    public partial class ApplicationUpdateDialog : Window
    {
        private ApplicationDetail _currentApp;
        private string _packagePath;
        private string _iconPath;

        public bool UpdateSuccessful { get; set; }
        public string NewDisplayName { get; set; }
        public string NewDescription { get; set; }
        public string NewCategory { get; set; }
        public string NewPackagePath { get; set; }
        public string NewIconPath { get; set; }
        public List<DetectionRule> NewDetectionRules { get; set; }

        public ApplicationUpdateDialog(ApplicationDetail currentApp)
        {
            InitializeComponent();
            _currentApp = currentApp;

            // Set header info
            HeaderAppName.Text = currentApp.DisplayName;
            HeaderVersion.Text = currentApp.Version ?? "Unknown";

            // Pre-populate with current values
            DisplayNameTextBox.Text = currentApp.DisplayName;
            DescriptionTextBox.Text = currentApp.Description ?? "";

            LoadCategoriesFromIntune(currentApp.Category);

            CategoryComboBox.Text = currentApp.Category ?? "Business";
            CategoryComboBox.IsEditable = true;

            // Load current detection rules if they exist
            LoadCurrentDetectionRules();

            // Load current icon if exists
            if (currentApp.IconImage != null)
            {
                IconPreview.Source = currentApp.IconImage;
            }
        }


        private async void LoadCategoriesFromIntune(string currentCategory)
        {
            try
            {
                var intuneService = new IntuneService();
                var categories = await intuneService.GetApplicationCategoriesAsync();

                // Clear hardcoded items
                CategoryComboBox.Items.Clear();

                // Add categories from Intune
                foreach (var category in categories)
                {
                    CategoryComboBox.Items.Add(new ComboBoxItem { Content = category });
                }

                // Set current category
                CategoryComboBox.Text = currentCategory ?? "Business";
            }
            catch
            {
                // If loading fails, keep the hardcoded items and just set the text
                CategoryComboBox.Text = currentCategory ?? "Business";
            }
        }

        private void LoadCurrentDetectionRules()
        {
            if (_currentApp.DetectionRules?.Count > 0)
            {
                var firstRule = _currentApp.DetectionRules[0];

                switch (firstRule.Type)
                {
                    case DetectionRuleType.File:
                        DetectionTypeCombo.SelectedIndex = 0;
                        FilePathTextBox.Text = firstRule.Path;
                        // FileOrFolderName is the actual file name
                        var fullPath = System.IO.Path.Combine(firstRule.Path, firstRule.FileOrFolderName);
                        FilePathTextBox.Text = fullPath;

                        if (firstRule.CheckVersion)
                        {
                            FileDetectionMethodCombo.Text = "Version";
                            // Version info might be in FileOrFolderName when CheckVersion is true
                            FileVersionTextBox.Text = firstRule.FileOrFolderName;
                        }
                        break;

                    case DetectionRuleType.Registry:
                        DetectionTypeCombo.SelectedIndex = 1;
                        // Path contains the full registry path
                        var parts = firstRule.Path.Split(new[] { '\\' }, 2);
                        if (parts.Length > 0)
                        {
                            RegistryHiveCombo.Text = parts[0];
                            if (parts.Length > 1)
                                RegistryKeyTextBox.Text = parts[1];
                        }
                        // FileOrFolderName is the registry value name
                        RegistryValueNameTextBox.Text = firstRule.FileOrFolderName;
                        break;

                    case DetectionRuleType.MSI:
                        DetectionTypeCombo.SelectedIndex = 2;
                        // Path contains the product code
                        MSIProductCodeTextBox.Text = firstRule.Path;
                        // FileOrFolderName contains the version
                        MSIProductVersionTextBox.Text = firstRule.FileOrFolderName;
                        // Operator contains the comparison operator
                        MSIVersionOperatorCombo.Text = ConvertOperatorToDisplay(firstRule.Operator);
                        break;
                }
            }
        }

        private string ConvertOperatorToDisplay(string operatorValue)
        {
            return operatorValue?.ToLower() switch
            {
                "equal" or "=" or "==" => "Equals",
                "notequal" or "!=" or "<>" => "Not equal to",
                "greaterthan" or ">" => "Greater than",
                "greaterthanorequal" or ">=" => "Greater than or equal",
                "lessthan" or "<" => "Less than",
                "lessthanorequal" or "<=" => "Less than or equal",
                _ => "Equals"
            };
        }

        private void DetectionTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Hide all panels
            FileDetectionPanel.Visibility = Visibility.Collapsed;
            RegistryDetectionPanel.Visibility = Visibility.Collapsed;
            MSIDetectionPanel.Visibility = Visibility.Collapsed;
            ScriptDetectionPanel.Visibility = Visibility.Collapsed;

            // Show selected panel
            if (DetectionTypeCombo.SelectedItem is ComboBoxItem item)
            {
                switch (item.Tag?.ToString())
                {
                    case "File":
                        FileDetectionPanel.Visibility = Visibility.Visible;
                        break;
                    case "Registry":
                        RegistryDetectionPanel.Visibility = Visibility.Visible;
                        break;
                    case "MSI":
                        MSIDetectionPanel.Visibility = Visibility.Visible;
                        break;
                    case "Script":
                        ScriptDetectionPanel.Visibility = Visibility.Visible;
                        break;
                }
            }
        }

        private void BrowsePackageButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Package File",
                Filter = "Package Files (*.exe;*.msi;*.intunewin)|*.exe;*.msi;*.intunewin|All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _packagePath = openFileDialog.FileName;
                PackagePathTextBox.Text = System.IO.Path.GetFileName(_packagePath);
            }
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Icon",
                Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                _iconPath = openFileDialog.FileName;
                IconPathTextBox.Text = System.IO.Path.GetFileName(_iconPath);

                // Preview the icon
                try
                {
                    var bitmap = new BitmapImage(new Uri(_iconPath));
                    IconPreview.Source = bitmap;
                }
                catch { }
            }
        }

        private void AutoDetectMSIButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_packagePath) && _packagePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Auto-detect from MSI not yet implemented", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private List<DetectionRule> BuildDetectionRules()
        {
            var rules = new List<DetectionRule>();

            if (DetectionTypeCombo.SelectedItem is ComboBoxItem item)
            {
                switch (item.Tag?.ToString())
                {
                    case "File":
                        var filePath = FilePathTextBox.Text;
                        var directory = System.IO.Path.GetDirectoryName(filePath) ?? "";
                        var fileName = System.IO.Path.GetFileName(filePath);

                        var fileRule = new DetectionRule
                        {
                            Type = DetectionRuleType.File,
                            Path = directory,
                            FileOrFolderName = fileName,
                            CheckVersion = FileDetectionMethodCombo.Text == "Version"
                        };

                        if (fileRule.CheckVersion && !string.IsNullOrEmpty(FileVersionTextBox.Text))
                        {
                            fileRule.Operator = "equal";
                            // Store version in FileOrFolderName when checking version
                            // This might need adjustment based on your actual usage
                        }

                        rules.Add(fileRule);
                        break;

                    case "Registry":
                        var registryPath = $"{RegistryHiveCombo.Text}\\{RegistryKeyTextBox.Text}";
                        rules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.Registry,
                            Path = registryPath,
                            FileOrFolderName = RegistryValueNameTextBox.Text, // Value name
                            CheckVersion = false
                        });
                        break;

                    case "MSI":
                        rules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.MSI,
                            Path = MSIProductCodeTextBox.Text, // Product code
                            FileOrFolderName = MSIProductVersionTextBox.Text, // Version
                            CheckVersion = !string.IsNullOrEmpty(MSIProductVersionTextBox.Text),
                            Operator = ConvertDisplayToOperator(MSIVersionOperatorCombo.Text)
                        });
                        break;
                }
            }

            return rules;
        }

        private string ConvertDisplayToOperator(string displayValue)
        {
            return displayValue switch
            {
                "Equals" => "equal",
                "Not equal to" => "notequal",
                "Greater than" => "greaterthan",
                "Greater than or equal" => "greaterthanorequal",
                "Less than" => "lessthan",
                "Less than or equal" => "lessthanorequal",
                _ => "equal"
            };
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateButton.IsEnabled = false;
                UpdateButton.Content = "Updating...";
                StatusText.Text = "Processing update...";

                // Collect all the values
                NewDisplayName = DisplayNameTextBox.Text;
                NewDescription = DescriptionTextBox.Text;
                NewCategory = CategoryComboBox.Text;
                NewPackagePath = _packagePath;
                NewIconPath = _iconPath;
                NewDetectionRules = BuildDetectionRules();

                // Validate
                if (string.IsNullOrWhiteSpace(NewDisplayName))
                {
                    MessageBox.Show("Display name is required", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var intuneService = new IntuneService();

                // Update in Intune
                StatusText.Text = "Uploading to Intune...";
                var success = await intuneService.UpdateApplicationAsync(
                    _currentApp.Id,
                    NewDisplayName,
                    NewDescription,
                    NewCategory,
                    NewDetectionRules,
                    NewPackagePath,
                    NewIconPath
                );

                if (success)
                {
                    UpdateSuccessful = true;
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Failed to update application", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Update Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Update Application";
                StatusText.Text = "";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}