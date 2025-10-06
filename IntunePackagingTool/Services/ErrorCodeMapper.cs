// Create a new file: Services/ErrorCodeMapper.cs
using System.Collections.Generic;

namespace IntunePackagingTool.Services
{
    public static class ErrorCodeMapper
    {
        private static readonly Dictionary<int, string> ErrorCodeDescriptions = new Dictionary<int, string>
        {
            // Success codes
            { 0, "Installation completed successfully" },
            { 3010, "Installation successful - Restart required" },
            { 1641, "Installation successful - Restart initiated" },
            { 3011, "Installation successful - Restart required (custom)" },
            
            // Common MSI errors (1xxx range)
            { 1602, "User cancelled the installation" },
            { 1603, "Fatal error during installation" },
            { 1605, "Product not found - may be already uninstalled" },
            { 1618, "Another installation is already in progress" },
            { 1619, "Installation package could not be opened - verify package exists and is accessible" },
            { 1620, "Installation package is invalid or corrupted" },
            { 1621, "Could not start Windows Installer service" },
            { 1625, "Installation prohibited by system policy" },
            { 1628, "Invalid or unknown product specified" },
            { 1633, "This platform is not supported" },
            { 1638, "Product version already installed" },
            { 1639, "Invalid command line argument" },
            { 1640, "Only administrators can perform this operation" },
            { 1643, "The update package is not permitted by software restriction policy" },
            { 1644, "Installation suspended, incomplete" },
            { 1645, "Installation not permitted for current user" },
            { 1646, "The patch package is not a removable patch package" },
            { 1647, "The patch is not applied to this product" },
            { 1648, "No valid sequence could be found for the set of patches" },
            { 1649, "Patch removal was disallowed by policy" },
            
            // System errors
            { 2, "File not found" },
            { 3, "Path not found" },
            { 5, "Access denied - insufficient permissions" },
            { 32, "File in use by another process" },
            { 50, "The request is not supported" },
            { 87, "Invalid parameter" },
            { 112, "Insufficient disk space" },
            { 1053, "Service did not respond in time" },
            { 1054, "Failed to create service thread" },
            { 1060, "The specified service does not exist" },
            { 1073, "The specified service already exists" },
            { 1074, "System is shutting down" },
            { 1223, "Operation cancelled by user" },
            { 1327, "Invalid account or password" },
            { 1385, "Logon failure - user not granted requested logon type" },
            { 1396, "Target account name is incorrect" },
            { 1450, "Insufficient system resources" },
            { 1920, "Service failed to start - file not found" },
            { 1921, "Service failed to start - insufficient privileges" },
            { 1923, "Service failed to start - account information invalid" },
            
            // HRESULT errors (negative values)
            { -2147024891, "Access denied (0x80070005)" },
            { -2147024894, "File not found (0x80070002)" },
            { -2147024893, "Path not found (0x80070003)" },
            { -2147024882, "Out of memory (0x8007000E)" },
            { -2147024809, "Invalid parameter (0x80070057)" },
            { -2147023673, "RPC server unavailable (0x800706B7)" },
            { -2147023170, "Remote procedure call failed (0x800706BE)" },
            
            // Intune specific errors
            { -2016281112, "Application failed to install - check installation logs" },
            { -2016281111, "Application installation cancelled" },
            { -2016281110, "Application download failed" },
            { -2016281109, "Application hash mismatch - corrupted download" },
            { -2016281108, "Application prerequisites not met" },
            { -2016281107, "PowerShell script execution failed" },
            { -2016281106, "Win32 app installation failed" },
            { -2016281105, "Application detection failed after installation" },
            { -2016281104, "Application timeout during installation" },
            { -2016281103, "Application dependency failed" },
            { -2016281102, "Supersedence conflict detected" },
            { -2016281101, "VPN connection required but not available" },
            { -2016281100, "Company Portal required but not installed" },

            { -2016345060, "Installation timed out - exceeded maximum duration" },
            { -2016345059, "Content download failed - network issue" },
            { -2016345058, "Failed to decrypt content" },
            { -2016345057, "Failed to validate application signature" },
            { -2016345056, "Device not compliant with requirements" },
            { -2016345055, "User not licensed for application" },
            { -2016345054, "Application not available for this device type" },
            { -2016345053, "Application blocked by conditional access" },
            { -2016345052, "ESP (Enrollment Status Page) timeout" },
            { -2016345051, "Application conflicts with installed software" },
            
            // Windows Update / Store errors
            { -2145124329, "Windows Update in progress" },
            { -2145124330, "Pending restart blocking installation" },
            { -2145124331, "Store app update required" },
            { -2145124332, "Windows feature update required" },
            
            // Network related
            { -2147012889, "Network timeout (0x80072EE7)" },
            { -2147012867, "Cannot connect to server (0x80072EFD)" },
            { -2147012866, "Connection aborted (0x80072EFE)" },
            { -2147012865, "Connection reset by peer (0x80072EFF)" },
            { -2147012894, "Certificate error (0x80072EE2)" },
            { -2147012891, "Invalid certificate (0x80072EE5)" },
            
            // PowerShell/Script errors
            { 1, "PowerShell script returned error" },
            { 259, "No more data available - script incomplete" },
            
            { -532462766, "PowerShell execution policy blocking script" }
        };

