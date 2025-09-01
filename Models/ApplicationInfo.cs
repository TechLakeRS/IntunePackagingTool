using System;

namespace IntunePackagingTool
{
    /// <summary>
    /// Input model for creating new application packages
    /// Uses different property names for historical/UI reasons
    /// </summary>

    public class ApplicationInfo
    {
        public string Manufacturer { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string InstallContext { get; set; } = "System";
        public string SourcesPath { get; set; } = "";
        public string ServiceNowSRI { get; set; } = "";
    }

}
