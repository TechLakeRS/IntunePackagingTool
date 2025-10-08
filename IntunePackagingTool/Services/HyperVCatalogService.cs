using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntunePackagingTool.Configuration;

namespace IntunePackagingTool.Services
{
    public class HyperVCatalogService
    {
        private readonly string _scriptPath = Paths.HyperVScript;
        private StringBuilder _logBuilder;

        private static string _cachedUsername;
        private static string _cachedPassword;
        private static object _credentialLock = new object();

        public event EventHandler<string> StatusChanged;
        public event EventHandler<string> DetailChanged;

        private readonly HashSet<string> _loggedMessages = new HashSet<string>();
        private string _lastLogMessage = "";
        private int _duplicateCount = 0;
        private DateTime _lastLogTime = DateTime.MinValue;

        public HyperVCatalogService()
        {
            _logBuilder = new StringBuilder();
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Skip .NET exceptions entirely - they're useless noise
            if (message.Contains("Exception thrown: 'System.ObjectDisposedException'") ||
                message.Contains("The thread '.NET TP Worker'"))
            {
                return;
            }

            // Skip file listing spam (files being copied)
            if (message.StartsWith(@"\Application\") ||
                message.StartsWith(@"\Icon\") ||
                message.StartsWith(@"\Intune\") ||
                message.StartsWith(@"\Documentation\") ||
                message.StartsWith(@"\Project Files\") ||
                message.StartsWith(@"\NBB_Info\"))
            {
                return;
            }

            // CRITICAL: Always log process exit codes for investigation
            if (message.Contains("Process exit code"))
            {
                var logEntry = $"[{timestamp}] ⚠️ CRITICAL: {message}";
                _logBuilder.AppendLine(logEntry);
                Debug.WriteLine(logEntry);
                _lastLogMessage = message;
                _lastLogTime = DateTime.Now;
                return;
            }

            // Define truly important keywords
            var importantKeywords = new[]
            {
                "Starting restore",
                "Executed successfully",
                "Copy applicationfiles from",
                "Waiting 10 seconds",
                "packageinspector.exe stop",
                "Generate the hash",
                "Copy the security catalog",
                "Copy succes",
                "ERROR",
                "FAILED",
                "EXCEPTION",
                "WARNING",
                "Using provided credentials",
                "Credentials cached",
                "Connecting to",
                "PsExec exit code"
            };

            bool isImportant = importantKeywords.Any(keyword =>
                message.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (!isImportant)
            {
                return;
            }

            // Prevent rapid-fire duplicates (same message within 3 seconds)
            if (message == _lastLogMessage &&
                (DateTime.Now - _lastLogTime).TotalSeconds < 3)
            {
                _duplicateCount++;
                return;
            }

            // Log suppressed duplicates
            if (_duplicateCount > 0)
            {
                var suppressedEntry = $"[{timestamp}] ... ({_duplicateCount} duplicate message(s) suppressed)";
                _logBuilder.AppendLine(suppressedEntry);
                Debug.WriteLine(suppressedEntry);
                _duplicateCount = 0;
            }

            // Log the message
            var logEntry2 = $"[{timestamp}] {message}";
            _logBuilder.AppendLine(logEntry2);
            Debug.WriteLine(logEntry2);

            _lastLogMessage = message;
            _lastLogTime = DateTime.Now;
        }

        public string GetLog() => _logBuilder.ToString();

        public async Task<HyperVCatalogResult> GenerateCatalogAsync(
            string hyperVHost,
            string vmName,
            string snapshotName,
            string applicationPath)
        {
            var result = new HyperVCatalogResult();
            _logBuilder.Clear();

            // Reset duplicate tracking for new operation
            _lastLogMessage = "";
            _duplicateCount = 0;
            _lastLogTime = DateTime.MinValue;

            try
            {
                UpdateStatus("Validating parameters...", "Checking prerequisites");
                LogMessage($"Starting catalog generation for: {applicationPath}");
                LogMessage($"Hyper-V Host: {hyperVHost}");
                LogMessage($"VM Name: {vmName}");
                LogMessage($"Snapshot: {snapshotName}");

                if (!ValidateInputs(hyperVHost, vmName, snapshotName, applicationPath, result))
                {
                    return result;
                }

                if (!File.Exists(_scriptPath))
                {
                    result.Success = false;
                    result.ErrorMessage = $"Script not found at: {_scriptPath}";
                    LogMessage($"ERROR: {result.ErrorMessage}");
                    return result;
                }

                if (!File.Exists(Paths.WDACHyperV.PsExecPath))
                {
                    result.Success = false;
                    result.ErrorMessage = $"PsExec not found at: {Paths.WDACHyperV.PsExecPath}\n\nPlease download PsExec from https://docs.microsoft.com/sysinternals/downloads/psexec";
                    LogMessage($"ERROR: {result.ErrorMessage}");
                    return result;
                }

                UpdateStatus("Connecting to Hyper-V host...", $"Using PsExec to connect to {hyperVHost}");

                result = await ExecuteWithPsExecAsync(hyperVHost, vmName, snapshotName, applicationPath);

                if (result.Success)
                {
                    UpdateStatus("Catalog generation complete!", "Successfully created catalog file");
                    LogMessage($"SUCCESS: Catalog file created at {result.CatalogFilePath}");
                    LogMessage($"Hash: {result.CatalogHash}");
                }
                else
                {
                    UpdateStatus("Catalog generation failed", result.ErrorMessage);
                    LogMessage($"ERROR: {result.ErrorMessage}");
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Unexpected error: {ex.Message}";
                LogMessage($"EXCEPTION: {ex}");
                UpdateStatus("Error", ex.Message);
                return result;
            }
        }

        private async Task<HyperVCatalogResult> ExecuteWithPsExecAsync(string hyperVHost,string vmName,string snapshotName,string applicationPath)
        {
            var result = new HyperVCatalogResult();

            return await Task.Run(() =>
            {
                try
                {
                    UpdateStatus("Checking credentials...", "Authentication required");

                    var psScript = $@"
# Check if credentials are already provided as arguments
param($cachedUsername, $cachedPassword)

if (![string]::IsNullOrEmpty($cachedUsername) -and ![string]::IsNullOrEmpty($cachedPassword)) {{
    Write-Host 'Using cached credentials'
    $username = $cachedUsername
    $password = $cachedPassword
}} else {{
    Write-Host 'Prompting for credentials...'
    $cred = Get-Credential -Message 'Enter credentials for {hyperVHost}' -UserName 'NBBLOCAL\wkssarkr'
    
    if ($null -eq $cred) {{
        Write-Error 'Credentials required'
        exit 1
    }}
    
    $username = $cred.UserName
    $password = $cred.GetNetworkCredential().Password
    Write-Host ""Using provided credentials: $username""
    
    # Output credentials so C# can cache them
    Write-Host ""CACHE_CREDENTIALS:$username|$password""
}}

# Clear old log files
Remove-Item 'C:\temp\psexec_output.log' -Force -ErrorAction SilentlyContinue
Remove-Item 'C:\temp\psexec_error.log' -Force -ErrorAction SilentlyContinue

Write-Host 'Connecting to {hyperVHost} using PsExec...'

# Build the PowerShell command
$remoteCommand = 'pushd \\nbb.local\sys && powershell.exe -ExecutionPolicy Bypass -File {_scriptPath} -HyperVHost {hyperVHost} -VMName {vmName} -VMSnapshot {snapshotName} -ApplicationPath ""{applicationPath.Replace("\"", "\"\"")}"" -Virtual $true'

$psexecArgs = @(
    '\\{hyperVHost}',
    '-u', $username,
    '-p', $password,
    '-accepteula',
    '-h',
    'cmd.exe',
    '/c', $remoteCommand
)

Write-Host 'Executing via PsExec...'

$process = Start-Process -FilePath '{Paths.WDACHyperV.PsExecPath}' -ArgumentList $psexecArgs -NoNewWindow -Wait -PassThru -RedirectStandardOutput 'C:\temp\psexec_output.log' -RedirectStandardError 'C:\temp\psexec_error.log'

$exitCode = $process.ExitCode
Write-Host ""PsExec exit code: $exitCode""

exit $exitCode
";

                    var tempScriptPath = Path.Combine(Path.GetTempPath(), $"PsExec_Catalog_{Guid.NewGuid()}.ps1");
                    File.WriteAllText(tempScriptPath, psScript);

                    try
                    {
                        UpdateStatus("Executing via PsExec...", "Running catalog generation");

                        var arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{tempScriptPath}\"";

                        lock (_credentialLock)
                        {
                            if (!string.IsNullOrEmpty(_cachedUsername) && !string.IsNullOrEmpty(_cachedPassword))
                            {
                                arguments += $" -cachedUsername \"{_cachedUsername}\" -cachedPassword \"{_cachedPassword}\"";
                                LogMessage("Using cached credentials");
                            }
                        }

                        using var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = arguments,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };

                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                if (e.Data.StartsWith("CACHE_CREDENTIALS:"))
                                {
                                    var parts = e.Data.Substring("CACHE_CREDENTIALS:".Length).Split('|');
                                    if (parts.Length == 2)
                                    {
                                        lock (_credentialLock)
                                        {
                                            _cachedUsername = parts[0];
                                            _cachedPassword = parts[1];
                                        }
                                        LogMessage("Credentials cached for future operations");
                                    }
                                }
                                else
                                {
                                    LogMessage(e.Data);
                                    ParseOutputForProgress(e.Data);
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                LogMessage($"STDERR: {e.Data}");
                            }
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        var logPath = @"C:\temp\psexec_output.log";
                        long lastPosition = 0;
                        int maxWaitIterations = 600;
                        int iterations = 0;

                        while (!process.HasExited && iterations < maxWaitIterations)
                        {
                            if (File.Exists(logPath))
                            {
                                try
                                {
                                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    {
                                        if (fs.Length > lastPosition)
                                        {
                                            fs.Seek(lastPosition, SeekOrigin.Begin);
                                            using (var sr = new StreamReader(fs))
                                            {
                                                string line;
                                                while ((line = sr.ReadLine()) != null)
                                                {
                                                    LogMessage(line);
                                                    ParseOutputForProgress(line);
                                                }
                                            }
                                            lastPosition = fs.Length;
                                        }
                                    }
                                }
                                catch { }
                            }

                            System.Threading.Thread.Sleep(1000);
                            iterations++;
                        }

                        if (iterations >= maxWaitIterations)
                        {
                            LogMessage("WARNING: Maximum wait time exceeded (10 minutes)");
                            try
                            {
                                if (!process.HasExited)
                                    process.Kill();
                            }
                            catch { }

                            result.Success = false;
                            result.ErrorMessage = "Operation timed out after 10 minutes";
                            return result;
                        }

                   
                        process.WaitForExit();

                        // CRITICAL: Log exit code for diagnosis
                        LogMessage($"Process exit code: {process.ExitCode}");

                        if (process.ExitCode == 0)
                        {
                            result.Success = true;

                            var catalogInfo = ExtractCatalogInfoFromLog();
                            result.CatalogFilePath = catalogInfo.FilePath;
                            result.CatalogHash = catalogInfo.Hash;

                            if (string.IsNullOrEmpty(result.CatalogFilePath))
                            {
                                var catalogPath = FindCatalogFile(applicationPath);
                                if (!string.IsNullOrEmpty(catalogPath))
                                {
                                    result.CatalogFilePath = catalogPath;
                                    result.CatalogHash = ExtractHashFromFilename(catalogPath);
                                }
                                else
                                {
                                    result.Success = false;
                                    result.ErrorMessage = "Catalog file was not found after script execution.";
                                }
                            }
                        }
                        else
                        {
                            result.Success = false;
                            result.ErrorMessage = $"PsExec failed with exit code {process.ExitCode}";
                            LogMessage($"ERROR: PsExec exit code {process.ExitCode} indicates failure");
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (File.Exists(tempScriptPath))
                                File.Delete(tempScriptPath);

                            if (File.Exists(@"C:\temp\psexec_output.log"))
                                File.Delete(@"C:\temp\psexec_output.log");

                            if (File.Exists(@"C:\temp\psexec_error.log"))
                                File.Delete(@"C:\temp\psexec_error.log");
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = $"Execution failed: {ex.Message}";
                    LogMessage($"EXCEPTION: {ex}");
                }

                return result;
            });
        }

      

        private string FindCatalogFile(string applicationPath)
        {
            try
            {
                var projectFilesFolder = Path.Combine(applicationPath, "Project Files");

                LogMessage($"Searching for catalog in: {projectFilesFolder}");

                if (!Directory.Exists(projectFilesFolder))
                {
                    LogMessage($"Project Files folder does not exist: {projectFilesFolder}");
                    return null;
                }

                var catFiles = Directory.GetFiles(projectFilesFolder, "*.cat")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToArray();

                LogMessage($"Found {catFiles.Length} .cat files");

                return catFiles.Length > 0 ? catFiles[0] : null;
            }
            catch (Exception ex)
            {
                LogMessage($"Error finding catalog file: {ex.Message}");
                return null;
            }
        }

        private string ExtractHashFromFilename(string catalogPath)
        {
            var filename = Path.GetFileName(catalogPath);
            var match = Regex.Match(filename, @"C_([A-F0-9]+)-", RegexOptions.IgnoreCase);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            LogMessage("Catalog filename doesn't contain hash, computing from file...");
            return ComputeFileHash(catalogPath);
        }

        private string ComputeFileHash(string filePath)
        {
            try
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hashBytes).Replace("-", "");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to compute hash: {ex.Message}");
                return "";
            }
        }

        private bool ValidateInputs(string hyperVHost, string vmName, string snapshotName, string applicationPath, HyperVCatalogResult result)
        {
            if (string.IsNullOrWhiteSpace(hyperVHost))
            {
                result.Success = false;
                result.ErrorMessage = "Hyper-V host is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(vmName))
            {
                result.Success = false;
                result.ErrorMessage = "VM name is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(snapshotName))
            {
                result.Success = false;
                result.ErrorMessage = "Snapshot name is required";
                return false;
            }

            if (string.IsNullOrWhiteSpace(applicationPath))
            {
                result.Success = false;
                result.ErrorMessage = "Application path is required";
                return false;
            }

            if (!Directory.Exists(applicationPath))
            {
                result.Success = false;
                result.ErrorMessage = $"Application path does not exist: {applicationPath}";
                return false;
            }

            return true;
        }

        private void ParseOutputForProgress(string output)
        {
            if (string.IsNullOrEmpty(output)) return;

            if (output.Contains("Starting restore") || output.Contains("Start script"))
            {
                UpdateStatus("Starting", "Initializing catalog generation");
            }
            else if (output.Contains("restore") && output.Contains("snapshot"))
            {
                UpdateStatus("Restoring VM", "Reverting to snapshot");
            }
            else if (output.Contains("packageinspector.exe start"))
            {
                UpdateStatus("Package Inspector", "Started monitoring");
            }
            else if (output.Contains("Copy applicationfiles from"))
            {
                UpdateStatus("Copying Files", "Transferring to VM");
            }
            else if (output.Contains("Deploy-Application.ps1") && output.Contains("Running"))
            {
                UpdateStatus("Installing", "Running PSADT installer");
            }
            else if (output.Contains("Waiting") && output.Contains("seconds"))
            {
                UpdateStatus("Finalizing", "Waiting for installation to complete");
            }
            else if (output.Contains("packageinspector.exe stop"))
            {
                UpdateStatus("Generating Catalog", "Creating .cat file");
            }
            else if (output.Contains("Generate the hash"))
            {
                UpdateStatus("Hashing", "Computing catalog hash");
            }
            else if (output.Contains("Copy succes"))
            {
                UpdateStatus("Success", "Catalog created");
            }
        }

        private (string FilePath, string Hash) ExtractCatalogInfoFromLog()
        {
            string filePath = null;
            string hash = null;

            var logContent = _logBuilder.ToString();
            var lines = logContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.Contains("Project Files") && line.Contains(".cat"))
                {
                    var matchWithPrefix = Regex.Match(line, @"([A-Z]:\\.*\\Project Files\\C_[A-F0-9]+-.*\.cat)", RegexOptions.IgnoreCase);
                    var matchWithoutPrefix = Regex.Match(line, @"([A-Z]:\\.*\\Project Files\\[^\\]+\.cat)", RegexOptions.IgnoreCase);

                    if (matchWithPrefix.Success)
                    {
                        filePath = matchWithPrefix.Groups[1].Value.Trim();
                    }
                    else if (matchWithoutPrefix.Success)
                    {
                        filePath = matchWithoutPrefix.Groups[1].Value.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(filePath) && string.IsNullOrEmpty(hash))
                {
                    hash = ExtractHashFromFilename(filePath);
                }
            }

            return (filePath, hash);
        }

        private void UpdateStatus(string status, string detail)
        {
            StatusChanged?.Invoke(this, status);
            DetailChanged?.Invoke(this, detail);
        }

        public void ClearCachedCredentials()
        {
            lock (_credentialLock)
            {
                _cachedUsername = null;
                _cachedPassword = null;
                LogMessage("Cached credentials cleared");
            }
        }

       
    }

    public class HyperVCatalogResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string CatalogFilePath { get; set; }
        public string CatalogHash { get; set; }
    }
}