        public static string GetErrorDescription(int? errorCode)
        {
            if (!errorCode.HasValue || errorCode == 0)
                return "No error";

            if (ErrorCodeDescriptions.TryGetValue(errorCode.Value, out var description))
                return description;

            // Handle ranges of errors
            if (errorCode > 0)
            {
                return errorCode.Value switch
                {
                    var n when n >= 13000 && n <= 13999 => $"MSI internal error ({errorCode})",
                    var n when n >= 14000 && n <= 14999 => $"MSI configuration error ({errorCode})",
                    var n when n >= 15000 && n <= 15999 => $"MSI validation error ({errorCode})",
                    var n when n >= 16000 && n <= 16999 => $"Windows Installer service error ({errorCode})",
                    var n when n >= 17000 && n <= 17999 => $"Application compatibility error ({errorCode})",
                    _ => $"Installation failed with error code {errorCode}"
                };
            }
            else
            {
                // HRESULT error - format as hex
                return $"Installation failed (0x{errorCode:X8})";
            }
        }

        public static string GetShortErrorDescription(int? errorCode)
        {
            if (!errorCode.HasValue || errorCode == 0)
                return "";

            return errorCode.Value switch
            {
                3010 or 1641 or 3011 => "Restart Required",
                1602 or 1223 => "User Cancelled",
                1603 => "Fatal Error",
                1618 => "Another Install Running",
                1619 or 1620 => "Package Error",
                1625 or 1640 or 1645 => "Access Denied",
                112 => "Disk Space",
                -2016281112 or -2016281106 => "Install Failed",
                -2016345060 or -2016281104 => "Timeout",
                -2016345059 or -2147012889 => "Network Error",
                -2147024891 or 5 => "Access Denied",
                _ => "Error"
            };
        }

        public static bool IsSuccessCode(int? errorCode)
        {
            if (!errorCode.HasValue)
                return true;

            return errorCode.Value switch
            {
                0 or 3010 or 1641 or 3011 => true,
                _ => false
            };
        }

        public static string GetRecommendedAction(int? errorCode)
        {
            if (!errorCode.HasValue || errorCode == 0)
                return "";

            return errorCode.Value switch
            {
                3010 or 1641 => "Schedule a device restart",
                1602 or 1223 => "Re-deploy with user communication",
                1603 => "Check application logs for detailed error",
                1618 => "Wait for current installation to complete",
                1619 or 1620 => "Re-download the application package",
                1625 or 1640 or 1645 => "Verify user has admin rights",
                112 => "Free up disk space on target device",
                1638 => "Application already installed - no action needed",
                -2016281112 => "Review installation logs in C:\\ProgramData\\Microsoft\\IntuneManagementExtension\\Logs",
                -2016345060 => "Increase installation timeout or optimize installer",
                -2016345059 => "Check network connectivity and proxy settings",
                -2147024891 or 5 => "Run installation with elevated privileges",
                _ => "Review installation logs for more details"
            };
        }
    }
}