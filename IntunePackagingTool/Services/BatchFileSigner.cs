using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IntunePackagingTool.Services
{
    public class BatchFileSigner
    {
        private readonly string _certificateName;
        private readonly string _certificateSubject;
        private readonly string _certificateThumbprint;
        private readonly string _timestampServer;

        public BatchFileSigner(string? certificateName = null,
                              string? certificateSubject = null,
                              string? certificateThumbprint = null,
                              string? timestampServer = null)
        {
            // Load from settings if not provided
            if (certificateName == null || certificateThumbprint == null)
            {
                var settingsService = new SettingsService();
                var settings = settingsService.Settings.CodeSigning;

                _certificateName = certificateName ?? settings.CertificateName;
                _certificateSubject = certificateSubject ?? settings.CertificateSubject;
                _certificateThumbprint = (certificateThumbprint ?? settings.CertificateThumbprint)?.Replace(" ", "").ToUpper() ?? "";
                _timestampServer = timestampServer ?? settings.TimestampServer;
            }
            else
            {
                _certificateName = certificateName;
                _certificateSubject = certificateSubject ?? "CN=NBB Digital Workplace, OU=National Bank of Belgium (BE), O=EUROPEAN SYSTEM OF CENTRAL BANKS, C=BE";
                _certificateThumbprint = certificateThumbprint?.Replace(" ", "").ToUpper() ?? "";
                _timestampServer = timestampServer ?? "http://timestamp.digicert.com";
            }
        }

        // Supported file extensions for signing
        private readonly HashSet<string> _signableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".dll", ".exe", ".ocx", ".cab", ".cat",
            ".ps1", ".psm1", ".psd1", ".ps1xml"
        };

        public class SigningResult
        {
            public string FilePath { get; set; } = "";
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        public class SigningProgress
        {
            public int TotalFiles { get; set; }
            public int ProcessedFiles { get; set; }
            public string CurrentFile { get; set; } = "";
            public List<SigningResult> Results { get; set; } = new List<SigningResult>();
        }

        public async Task<SigningProgress> SignApplicationFolderAsync(string packagePath,
                                                                     IProgress<SigningProgress>? progress = null)
        {
            var applicationPath = Path.Combine(packagePath, "Application");

            if (!Directory.Exists(applicationPath))
            {
                throw new DirectoryNotFoundException($"Application folder not found: {applicationPath}");
            }

            return await SignAllFilesAsync(applicationPath, progress, recursive: true);
        }

        public async Task<SigningProgress> SignAllFilesAsync(string rootDirectory,
                                                            IProgress<SigningProgress>? progress = null,
                                                            bool recursive = true)
        {
            var signableFiles = GetSignableFiles(rootDirectory, recursive);
            var signingProgress = new SigningProgress { TotalFiles = signableFiles.Count };

            foreach (var file in signableFiles)
            {
                signingProgress.CurrentFile = file;
                progress?.Report(signingProgress);

                var result = await SignSingleFileAsync(file);
                signingProgress.Results.Add(result);
                signingProgress.ProcessedFiles++;

                progress?.Report(signingProgress);
            }

            return signingProgress;
        }

        public async Task<SigningResult> SignSingleFileAsync(string filePath)
        {
            var result = new SigningResult { FilePath = filePath };

            try
            {
                var success = await SignWithPowerShellAsync(filePath);
                result.Success = success;

                if (!success)
                {
                    result.ErrorMessage = "PowerShell signing failed - check certificate and file permissions";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private List<string> GetSignableFiles(string rootDirectory, bool recursive)
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            return Directory.GetFiles(rootDirectory, "*.*", searchOption)
                .Where(file => _signableExtensions.Contains(Path.GetExtension(file)))
                .OrderBy(file => file)
                .ToList();
        }

        private async Task<bool> SignWithPowerShellAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Escape single quotes in file path for PowerShell
                    var escapedFilePath = filePath.Replace("'", "''");

                    // Build PowerShell command using Set-AuthenticodeSignature
                    var powerShellCommand = BuildPowerShellSigningCommand(escapedFilePath);

                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{powerShellCommand}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit();

                        if (process.ExitCode != 0)
                        {
                            var error = process.StandardError.ReadToEnd();
                            Debug.WriteLine($"Signing failed for {filePath}: {error}");
                            return false;
                        }

                        return true;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception during signing {filePath}: {ex.Message}");
                    return false;
                }
            });
        }

        private string BuildPowerShellSigningCommand(string escapedFilePath)
        {
            // Try multiple methods to find the certificate
            var commands = new List<string>();

            // Method 1: Try LocalMachine\TrustedPublisher with full subject match
            commands.Add($@"
                $cert = Get-ChildItem Cert:\LocalMachine\TrustedPublisher -CodeSigningCert | 
                    Where-Object {{$_.Subject -eq '{_certificateSubject}'}} | 
                    Select-Object -First 1");

            // Method 2: Try LocalMachine\My (Personal) with thumbprint
            if (!string.IsNullOrEmpty(_certificateThumbprint))
            {
                commands.Add($@"
                if (-not $cert) {{
                    $cert = Get-ChildItem Cert:\LocalMachine\My | 
                        Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}'}} | 
                        Select-Object -First 1
                }}");
            }

            // Method 3: Try CurrentUser\My with thumbprint
            if (!string.IsNullOrEmpty(_certificateThumbprint))
            {
                commands.Add($@"
                if (-not $cert) {{
                    $cert = Get-ChildItem Cert:\CurrentUser\My | 
                        Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}'}} | 
                        Select-Object -First 1
                }}");
            }

            // Method 4: Try LocalMachine\My with subject name contains
            commands.Add($@"
                if (-not $cert) {{
                    $cert = Get-ChildItem Cert:\LocalMachine\My -CodeSigningCert | 
                        Where-Object {{$_.Subject -like '*{_certificateName}*'}} | 
                        Select-Object -First 1
                }}");

            // Method 5: Try CurrentUser\My with subject name contains
            commands.Add($@"
                if (-not $cert) {{
                    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | 
                        Where-Object {{$_.Subject -like '*{_certificateName}*'}} | 
                        Select-Object -First 1
                }}");

            // Build the final command
            var fullCommand = string.Join(Environment.NewLine, commands) + $@"
                if ($cert) {{
                    Set-AuthenticodeSignature -Certificate $cert -TimestampServer '{_timestampServer}' -HashAlgorithm SHA256 -FilePath '{escapedFilePath}' | Out-Null
                    exit 0
                }} else {{
                    Write-Error 'Certificate not found'
                    exit 1
                }}";

            return fullCommand;
        }

        public bool ValidateCertificateAvailability()
        {
            try
            {
                // Check using PowerShell to see if we can find the certificate
                var checkCommand = $@"
                    $found = $false
                    
                    # Check TrustedPublisher
                    $cert = Get-ChildItem Cert:\LocalMachine\TrustedPublisher -CodeSigningCert | 
                        Where-Object {{$_.Subject -eq '{_certificateSubject}'}}
                    if ($cert) {{ $found = $true }}
                    
                    # Check LocalMachine\My
                    if (-not $found) {{
                        $cert = Get-ChildItem Cert:\LocalMachine\My | 
                            Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}' -or $_.Subject -like '*{_certificateName}*'}}
                        if ($cert) {{ $found = $true }}
                    }}
                    
                    # Check CurrentUser\My
                    if (-not $found) {{
                        $cert = Get-ChildItem Cert:\CurrentUser\My | 
                            Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}' -or $_.Subject -like '*{_certificateName}*'}}
                        if ($cert) {{ $found = $true }}
                    }}
                    
                    Write-Output $found";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{checkCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit();
                    var output = process.StandardOutput.ReadToEnd().Trim();
                    return output.Equals("True", StringComparison.OrdinalIgnoreCase);
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public string DiagnoseCertificateIssues()
        {
            var issues = new List<string>();

            try
            {
                issues.Add($"🔍 CERTIFICATE DIAGNOSIS (PowerShell Method)");
                issues.Add($"Searching for: Subject='{_certificateName}', Thumbprint='{_certificateThumbprint}'");
                issues.Add("");

                // Use PowerShell to get detailed certificate information
                var diagnosticCommand = $@"
                    Write-Output '📁 Certificate Store Analysis:'
                    Write-Output ''
                    
                    # Check LocalMachine\TrustedPublisher
                    Write-Output '🔐 LocalMachine\TrustedPublisher:'
                    $certs = Get-ChildItem Cert:\LocalMachine\TrustedPublisher -CodeSigningCert | 
                        Where-Object {{$_.Subject -eq '{_certificateSubject}'}}
                    if ($certs) {{
                        foreach ($cert in $certs) {{
                            Write-Output ""  ✅ FOUND: $($cert.Subject)""
                            Write-Output ""     Thumbprint: $($cert.Thumbprint)""
                            Write-Output ""     Has Private Key: $($cert.HasPrivateKey)""
                            Write-Output ""     Valid: $($cert.NotBefore) to $($cert.NotAfter)""
                        }}
                    }} else {{
                        Write-Output '  ❌ Certificate not found in TrustedPublisher'
                    }}
                    Write-Output ''
                    
                    # Check LocalMachine\My
                    Write-Output '🔐 LocalMachine\My (Personal):'
                    $certs = Get-ChildItem Cert:\LocalMachine\My | 
                        Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}' -or $_.Subject -like '*{_certificateName}*'}}
                    if ($certs) {{
                        foreach ($cert in $certs) {{
                            Write-Output ""  ✅ FOUND: $($cert.Subject)""
                            Write-Output ""     Thumbprint: $($cert.Thumbprint)""
                            Write-Output ""     Has Private Key: $($cert.HasPrivateKey)""
                            if ($cert.HasPrivateKey) {{
                                Write-Output ""     ⭐ PERFECT FOR SIGNING!""
                            }}
                        }}
                    }} else {{
                        Write-Output '  ❌ Certificate not found in Personal store'
                    }}
                    Write-Output ''
                    
                    # Check CurrentUser\My
                    Write-Output '🔐 CurrentUser\My (Personal):'
                    $certs = Get-ChildItem Cert:\CurrentUser\My | 
                        Where-Object {{$_.Thumbprint -eq '{_certificateThumbprint}' -or $_.Subject -like '*{_certificateName}*'}}
                    if ($certs) {{
                        foreach ($cert in $certs) {{
                            Write-Output ""  ✅ FOUND: $($cert.Subject)""
                            Write-Output ""     Thumbprint: $($cert.Thumbprint)""
                            Write-Output ""     Has Private Key: $($cert.HasPrivateKey)""
                        }}
                    }} else {{
                        Write-Output '  ❌ Certificate not found in CurrentUser Personal store'
                    }}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{diagnosticCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit();
                    var output = process.StandardOutput.ReadToEnd();
                    issues.Add(output);
                }

                issues.Add("");
                issues.Add("🔧 NOTES:");
                issues.Add("• PowerShell Set-AuthenticodeSignature can use certificates from:");
                issues.Add("  - LocalMachine\\TrustedPublisher (as specified in your command)");
                issues.Add("  - LocalMachine\\My (Personal)");
                issues.Add("  - CurrentUser\\My (Personal)");
                issues.Add("• SHA256 hash algorithm is used for all signatures");
                issues.Add("• Timestamp server: " + _timestampServer);

            }
            catch (Exception ex)
            {
                issues.Add($"❌ Error during diagnosis: {ex.Message}");
            }

            return string.Join("\n", issues);
        }
    }
}