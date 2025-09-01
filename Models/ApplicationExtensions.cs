namespace IntunePackagingTool.Models
{
    public static class ApplicationExtensions
    {
        /// <summary>
        /// Convert ApplicationInfo to IntuneApplication for display
        /// </summary>
        public static IntuneApplication ToIntuneApplication(this ApplicationInfo info)
        {
            return new IntuneApplication
            {
                DisplayName = $"{info.Manufacturer} {info.Name}",
                Version = info.Version,
                Publisher = info.Manufacturer,
                Description = $"Package created from {info.Name}",
                Category = "Uncategorized",
                Id = System.Guid.NewGuid().ToString(),
                LastModified = System.DateTime.Now
            };
        }

        /// <summary>
        /// Create ApplicationDetail from IntuneApplication (shell for loading)
        /// </summary>
        public static ApplicationDetail ToApplicationDetail(this IntuneApplication app)
        {
            return new ApplicationDetail
            {
                Id = app.Id,
                DisplayName = app.DisplayName,
                Version = app.Version,
                Publisher = app.Publisher,
                Description = app.Description,
                Category = app.Category,
                LastModified = app.LastModified
            };
        }

        /// <summary>
        /// Update ApplicationDetail from ApplicationInfo
        /// </summary>
        public static void UpdateFromApplicationInfo(this ApplicationDetail detail, ApplicationInfo info)
        {
            detail.DisplayName = $"{info.Manufacturer} {info.Name}";
            detail.Version = info.Version;
            detail.Publisher = info.Manufacturer;
            detail.InstallContext = info.InstallContext;
        }
    }
}