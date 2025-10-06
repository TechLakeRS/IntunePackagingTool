using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;           
using System.Windows;

namespace IntunePackagingTool.Models
{
    /// <summary>
    /// Full application details from Microsoft Graph API
    /// Extends IntuneApplication with all additional properties
    /// </summary>
    public class ApplicationDetail : IntuneApplication, INotifyPropertyChanged
    {
        
        public string InstallContext { get; set; } = "System";
        public string InstallCommand { get; set; } = "Deploy-Application.exe Install";
        public string UninstallCommand { get; set; } = "Deploy-Application.exe Uninstall";
        public string NetworkSharePath { get; set; } = "";

        private byte[]? _iconData;
        private BitmapImage? _iconImage;
        private static IntunePackagingTool.Services.IconCacheService? _iconCache;

        public byte[]? IconData
        {
            get => _iconData;
            set
            {
                _iconData = value;
                OnPropertyChanged();
                if (value != null && value.Length > 0)
                {
                    _ = LoadIconAsync(); // Fire and forget
                }
            }
        }

        public string IconType { get; set; } = string.Empty;


        public BitmapImage? IconImage
        {
            get => _iconImage;
            private set
            {
                _iconImage = value;
                OnPropertyChanged();
            }
        }

        private async Task LoadIconAsync()
        {
            try
            {
                // Initialize cache service if needed (lazy singleton)
                _iconCache ??= new IntunePackagingTool.Services.IconCacheService();

                // Try to get from disk cache or create and cache
                var icon = await _iconCache.GetOrCacheIconAsync(Id, _iconData);

                // Update UI on UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IconImage = icon;
                    if (icon != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"✅ Icon loaded: {icon.PixelWidth}x{icon.PixelHeight}");
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Failed to load icon: {ex.Message}");
                IconImage = null;
            }
        }
       
       
        // Extended Properties from Microsoft Graph API
        public string Owner { get; set; } = "";
        public string Developer { get; set; } = "";
        public string Notes { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public DateTime CreatedDateTime { get; set; } = DateTime.MinValue;
        public DateTime LastModifiedDateTime { get; set; } = DateTime.MinValue;
        public bool IsFeatured { get; set; } = false;
        public bool IsAssigned { get; set; } = false;
        public string PrivacyInformationUrl { get; set; } = "";
        public string InformationUrl { get; set; } = "";
        public string UploadState { get; set; } = string.Empty;
        public string PublishingState { get; set; } = "";
        public string ApplicableArchitectures { get; set; } = "";
        public string AllowedArchitectures { get; set; } = "";
        public int? MinimumFreeDiskSpaceInMB { get; set; }
        public int? MinimumMemoryInMB { get; set; }
        public int? MinimumNumberOfProcessors { get; set; }
        public int? MinimumCpuSpeedInMHz { get; set; }
        public string SetupFilePath { get; set; } = "";
        public string MinimumSupportedWindowsRelease { get; set; } = "";
        public bool AllowAvailableUninstall { get; set; }

        // Collections
        public List<DetectionRule> DetectionRules { get; set; } = new List<DetectionRule>();
        public List<AssignedGroup> AssignedGroups { get; set; } = new List<AssignedGroup>();
        public List<RequirementRule> RequirementRules { get; set; } = new List<RequirementRule>();
        public List<ReturnCode> ReturnCodes { get; set; } = new List<ReturnCode>();
        public List<string> RoleScopeTagIds { get; set; } = new List<string>();

        // Computed Properties for UI Display
        public string SizeFormatted
        {
            get
            {
                if (Size > 1024 * 1024 * 1024) // GB
                    return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
                else if (Size > 1024 * 1024) // MB
                    return $"{Size / (1024.0 * 1024):F1} MB";
                else if (Size > 1024) // KB
                    return $"{Size / 1024.0:F1} KB";
                else if (Size > 0)
                    return $"{Size} B";
                else
                    return "Unknown";
            }
        }

        public string CreatedDateFormatted => CreatedDateTime != DateTime.MinValue ? CreatedDateTime.ToString("MMM dd, yyyy HH:mm") : "Unknown";
        public string LastModifiedFormatted => LastModifiedDateTime != DateTime.MinValue ? LastModifiedDateTime.ToString("MMM dd, yyyy HH:mm") : "Unknown";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Keep these supporting classes in the same file
    public class GroupAssignmentIds
    {
        public string SystemInstallId { get; set; } = string.Empty;
        public string UserInstallId { get; set; } = string.Empty;
        public string SystemUninstallId { get; set; } = string.Empty;
        public string UserUninstallId { get; set; } = string.Empty;

        public int Count => new[] { SystemInstallId, UserInstallId, SystemUninstallId, UserUninstallId }
            .Count(id => !string.IsNullOrEmpty(id));
    }

    public class AssignedGroup
    {
        public string GroupId { get; set; }
        public string GroupName { get; set; } = "";
        public string AssignmentType { get; set; } = "";
    }

    public class RequirementRule
    {
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public class ReturnCode
    {
        public int Code { get; set; }
        public string Type { get; set; } = "";
        public string Description => $"Exit Code {Code}: {Type}";
    }
}