using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IntunePackagingTool.Utilities
{
    public class IntuneDiagnosticsService
    {
        private readonly string _scriptPath;

        public IntuneDiagnosticsService()
        {
            // Path to the Scripts folder in your project
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _scriptPath = Path.Combine(baseDir, "Scripts", "Get-IntuneManagementExtensionDiagnostics.ps1");

            if (!File.Exists(_scriptPath))
            {
                // Try alternative location
                _scriptPath = Path.Combine(baseDir, "Get-IntuneManagementExtensionDiagnostics.ps1");

                if (!File.Exists(_scriptPath))
                {
                    throw new FileNotFoundException(
                        $"Script not found at: {_scriptPath}\n\n" +
                        "Please download the script from GitHub and place it in the Scripts folder.");
                }
            }
        }

        // IntuneDiagnosticsService.cs - Modified method
        public async Task<IntuneDiagnosticsResult> GetDiagnosticsAsync(
            string computerName,
            bool onlineCheck = true,
            bool showAllTimelineEntries = false,
            Action<string> outputCallback = null)
        {
            return await Task.Run(() =>
            {
                var result = new IntuneDiagnosticsResult
                {
                    ComputerName = computerName,
                    TimeStamp = DateTime.Now
                };

                try
                {
                    bool isLocal = string.IsNullOrEmpty(computerName) ||
                                  computerName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                                  computerName.Equals("localhost", StringComparison.OrdinalIgnoreCase);

                    if (isLocal)
                    {
                        // For local machine, directly read log files
                        var localPath = @"C:\ProgramData\Microsoft\IntuneManagementExtension\Logs";
                        if (Directory.Exists(localPath))
                        {
                            var logFiles = Directory.GetFiles(localPath, "*.log");
                            foreach (var logFile in logFiles)
                            {
                                var fileInfo = new FileInfo(logFile);
                                result.LogFiles.Add(new LogFileInfo
                                {
                                    Name = fileInfo.Name,
                                    Path = logFile,  // Local path
                                    Size = fileInfo.Length,
                                    LastModified = fileInfo.LastWriteTime
                                });
                            }
                        }
                        result.IsSuccess = true;
                    }
                    else
                    {
                        // For remote computers, store the UNC path
                        var remotePath = $@"\\{computerName}\c$\ProgramData\Microsoft\IntuneManagementExtension\Logs";

                        outputCallback?.Invoke($"Checking remote path: {remotePath}");

                        if (Directory.Exists(remotePath))
                        {
                            var logFiles = Directory.GetFiles(remotePath, "*.log");
                            foreach (var logFile in logFiles)
                            {
                                var fileInfo = new FileInfo(logFile);
                                result.LogFiles.Add(new LogFileInfo
                                {
                                    Name = fileInfo.Name,
                                    Path = logFile,  // Store the UNC path
                                    Size = fileInfo.Length,
                                    LastModified = fileInfo.LastWriteTime
                                });
                                outputCallback?.Invoke($"Found: {fileInfo.Name}");
                            }
                            result.IsSuccess = true;
                        }
                        else
                        {
                            result.Errors.Add($"Cannot access remote path: {remotePath}");
                            result.Errors.Add("Ensure you have administrative access and the admin$ share is enabled.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to get diagnostics: {ex.Message}");
                    result.IsSuccess = false;
                }

                return result;
            });
        }

        private void ProcessRemoteLogFiles(string logDirectory, IntuneDiagnosticsResult result)
        {
            try
            {
                // Process log files in the directory
                var logFiles = Directory.GetFiles(logDirectory, "*.log");

                foreach (var logFile in logFiles)
                {
                    var fileInfo = new FileInfo(logFile);

                    result.LogFiles.Add(new LogFileInfo
                    {
                        Name = fileInfo.Name,
                        Path = logFile,
                        Size = fileInfo.Length,
                        LastModified = fileInfo.LastWriteTime
                    });

                    // Read and process the main IntuneManagementExtension.log
                    if (fileInfo.Name.Equals("IntuneManagementExtension.log", StringComparison.OrdinalIgnoreCase))
                    {
                        ProcessLogFile(logFile, result);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing remote log files: {ex.Message}");
            }
        }

        private void ProcessLogFile(string logFilePath, IntuneDiagnosticsResult result)
        {
            try
            {
                // Read last 500 lines of the log file
                var lines = File.ReadAllLines(logFilePath);
                var lastLines = lines.Skip(Math.Max(0, lines.Length - 500)).ToArray();

                foreach (var line in lastLines)
                {
                    ProcessOutputLine(line, result);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading log file: {ex.Message}");
            }
        }

        // Keep all other methods the same as before...
        private void ProcessOutputLine(string line, IntuneDiagnosticsResult result)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            try
            {
                // Skip warning lines
                if (line.Contains("WARNING:", StringComparison.OrdinalIgnoreCase))
                    return;

                // Try to parse JSON output if the script outputs JSON
                if (line.TrimStart().StartsWith("{") || line.TrimStart().StartsWith("["))
                {
                    try
                    {
                        var json = JsonDocument.Parse(line);
                        ProcessJsonOutput(json.RootElement, result);
                        return;
                    }
                    catch
                    {
                        // Not valid JSON, process as regular text
                    }
                }

                // Look for specific patterns in the output

                // Win32 App pattern - look for GUID patterns that indicate app IDs
                if (Regex.IsMatch(line, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.IgnoreCase))
                {
                    var appStatus = ParseWin32AppLine(line);
                    if (appStatus != null)
                    {
                        result.Win32Apps.Add(appStatus);
                    }
                }
                // Log file pattern
                else if (line.Contains(".log", StringComparison.OrdinalIgnoreCase) &&
                         (line.Contains("KB") || line.Contains("MB") || line.Contains("bytes")))
                {
                    var logFile = ParseLogFileLine(line);
                    if (logFile != null)
                    {
                        result.LogFiles.Add(logFile);
                    }
                }
                // Timeline entry pattern
                else if (Regex.IsMatch(line, @"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}"))
                {
                    var timelineEntry = ParseTimelineLine(line);
                    if (timelineEntry != null)
                    {
                        result.TimelineEntries.Add(timelineEntry);
                    }
                }
                // Error patterns
                else if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("0x8", StringComparison.OrdinalIgnoreCase)) // Common error codes
                {
                    result.RecentLogEntries.Add($"[ERROR] {line}");
                }
                // Success patterns
                else if (line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("successfully", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("COMPLETE", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("INSTALLED", StringComparison.OrdinalIgnoreCase))
                {
                    result.RecentLogEntries.Add($"[SUCCESS] {line}");
                }
                // Warning patterns
                else if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase))
                {
                    result.RecentLogEntries.Add($"[WARN] {line}");
                }
                // Info patterns
                else if (line.Contains("INFO", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("START", StringComparison.OrdinalIgnoreCase))
                {
                    result.RecentLogEntries.Add($"[INFO] {line}");
                }
                else if (!string.IsNullOrWhiteSpace(line) && line.Length > 10)
                {
                    // Add as general log entry if it's substantial
                    result.RecentLogEntries.Add(line);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing line: {ex.Message}");
            }
        }

        // Keep all other helper methods exactly the same...
        private void ProcessJsonOutput(JsonElement json, IntuneDiagnosticsResult result)
        {
            try
            {
                // Process based on JSON structure
                if (json.TryGetProperty("Win32Apps", out var apps))
                {
                    foreach (var app in apps.EnumerateArray())
                    {
                        var appStatus = new Win32AppStatus
                        {
                            AppId = GetJsonString(app, "AppId") ?? GetJsonString(app, "Id"),
                            AppName = GetJsonString(app, "AppName") ?? GetJsonString(app, "Name"),
                            Status = GetJsonString(app, "Status") ?? GetJsonString(app, "State"),
                            LastError = GetJsonString(app, "ErrorCode") ?? GetJsonString(app, "LastError"),
                            UserSID = GetJsonString(app, "UserSID") ?? GetJsonString(app, "User")
                        };

                        if (app.TryGetProperty("DownloadStartTime", out var downloadTime))
                        {
                            appStatus.DownloadStartTime = ParseDateTime(downloadTime.GetString());
                        }

                        result.Win32Apps.Add(appStatus);
                    }
                }

                if (json.TryGetProperty("LogFiles", out var logFiles))
                {
                    foreach (var file in logFiles.EnumerateArray())
                    {
                        var logFile = new LogFileInfo
                        {
                            Name = GetJsonString(file, "Name") ?? GetJsonString(file, "FileName"),
                            Path = GetJsonString(file, "Path") ?? GetJsonString(file, "FullName"),
                           
                            LastModified = ParseDateTime(GetJsonString(file, "LastModified") ??
                                          GetJsonString(file, "LastWriteTime")) ?? DateTime.MinValue
                        };
                        result.LogFiles.Add(logFile);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing JSON: {ex.Message}");
            }
        }

        private Win32AppStatus ParseWin32AppLine(string line)
        {
            try
            {
                var appStatus = new Win32AppStatus();

                // Look for GUID pattern for App ID
                var guidMatch = Regex.Match(line, @"([a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12})", RegexOptions.IgnoreCase);
                if (guidMatch.Success)
                {
                    appStatus.AppId = guidMatch.Groups[1].Value;
                }

                // Parse various formats
                var patterns = new Dictionary<string, string>
                {
                    { @"AppName[:\s]+([^,\r\n]+)", "AppName" },
                    { @"Name[:\s]+([^,\r\n]+)", "AppName" },
                    { @"Status[:\s]+([^,\r\n]+)", "Status" },
                    { @"State[:\s]+([^,\r\n]+)", "Status" },
                    { @"ErrorCode[:\s]+(0x[0-9A-Fa-f]+|\d+)", "LastError" },
                    { @"Error[:\s]+(0x[0-9A-Fa-f]+|\d+)", "LastError" },
                    { @"User[:\s]+([^,\r\n]+)", "UserSID" }
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(line, pattern.Key, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var value = match.Groups[1].Value.Trim();
                        switch (pattern.Value)
                        {
                            case "AppName":
                                if (string.IsNullOrEmpty(appStatus.AppName))
                                    appStatus.AppName = value;
                                break;
                            case "Status":
                                appStatus.Status = value;
                                break;
                            case "LastError":
                                appStatus.LastError = value;
                                break;
                            case "UserSID":
                                appStatus.UserSID = value;
                                break;
                        }
                    }
                }

                // Determine success/failure
                if (!string.IsNullOrEmpty(appStatus.Status))
                {
                    if (appStatus.Status.Contains("Success", StringComparison.OrdinalIgnoreCase) ||
                        appStatus.Status.Contains("Installed", StringComparison.OrdinalIgnoreCase))
                    {
                        appStatus.IsSuccess = true;
                    }
                    else if (appStatus.Status.Contains("Fail", StringComparison.OrdinalIgnoreCase) ||
                             appStatus.Status.Contains("Error", StringComparison.OrdinalIgnoreCase))
                    {
                        appStatus.IsSuccess = false;
                    }
                }

                return string.IsNullOrEmpty(appStatus.AppId) ? null : appStatus;
            }
            catch
            {
                return null;
            }
        }

        // Keep all remaining methods unchanged...
        private LogFileInfo ParseLogFileLine(string line)
        {
            try
            {
                var logFile = new LogFileInfo();

                // Extract filename
                var match = Regex.Match(line, @"([\w\-]+\.log)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    logFile.Name = match.Groups[1].Value;
                }

                // Extract size
                match = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*(KB|MB|bytes)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var sizeValue = double.Parse(match.Groups[1].Value);
                    var unit = match.Groups[2].Value.ToUpper();

                    logFile.Size = unit switch
                    {
                        "KB" => (long)(sizeValue * 1024),
                        "MB" => (long)(sizeValue * 1024 * 1024),
                        _ => (long)sizeValue
                    };
                }

                // Extract date
                match = Regex.Match(line, @"(\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}:\d{2}\s*[AP]M)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    if (DateTime.TryParse(match.Groups[1].Value, out var date))
                    {
                        logFile.LastModified = date;
                    }
                }

                return string.IsNullOrEmpty(logFile.Name) ? null : logFile;
            }
            catch
            {
                return null;
            }
        }

        private TimelineEntry ParseTimelineLine(string line)
        {
            try
            {
                var entry = new TimelineEntry();

                // Extract timestamp
                var match = Regex.Match(line, @"^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?)\s*(.+)");
                if (match.Success)
                {
                    if (DateTime.TryParse(match.Groups[1].Value, out var time))
                    {
                        entry.Time = time;
                    }

                    var remainder = match.Groups[2].Value;

                    // Try to extract event type
                    if (remainder.Contains(":"))
                    {
                        var parts = remainder.Split(new[] { ':' }, 2);
                        entry.Event = parts[0].Trim();
                        entry.Details = parts.Length > 1 ? parts[1].Trim() : "";
                    }
                    else
                    {
                        entry.Event = remainder;
                        entry.Details = "";
                    }

                    // Determine type
                    if (entry.Event.Contains("Error", StringComparison.OrdinalIgnoreCase))
                        entry.Type = "Error";
                    else if (entry.Event.Contains("Success", StringComparison.OrdinalIgnoreCase))
                        entry.Type = "Success";
                    else if (entry.Event.Contains("Start", StringComparison.OrdinalIgnoreCase))
                        entry.Type = "Start";
                    else
                        entry.Type = "Info";
                }

                return entry.Time != DateTime.MinValue ? entry : null;
            }
            catch
            {
                return null;
            }
        }

        private string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
            }
            return null;
        }

        private DateTime? ParseDateTime(string dateString)
        {
            if (string.IsNullOrEmpty(dateString)) return null;

            if (DateTime.TryParse(dateString, out var date))
            {
                return date;
            }

            return null;
        }

      
    }

    // Keep all model classes exactly the same...
    public class IntuneDiagnosticsResult
    {
        public string ComputerName { get; set; }
        public DateTime TimeStamp { get; set; }
        public bool IsSuccess { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<LogFileInfo> LogFiles { get; set; } = new List<LogFileInfo>();
        public List<Win32AppStatus> Win32Apps { get; set; } = new List<Win32AppStatus>();
        public List<TimelineEntry> TimelineEntries { get; set; } = new List<TimelineEntry>();
        public List<string> RecentLogEntries { get; set; } = new List<string>();
        public List<string> RawOutput { get; set; } = new List<string>();
    }

    public class LogFileInfo
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }

        public string SizeFormatted
        {
            get
            {
                if (Size > 1048576)
                    return $"{Size / 1048576.0:F2} MB";
                if (Size > 1024)
                    return $"{Size / 1024.0:F2} KB";
                return $"{Size} B";
            }
        }
    }

    public class Win32AppStatus
    {
        public string AppId { get; set; }
        public string AppName { get; set; }
        public string Status { get; set; }
        public string UserSID { get; set; }
        public DateTime? DownloadStartTime { get; set; }
        public DateTime? InstallStartTime { get; set; }
        public DateTime? CompletionTime { get; set; }
        public string LastError { get; set; }
        public bool? IsSuccess { get; set; }

        public string StatusDisplay
        {
            get
            {
                if (IsSuccess == true) return "✅ Success";
                if (IsSuccess == false) return "❌ Failed";
                if (Status?.Contains("Progress") == true) return "⏳ In Progress";
                return Status ?? "Unknown";
            }
        }
    }

    public class TimelineEntry
    {
        public DateTime Time { get; set; }
        public string Event { get; set; }
        public string Details { get; set; }
        public string Type { get; set; }

        public string TimeFormatted => Time.ToString("HH:mm:ss.fff");
    }
}