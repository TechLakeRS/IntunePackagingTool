using System;

namespace IntunePackagingTool
{
    /// <summary>
    /// Input model for creating new application packages
    /// Enhanced with MSI-specific properties for automatic detection
    /// </summary>
    public class ApplicationInfo
    {
        // Existing properties
        public string Manufacturer { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string InstallContext { get; set; } = "System";
        public string SourcesPath { get; set; } = "";
        public string ServiceNowSRI { get; set; } = "";

        // NEW: MSI-specific properties
        public string MsiProductCode { get; set; } = "";
        public string MsiProductVersion { get; set; } = "";
        public string MsiUpgradeCode { get; set; } = "";

        // Helper properties
        public bool IsMsiPackage => !string.IsNullOrEmpty(MsiProductCode);

        public string PackageType
        {
            get
            {
                if (IsMsiPackage) return "MSI";
                if (!string.IsNullOrEmpty(SourcesPath))
                {
                    var extension = System.IO.Path.GetExtension(SourcesPath).ToLower();
                    if (extension == ".exe") return "EXE";
                }
                return "Unknown";
            }
        }

        /// <summary>
        /// Gets a formatted display string for the package type
        /// </summary>
        public string PackageTypeDisplay
        {
            get
            {
                if (IsMsiPackage)
                    return $"MSI ({MsiProductCode})";

                return PackageType;
            }
        }

        /// <summary>
        /// Returns a detection rule summary for UI display
        /// </summary>
        public string DetectionRuleSummary
        {
            get
            {
                if (IsMsiPackage)
                    return $"MSI Product Code: {MsiProductCode} (Version {MsiProductVersion})";

                return $"File Detection: {Name}.exe";
            }
        }
    }
}