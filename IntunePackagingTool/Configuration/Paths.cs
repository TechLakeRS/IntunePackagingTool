// Configuration/Paths.cs
using IntunePackagingTool.Services;

namespace IntunePackagingTool.Configuration
{
    public static class Paths
    {
        private static SettingsService? _settingsService;

        private static SettingsService GetSettings()
        {
            if (_settingsService == null)
            {
                _settingsService = new SettingsService();
            }
            return _settingsService;
        }

        // Network paths from settings
        public static string NetworkRoot => GetSettings().Settings.NetworkPaths.NetworkRoot;
        public static string IntuneApplications => GetSettings().Settings.NetworkPaths.IntuneApplications;
        public static string Tools => GetSettings().Settings.NetworkPaths.Tools;
        public static string Scripts => GetSettings().Settings.NetworkPaths.Scripts;
        public static string IntuneWinAppUtil => GetSettings().Settings.NetworkPaths.IntuneWinAppUtil;
        public static string PSADTTemplate => GetSettings().Settings.NetworkPaths.PSADTTemplate;
        public static string HyperVScript => GetSettings().Settings.NetworkPaths.HyperVScript;
        public static string VMScript => GetSettings().Settings.NetworkPaths.VMScript;

        // WDAC Hyper-V Configuration from settings
        public static class WDACHyperV
        {
            public static string Host => GetSettings().Settings.WDACHyperV.Host;
            public static string VMName => GetSettings().Settings.WDACHyperV.VMName;
            public static string SnapshotName => GetSettings().Settings.WDACHyperV.SnapshotName;
            public static string PsExecPath => GetSettings().Settings.WDACHyperV.PsExecPath;
        }
    }
}