using System;

namespace IntunePackagingTool.Models
{
    /// <summary>
    /// Lightweight model for displaying applications in list views
    /// Used when fetching multiple apps from Intune API
    /// </summary>
    public class IntuneApplication : ApplicationBase
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public DateTime LastModified { get; set; }

       
    }
}