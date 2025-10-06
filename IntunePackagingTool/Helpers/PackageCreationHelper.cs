using IntunePackagingTool.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IntunePackagingTool.Services;
namespace IntunePackagingTool.Helpers
{
    public static class PackageCreationHelper
    {
        /// <summary>
        /// Collects and validates PSADT options from the UI
        /// </summary>
        public static PSADTOptions? CollectPSADTOptions(
            bool userInstall,
            PSADTOptions? currentOptions,
            string packageType)
        {
            // If options were configured via dialog, use those as base
            var options = currentOptions ?? new PSADTOptions
            {
                PackageType = packageType
            };

            // Apply UserInstall setting from MainWindow checkbox
            options.UserInstall = userInstall;

            // Validate conflicting options
            if (options.UserInstall && options.AllUsersInstall)
            {
                MessageBox.Show(
                    "Cannot use both 'User Install' and 'Install for All Users'.\n\n" +
                    "User Install is checked on the main page, but 'Install for All Users' is configured in PSADT Options.\n\n" +
                    "Please uncheck one of these options.",
                    "Conflicting Installation Options",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            return options;
        }

        /// <summary>
        /// Shows success UI after package creation
        /// </summary>
        public static void ShowPackageSuccess(
            Border packageStatusPanel,
            TextBlock packageStatusText,
            TextBlock statusText,
            TextBlock packagePathText,
            Button openPackageFolderButton,
            System.Windows.Controls.ProgressBar progressBar,
            string currentPackagePath,
            PSADTOptions? psadtOptions)
        {
            packageStatusPanel.Visibility = Visibility.Visible;
            packageStatusText.Text = "✅ Package created successfully!";
            packagePathText.Text = currentPackagePath;
            openPackageFolderButton.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;

            int enabledFeatures = psadtOptions != null ? CountEnabledFeatures(psadtOptions) : 0;
            string featuresText = enabledFeatures > 0 ? $" with {enabledFeatures} PSADT cheatsheet functions" : "";
            statusText.Text = $"Package created successfully{featuresText} • {DateTime.Now:HH:mm:ss}";
        }

        /// <summary>
        /// Counts enabled boolean features in PSADTOptions
        /// </summary>
        public static int CountEnabledFeatures(PSADTOptions options)
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

        /// <summary>
        /// Updates PSADT summary panel in UI
        /// </summary>
        public static void UpdatePSADTSummary(
            PSADTOptions? currentOptions,
            TextBlock statusText,
            TextBlock selectedOptionsCount,
            TextBlock estimatedScriptSize,
            TextBlock injectionCount,
            UIElement summaryPanel,
            Button configureButton)
        {
            if (currentOptions == null)
            {
                statusText.Text = "Not configured - Click to select deployment options";
                summaryPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // Count selected options
            int count = CountEnabledFeatures(currentOptions);

            // Update UI
            statusText.Text = $"Configured - {count} options selected";
            statusText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));

            selectedOptionsCount.Text = count.ToString();
            estimatedScriptSize.Text = $"~{8 + (count * 2)}KB";
            injectionCount.Text = Math.Ceiling(count / 2.0).ToString();

            summaryPanel.Visibility = Visibility.Visible;
            configureButton.Content = "Modify Options";
        }

        /// <summary>
        /// Validates package inputs before generation
        /// </summary>
        public static bool ValidatePackageInputs(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName))
            {
                MessageBox.Show("Please enter an Application Name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates ApplicationInfo from UI inputs
        /// </summary>
        public static ApplicationInfo CreateApplicationInfo(
            string appName,
            string manufacturer,
            string version,
            string sourcesPath,
            Services.MsiInfoService.MsiInfo? msiInfo)
        {
            var appInfo = new ApplicationInfo
            {
                Name = appName.Trim(),
                Manufacturer = string.IsNullOrWhiteSpace(manufacturer) ? "Unknown" : manufacturer.Trim(),
                Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version.Trim(),
                SourcesPath = sourcesPath.Trim(),
                ServiceNowSRI = ""
            };

            // Include MSI information if available
            if (msiInfo != null && msiInfo.IsValid)
            {
                appInfo.MsiProductCode = msiInfo.ProductCode;
                appInfo.MsiProductVersion = msiInfo.ProductVersion;
                appInfo.MsiUpgradeCode = msiInfo.UpgradeCode;
            }

            return appInfo;
        }
    }
}
