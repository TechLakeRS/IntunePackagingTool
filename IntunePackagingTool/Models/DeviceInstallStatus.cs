using IntunePackagingTool.Services;

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
        public string? HexErrorCode { get; set; } = "";
        public string? AppVersion { get; set; } = "";
        public DateTime? LastSyncDateTime { get; set; }

        // Enhanced computed properties
        public string StatusIcon => InstallState?.ToLower() switch
        {
            "installed" => ErrorCodeMapper.IsSuccessCode(ErrorCode) ? "✅" : "⚠️",
            "failed" => "❌",
            "pending" => "⏳",
            "notinstalled" => "⚫",
            "notapplicable" => "➖",
            _ => "❓"
        };

        public string FormattedLastSync => LastSyncDateTime?.ToString("MMM dd, yyyy HH:mm") ?? "Never";

        public string ErrorCodeDisplay
        {
            get
            {
                if (!ErrorCode.HasValue || ErrorCode == 0)
                    return "";

                if (!string.IsNullOrEmpty(HexErrorCode))
                    return HexErrorCode;

                return $"0x{ErrorCode.Value:X8}";
            }
        }

        // Use the mapper for detailed description
        public string DetailedStatus => ErrorCodeMapper.GetErrorDescription(ErrorCode);

        // Short status for grid display
        public string StatusSummary => string.IsNullOrEmpty(InstallStateDetail)
            ? ErrorCodeMapper.GetShortErrorDescription(ErrorCode)
            : InstallStateDetail;

        // Recommended action
        public string RecommendedAction => ErrorCodeMapper.GetRecommendedAction(ErrorCode);
    }
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
