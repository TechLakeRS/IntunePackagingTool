// Models/WingetModels.cs
using System;
using System.Collections.Generic;

namespace IntunePackagingTool.Models
{
    // Model for displaying packages in the Winget catalog list
    public class WingetPackage
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string Homepage { get; set; } = "";
        public string License { get; set; } = "";
        public string InstallerType { get; set; } = "";
        public string Source { get; set; } = "winget";
        public bool IsInstalled { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    // Detailed info when creating a package from Winget
    public class WingetPackageInfo
    {
        public string WingetId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string Version { get; set; } = "";
        public string Description { get; set; } = "";
        public string Homepage { get; set; } = "";
        public string License { get; set; } = "";
        public string InstallerType { get; set; } = "";
    }

    // Options for package creation from Winget
    public class PackageOptions
    {
        public bool RemoveOldVersions { get; set; } = true;
        public bool CloseApps { get; set; } = true;
        public bool CreateShortcuts { get; set; } = true;
        public string InstallLocation { get; set; } = "";
    }

    // Result of package creation
    public class PackageResult
    {
        public bool Success { get; set; }
        public string PackagePath { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
        public WingetPackageInfo? PackageInfo { get; set; }
    }
}