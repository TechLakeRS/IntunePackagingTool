using IntunePackagingTool.Models;
using IntunePackagingTool.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool.Views
{
    public partial class PSADTConfigDialog : Window
    {
        public PSADTOptions SelectedOptions { get; private set; }
        private string _packageType = "MSI";

        public PSADTConfigDialog()
        {
            InitializeComponent();
            SelectedOptions = new PSADTOptions();
            UpdateSelectedCount();
        }

        public void SetPackageInfo(string manufacturer, string appName, string version, string packageType)
        {
            _packageType = packageType;
            PackageInfoText.Text = $"Configure for: {manufacturer} {appName} v{version} ({packageType})";
        }

        public void LoadOptions(PSADTOptions options)
        {
            SelectedOptions = options;

            // Load all checkboxes
            SilentInstallCheck.IsChecked = options.SilentInstall;
            SuppressRestartCheck.IsChecked = options.SuppressRestart;
            AllUsersInstallCheck.IsChecked = options.AllUsersInstall;
            VerboseLoggingCheck.IsChecked = options.VerboseLogging;
            UserInstallCheck.IsChecked = options.UserInstall;

            CloseRunningAppsCheck.IsChecked = options.CloseRunningApps;
            AllowUserDeferralsCheck.IsChecked = options.AllowUserDeferrals;
            CheckDiskSpaceCheck.IsChecked = options.CheckDiskSpace;
            ShowProgressCheck.IsChecked = options.ShowProgress;

            CheckDotNetCheck.IsChecked = options.CheckDotNet;
            ImportCertificatesCheck.IsChecked = options.ImportCertificates;
            CheckVCRedistCheck.IsChecked = options.CheckVCRedist;
            RegisterDLLsCheck.IsChecked = options.RegisterDLLs;

            CopyToAllUsersCheck.IsChecked = options.CopyToAllUsers;
            SetHKCUAllUsersCheck.IsChecked = options.SetHKCUAllUsers;
            SetCustomRegistryCheck.IsChecked = options.SetCustomRegistry;
            CopyConfigFilesCheck.IsChecked = options.CopyConfigFiles;

            DesktopShortcutCheck.IsChecked = options.DesktopShortcut;
            StartMenuEntryCheck.IsChecked = options.StartMenuEntry;
            RemovePreviousVersionsCheck.IsChecked = options.RemovePreviousVersions;
            CreateInstallMarkerCheck.IsChecked = options.CreateInstallMarker;

            UpdateSelectedCount();
        }

        private void PresetBasic_Click(object sender, RoutedEventArgs e)
        {
            ClearAll();
            SilentInstallCheck.IsChecked = true;
            AllUsersInstallCheck.IsChecked = true;
            VerboseLoggingCheck.IsChecked = true;
            UpdateSelectedCount();
        }

        private void PresetSilent_Click(object sender, RoutedEventArgs e)
        {
            ClearAll();
            SilentInstallCheck.IsChecked = true;
            SuppressRestartCheck.IsChecked = true;
            AllUsersInstallCheck.IsChecked = true;
            VerboseLoggingCheck.IsChecked = true;
            CloseRunningAppsCheck.IsChecked = true;
            CreateInstallMarkerCheck.IsChecked = true;
            UpdateSelectedCount();
        }

        private void PresetEnterprise_Click(object sender, RoutedEventArgs e)
        {
            // Select all common enterprise options
            SilentInstallCheck.IsChecked = true;
            SuppressRestartCheck.IsChecked = true;
            AllUsersInstallCheck.IsChecked = true;
            VerboseLoggingCheck.IsChecked = true;
            CloseRunningAppsCheck.IsChecked = true;
            CheckDiskSpaceCheck.IsChecked = true;
            CheckDotNetCheck.IsChecked = true;
            SetCustomRegistryCheck.IsChecked = true;
            DesktopShortcutCheck.IsChecked = true;
            StartMenuEntryCheck.IsChecked = true;
            RemovePreviousVersionsCheck.IsChecked = true;
            CreateInstallMarkerCheck.IsChecked = true;
            UpdateSelectedCount();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            ClearAll();
            UpdateSelectedCount();
        }

        private void ClearAll()
        {
            // Clear all checkboxes
            foreach (var child in FindVisualChildren<CheckBox>(this))
            {
                child.IsChecked = false;
            }
        }

        private void UpdateSelectedCount()
        {
            int count = 0;
            foreach (var child in FindVisualChildren<CheckBox>(this))
            {
                if (child.IsChecked == true)
                    count++;
            }
            SelectedCountText.Text = $"{count} options";
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            // Collect all options
            SelectedOptions = new PSADTOptions
            {
                PackageType = _packageType,

                // Installation
                SilentInstall = SilentInstallCheck.IsChecked ?? false,
                SuppressRestart = SuppressRestartCheck.IsChecked ?? false,
                AllUsersInstall = AllUsersInstallCheck.IsChecked ?? false,
                VerboseLogging = VerboseLoggingCheck.IsChecked ?? false,
                UserInstall = UserInstallCheck?.IsChecked ?? false,
                WaitForProcessCompletion = WaitForProcessCompletionCheck.IsChecked ?? false,
                ImportRegFile = ImportRegFileCheck.IsChecked ?? false,
                UninstallPreviousByCode = UninstallPreviousByCodeCheck.IsChecked ?? false,

                // User Interaction
                CloseRunningApps = CloseRunningAppsCheck.IsChecked ?? false,
                AllowUserDeferrals = AllowUserDeferralsCheck.IsChecked ?? false,
                CheckDiskSpace = CheckDiskSpaceCheck.IsChecked ?? false,
                ShowProgress = ShowProgressCheck.IsChecked ?? false,

                // Prerequisites
                CheckDotNet = CheckDotNetCheck.IsChecked ?? false,
                ImportCertificates = ImportCertificatesCheck.IsChecked ?? false,
                CheckVCRedist = CheckVCRedistCheck.IsChecked ?? false,
                RegisterDLLs = RegisterDLLsCheck.IsChecked ?? false,

                // Registry & Files
                CopyToAllUsers = CopyToAllUsersCheck.IsChecked ?? false,
                SetHKCUAllUsers = SetHKCUAllUsersCheck.IsChecked ?? false,
                SetCustomRegistry = SetCustomRegistryCheck.IsChecked ?? false,
                CopyConfigFiles = CopyConfigFilesCheck.IsChecked ?? false,
                RemoveSpecificFiles = RemoveSpecificFilesCheck.IsChecked ?? false, 
                RemoveEmptyFolders = RemoveEmptyFoldersCheck.IsChecked ?? false,
                ModifyFilePermissions = ModifyFilePermissionsCheck.IsChecked ?? false,

                // Shortcuts & Cleanup
                DesktopShortcut = DesktopShortcutCheck.IsChecked ?? false,
                StartMenuEntry = StartMenuEntryCheck.IsChecked ?? false,
                RemovePreviousVersions = RemovePreviousVersionsCheck.IsChecked ?? false,
                CreateInstallMarker = CreateInstallMarkerCheck.IsChecked ?? false,
                AddToPath = AddToPathCheck.IsChecked ?? false,
                UnregisterDLLs = UnregisterDLLsCheck.IsChecked ?? false,
                ImportDrivers = ImportDriversCheck.IsChecked ?? false,
             
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }
    }
}