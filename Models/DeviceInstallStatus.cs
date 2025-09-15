using System;

namespace IntunePackagingTool.Models
{
    public class DeviceInstallStatus
    {
        public string DeviceId { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string UserPrincipalName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string Platform { get; set; } = "";
        public string InstallState { get; set; } = "";
        public string InstallStateDetail { get; set; } = "";
        public int? ErrorCode { get; set; }
        public string? HexErrorCode { get; set; } = "";  // New - hex version of error code
        public string? AppVersion { get; set; } = "";     // New - version of app installed
        public DateTime? LastSyncDateTime { get; set; }

        // These aren't in the API response but keeping them won't hurt
        public string Id { get; set; } = "";  // Can map DeviceId to this
        public string OSDescription { get; set; } = "";  // Not available from this endpoint
        public string OSVersion { get; set; } = "";      // Not available from this endpoint

        // Computed properties - these are perfect
        public string StatusIcon => InstallState?.ToLower() switch
        {
            "installed" => "✅",
            "failed" => "❌",
            "pending" => "⏳",
            "notinstalled" => "⚫",
            "notapplicable" => "➖",  // Added this state
            _ => "❓"
        };

        public string FormattedLastSync => LastSyncDateTime?.ToString("MMM dd, yyyy HH:mm") ?? "Never";

        // Add error description if you want
       
    }

    public class InstallationStatistics
    {
        public int TotalDevices { get; set; }
        public int SuccessfulInstalls { get; set; }
        public int FailedInstalls { get; set; }
        public int PendingInstalls { get; set; }
        public int NotInstalled { get; set; }
        public int Unknown { get; set; }

        public double SuccessRate => TotalDevices > 0
            ? Math.Round((double)SuccessfulInstalls / TotalDevices * 100, 1)
            : 0;

        public double FailureRate => TotalDevices > 0
            ? Math.Round((double)FailedInstalls / TotalDevices * 100, 1)
            : 0;
    }
}