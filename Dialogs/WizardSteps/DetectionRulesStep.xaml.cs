using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using System.Diagnostics;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.WizardSteps
{
    public partial class DetectionRulesStep : UserControl
    {
        public event ValidationChangedEventHandler? ValidationChanged;

        // Reference to parent wizard for opening dialogs
        public IntuneUploadWizard? ParentWindow { get; set; }

        private ObservableCollection<DetectionRule>? _detectionRules;
        public ObservableCollection<DetectionRule>? DetectionRules
        {
            get => _detectionRules;
            set
            {
                if (_detectionRules != null)
                {
                    _detectionRules.CollectionChanged -= DetectionRules_CollectionChanged;
                }

                _detectionRules = value;

                if (_detectionRules != null)
                {
                    _detectionRules.CollectionChanged += DetectionRules_CollectionChanged;
                    DetectionRulesList.ItemsSource = _detectionRules;
                }

                UpdateValidation();
                UpdateRuleCount();
            }
        }

        public DetectionRulesStep()
        {
            InitializeComponent();
            InitializeMsiOperatorComboBox();
        }

        private void InitializeMsiOperatorComboBox()
        {
            // Initialize MSI operator combobox with available options
            MsiOperatorComboBox.Items.Clear();
            MsiOperatorComboBox.Items.Add(new ComboBoxItem { Content = "Greater than or equal to" });
            MsiOperatorComboBox.Items.Add(new ComboBoxItem { Content = "Equal to" });
            MsiOperatorComboBox.Items.Add(new ComboBoxItem { Content = "Greater than" });
            MsiOperatorComboBox.Items.Add(new ComboBoxItem { Content = "Less than" });
            MsiOperatorComboBox.Items.Add(new ComboBoxItem { Content = "Less than or equal to" });
            MsiOperatorComboBox.SelectedIndex = 0; // Default to "Greater than or equal to"
        }

        private void DetectionRules_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateValidation();
            UpdateRuleCount();
        }

        private void UpdateValidation()
        {
            bool isValid = (_detectionRules?.Count ?? 0) > 0;

            if (isValid)
            {
                NoRulesPanel.Visibility = Visibility.Collapsed;
                RuleCountIndicator.Background = new SolidColorBrush(Color.FromRgb(232, 245, 233)); // Light green
            }
            else
            {
                NoRulesPanel.Visibility = Visibility.Visible;
                RuleCountIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 235, 238)); // Light red
            }

            ValidationChanged?.Invoke(isValid);
        }

        private void UpdateRuleCount()
        {
            var count = _detectionRules?.Count ?? 0;
            RuleCountText.Text = count switch
            {
                0 => "No rules configured",
                1 => "1 rule configured",
                _ => $"{count} rules configured"
            };
        }

        #region Inline Detection Rule Editing

        // File Detection Methods
        private void AddFileDetectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Show inline editor instead of dialog
            FileDetectionEditor.Visibility = Visibility.Visible;
            RegistryDetectionEditor.Visibility = Visibility.Collapsed;
            MsiDetectionEditor.Visibility = Visibility.Collapsed;

            // Clear previous values
            FilePathTextBox.Text = "%ProgramFiles%";
            FileNameTextBox.Text = "";
            CheckVersionCheckBox.IsChecked = false;

            // Focus on file name field
            FileNameTextBox.Focus();
        }

        private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Select File for Detection Rule",
                    Filter = "All Files (*.*)|*.*|Executable Files (*.exe)|*.exe|DLL Files (*.dll)|*.dll",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    var selectedFile = openFileDialog.FileName;
                    var directory = Path.GetDirectoryName(selectedFile);
                    var fileName = Path.GetFileName(selectedFile);

                    if (directory != null)
                    {
                        string environmentPath = ConvertToEnvironmentPath(directory);
                        FilePathTextBox.Text = environmentPath;
                    }
                    else
                    {
                       
                        FilePathTextBox.Text = string.Empty;
                    }

                    FileNameTextBox.Text = fileName;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error selecting file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ConvertToEnvironmentPath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return absolutePath;

            // Convert common paths to environment variables
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);

            if (absolutePath.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Replace(programFiles, "%ProgramFiles%");
            }
            else if (absolutePath.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Replace(programFilesX86, "%ProgramFiles(x86)%");
            }
            else if (absolutePath.StartsWith(system32, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Replace(system32, "%System32%");
            }

            return absolutePath;
        }

        private void SaveFileDetectionRule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(FilePathTextBox.Text))
                {
                    MessageBox.Show("Please enter a file path.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(FileNameTextBox.Text))
                {
                    MessageBox.Show("Please enter a file name.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create new detection rule using existing properties
                var newRule = new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = FilePathTextBox.Text.Trim(),
                    FileOrFolderName = FileNameTextBox.Text.Trim(),
                    CheckVersion = CheckVersionCheckBox.IsChecked ?? false
                };

                _detectionRules?.Add(newRule);

                // Hide editor
                FileDetectionEditor.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding file detection rule: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelFileEditor_Click(object sender, RoutedEventArgs e)
        {
            FileDetectionEditor.Visibility = Visibility.Collapsed;
        }

        // Registry Detection Methods
        private void AddRegistryDetectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Show inline editor instead of dialog
            RegistryDetectionEditor.Visibility = Visibility.Visible;
            FileDetectionEditor.Visibility = Visibility.Collapsed;
            MsiDetectionEditor.Visibility = Visibility.Collapsed;

            // Clear previous values
            RegistryPathTextBox.Text = "HKEY_LOCAL_MACHINE\\SOFTWARE";
            RegistryValueNameTextBox.Text = "";
            RegistryDetectionTypeComboBox.SelectedIndex = 0; // "Exists"
            RegistryValueTextBox.Text = "";

            // Focus on path field
            RegistryPathTextBox.Focus();
        }

        private void BrowseRegistryButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Since there's no direct API to open regedit with return value,
                // we'll provide a helper dialog or open regedit for reference
                var result = MessageBox.Show(
                    "This will open Registry Editor for you to browse and find the registry key.\n\n" +
                    "After finding your desired registry key:\n" +
                    "1. Copy the full registry path\n" +
                    "2. Close Registry Editor\n" +
                    "3. Paste the path in the Registry Path field\n\n" +
                    "Continue?",
                    "Registry Browser Helper",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    // Open Registry Editor
                    Process.Start("regedit.exe");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Registry Editor: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveRegistryDetectionRule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(RegistryPathTextBox.Text))
                {
                    MessageBox.Show("Please enter a registry path.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create new detection rule using existing properties
                // Store registry value name in FileOrFolderName property
                var newRule = new DetectionRule
                {
                    Type = DetectionRuleType.Registry,
                    Path = RegistryPathTextBox.Text.Trim(),
                    FileOrFolderName = RegistryValueNameTextBox.Text.Trim(), // Reuse this for registry value name
                    CheckVersion = false // Not applicable for registry
                };

                _detectionRules?.Add(newRule);

                // Hide editor
                RegistryDetectionEditor.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding registry detection rule: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelRegistryEditor_Click(object sender, RoutedEventArgs e)
        {
            RegistryDetectionEditor.Visibility = Visibility.Collapsed;
        }

        // MSI Detection Methods
        private void AddMsiDetectionButton_Click(object sender, RoutedEventArgs e)
        {
            // Show inline editor
            MsiDetectionEditor.Visibility = Visibility.Visible;
            FileDetectionEditor.Visibility = Visibility.Collapsed;
            RegistryDetectionEditor.Visibility = Visibility.Collapsed;

            // NEW: Check if we have MSI info from the parent wizard
            if (ParentWindow != null && ParentWindow.ApplicationInfo != null && ParentWindow.ApplicationInfo.IsMsiPackage)
            {
                // Pre-populate with extracted MSI info
                MsiProductCodeTextBox.Text = ParentWindow.ApplicationInfo.MsiProductCode;
                MsiVersionValueTextBox.Text = ParentWindow.ApplicationInfo.MsiProductVersion;
                MsiVersionCheckYes.IsChecked = true;
                MsiVersionCheckNo.IsChecked = false;
                MsiOperatorComboBox.SelectedIndex = 0; // "Greater than or equal to"

                Debug.WriteLine($"Pre-populated MSI detection with Product Code: {ParentWindow.ApplicationInfo.MsiProductCode}");
            }
            else
            {
                // Clear previous values for manual entry
                MsiProductCodeTextBox.Text = "";
                MsiVersionCheckYes.IsChecked = false;
                MsiVersionCheckNo.IsChecked = true;
                MsiOperatorComboBox.SelectedIndex = 0;
                MsiVersionValueTextBox.Text = "1.0.0";
            }

            // Focus on product code field
            MsiProductCodeTextBox.Focus();
        }

        private void SaveMsiDetectionRule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(MsiProductCodeTextBox.Text))
                {
                    MessageBox.Show("Please enter an MSI product code.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Validate product code format (should be GUID-like)
                var productCode = MsiProductCodeTextBox.Text.Trim();
                if (!productCode.StartsWith("{") || !productCode.EndsWith("}"))
                {
                    MessageBox.Show("MSI product code should be in GUID format: {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Check if version validation is required
                bool checkVersion = MsiVersionCheckYes.IsChecked ?? false;
                if (checkVersion && string.IsNullOrWhiteSpace(MsiVersionValueTextBox.Text))
                {
                    MessageBox.Show("Please enter a version value when version check is enabled.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var operatorText = (MsiOperatorComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Greater than or equal to";

                // Create new detection rule using existing properties
                // For MSI: Path = product code, FileOrFolderName = version info if checking version
                var versionInfo = checkVersion ? $"{operatorText}:{MsiVersionValueTextBox.Text.Trim()}" : "";

                var newRule = new DetectionRule
                {
                    Type = DetectionRuleType.MSI, // You'll need to add this to your existing enum
                    Path = productCode, // Store product code in Path property
                    FileOrFolderName = versionInfo, // Store version info in FileOrFolderName if checking version
                    CheckVersion = checkVersion
                };

                _detectionRules?.Add(newRule);

                // Hide editor
                MsiDetectionEditor.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding MSI detection rule: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelMsiEditor_Click(object sender, RoutedEventArgs e)
        {
            MsiDetectionEditor.Visibility = Visibility.Collapsed;
        }

        // Helper method to browse installed MSI products - simplified version
        private void BrowseMsiProductsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Simple input dialog for now - you can enhance this later
                var result = MessageBox.Show(
                    "To find MSI Product Codes:\n\n" +
                    "1. Open Registry Editor (regedit)\n" +
                    "2. Navigate to HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\n" +
                    "3. Look for keys that start with { and end with } (these are MSI product codes)\n" +
                    "4. Copy the key name (product code) and paste it in the field\n\n" +
                    "Open Registry Editor now?",
                    "Find MSI Product Code",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start("regedit.exe");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Remove Detection Rule
        private void RemoveDetectionRule_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is DetectionRule ruleToRemove)
                {
                    var ruleText = $"{ruleToRemove.Type}: {ruleToRemove.Path}";
                    var result = MessageBox.Show(
                        $"Are you sure you want to remove this detection rule?\n\n{ruleText}",
                        "Remove Detection Rule",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        _detectionRules?.Remove(ruleToRemove);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing detection rule: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Public Methods for Wizard

        public bool IsValid()
        {
            return (_detectionRules?.Count ?? 0) > 0;
        }

        public int GetRuleCount()
        {
            return _detectionRules?.Count ?? 0;
        }

        #endregion
    }

    // Event delegate (if not already defined elsewhere)
    public delegate void ValidationChangedEventHandler(bool isValid);
}