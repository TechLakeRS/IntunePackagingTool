using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace IntunePackagingTool.Services
{
    public class BatchFileSigner
    {
        private readonly string _certificateName;
        private readonly string _certificateThumbprint;
        private readonly string _timestampServer;
        private readonly string _signToolPath;

        public BatchFileSigner(string certificateName = "NBB Digital Workplace",
                              string certificateThumbprint = "B74452FD21BE6AD24CA9D61BCE156FD75E774716",
                              string timestampServer = "http://timestamp.digicert.com")
        {
            _certificateName = certificateName;
            _certificateThumbprint = certificateThumbprint?.Replace(" ", "").ToUpper() ?? "";
            _timestampServer = timestampServer;
            _signToolPath = FindSignToolPath();
        }

        // Supported file extensions for signing
        private readonly HashSet<string> _signableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".ocx", ".cab", ".cat", ".msi", ".msm", ".msp",
            ".ps1", ".psm1", ".psd1", ".ps1xml", ".vbs", ".js", ".wsf"
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
            var signableFiles = GetUnsignedFiles(rootDirectory, recursive);
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
                // Skip if already signed
                if (IsAlreadySigned(filePath))
                {
                    result.Success = true;
                    result.ErrorMessage = "Already signed";
                    return result;
                }

                if (string.IsNullOrEmpty(_signToolPath))
                {
                    result.Success = false;
                    result.ErrorMessage = "SignTool.exe not found";
                    return result;
                }

                var success = await SignWithSignToolAsync(filePath);
                result.Success = success;

                if (!success)
                {
                    result.ErrorMessage = "SignTool failed - check certificate and file permissions";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private List<string> GetUnsignedFiles(string rootDirectory, bool recursive)
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            return Directory.GetFiles(rootDirectory, "*.*", searchOption)
                .Where(file => _signableExtensions.Contains(Path.GetExtension(file)))
                .Where(file => !IsAlreadySigned(file))
                .OrderBy(file => file)
                .ToList();
        }

        private bool IsAlreadySigned(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();

                if (extension == ".ps1" || extension == ".psm1" || extension == ".psd1")
                {
                    // For PowerShell files, use PowerShell to check signature
                    return CheckPowerShellSignatureWithProcess(filePath);
                }
                else
                {
                    // For binary files, use .NET X509Certificate
                    return CheckAuthenticodeSignature(filePath);
                }
            }
            catch
            {
                return false; // Assume not signed if check fails
            }
        }

        private bool CheckPowerShellSignatureWithProcess(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"(Get-AuthenticodeSignature -FilePath '{filePath}').Status -eq 'Valid'\"",
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

        private bool CheckAuthenticodeSignature(string filePath)
        {
            try
            {
                var cert = X509Certificate.CreateFromSignedFile(filePath);
                return cert != null;
            }
            catch
            {
                return false;
            }
        }

        private string GetHashAlgorithm(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

   
            if (extension == ".ps1" || extension == ".psm1" || extension == ".psd1" || extension == ".ps1xml")
            {
                return "sha256";
            }
           
            return "sha256";
        }

        private async Task<bool> SignWithSignToolAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var hashAlgorithm = GetHashAlgorithm(filePath);
                    var fileName = Path.GetFileName(filePath);

                   
                    var signingAttempts = new List<string>();

                    // Method 1: Use thumbprint with LocalMachine\My store
                    if (!string.IsNullOrEmpty(_certificateThumbprint))
                    {
                        signingAttempts.Add($"sign /s My /sm /sha1 {_certificateThumbprint} /t {_timestampServer} /fd {hashAlgorithm} /v \"{filePath}\"");
                    }

                    // Method 2: Use thumbprint with CurrentUser\My store  
                    if (!string.IsNullOrEmpty(_certificateThumbprint))
                    {
                        signingAttempts.Add($"sign /s My /sha1 {_certificateThumbprint} /t {_timestampServer} /fd {hashAlgorithm} /v \"{filePath}\"");
                    }

                    // Method 3: Use subject name with LocalMachine\My store
                    signingAttempts.Add($"sign /s My /sm /n \"{_certificateName}\" /t {_timestampServer} /fd {hashAlgorithm} /v \"{filePath}\"");

                    // Method 4: Use subject name with CurrentUser\My store
                    signingAttempts.Add($"sign /s My /n \"{_certificateName}\" /t {_timestampServer} /fd {hashAlgorithm} /v \"{filePath}\"");

                    foreach (var arguments in signingAttempts)
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = _signToolPath,
                            Arguments = arguments,
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            process.WaitForExit();

                            if (process.ExitCode == 0)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }
                catch (Exception)
                {
                    return false;
                }
            });
        }

        private string FindSignToolPath()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
               
            };

            return possiblePaths.FirstOrDefault(File.Exists) ?? "";
        }

        public bool ValidateCertificateAvailability()
        {
            try
            {
                var storesToCheck = new[]
                {
                    new { Store = StoreName.My, Location = StoreLocation.LocalMachine },
                    new { Store = StoreName.My, Location = StoreLocation.CurrentUser }
                };

                foreach (var storeInfo in storesToCheck)
                {
                    using var store = new X509Store(storeInfo.Store, storeInfo.Location);
                    store.Open(OpenFlags.ReadOnly);

                   
                    if (!string.IsNullOrEmpty(_certificateThumbprint))
                    {
                        var certsByThumbprint = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
                        if (certsByThumbprint.Count > 0 && certsByThumbprint[0].HasPrivateKey)
                        {
                            return true;
                        }
                    }

                    
                    var certsBySubject = store.Certificates.Find(X509FindType.FindBySubjectName, _certificateName, false);
                    if (certsBySubject.Count > 0 && certsBySubject[0].HasPrivateKey)
                    {
                        return true;
                    }
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
                issues.Add($"🔍 CERTIFICATE DIAGNOSIS");
                issues.Add($"Searching for: Subject='{_certificateName}', Thumbprint='{_certificateThumbprint}'");
                issues.Add("");

                var storesToCheck = new[]
                {
                    new { Store = StoreName.My, Location = StoreLocation.LocalMachine, Name = "LocalMachine\\My (Personal)" },
                    new { Store = StoreName.My, Location = StoreLocation.CurrentUser, Name = "CurrentUser\\My (Personal)" },
                    new { Store = StoreName.TrustedPublisher, Location = StoreLocation.LocalMachine, Name = "LocalMachine\\TrustedPublisher" },
                    new { Store = StoreName.TrustedPublisher, Location = StoreLocation.CurrentUser, Name = "CurrentUser\\TrustedPublisher" },
                    new { Store = StoreName.Root, Location = StoreLocation.LocalMachine, Name = "LocalMachine\\Root" }
                };

                bool foundInTrustedPublisher = false;
                bool foundInPersonalWithPrivateKey = false;

                foreach (var storeInfo in storesToCheck)
                {
                    using var store = new X509Store(storeInfo.Store, storeInfo.Location);
                    store.Open(OpenFlags.ReadOnly);

                    issues.Add($"📁 {storeInfo.Name}: {store.Certificates.Count} certificates");

                    // Check by thumbprint
                    if (!string.IsNullOrEmpty(_certificateThumbprint))
                    {
                        var certsByThumbprint = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
                        foreach (X509Certificate2 cert in certsByThumbprint)
                        {
                            var hasPrivateKey = cert.HasPrivateKey ? "✅ Has Private Key" : "❌ No Private Key";
                            issues.Add($"  🎯 FOUND BY THUMBPRINT: {cert.Subject}");
                            issues.Add($"     {hasPrivateKey}");
                            issues.Add($"     Valid: {cert.NotBefore:yyyy-MM-dd} to {cert.NotAfter:yyyy-MM-dd}");

                            if (storeInfo.Store == StoreName.TrustedPublisher)
                            {
                                foundInTrustedPublisher = true;
                                issues.Add($"     ⚠️  Found in TrustedPublisher - need to copy to Personal store");
                            }

                            if (cert.HasPrivateKey && storeInfo.Store == StoreName.My)
                            {
                                foundInPersonalWithPrivateKey = true;
                                issues.Add($"     ⭐ PERFECT FOR SIGNING!");
                            }
                            else if (!cert.HasPrivateKey)
                            {
                                issues.Add($"     ⚠️  Can't sign - no private key");
                            }
                        }
                    }

                    // Check by subject name
                    var certsBySubject = store.Certificates.Find(X509FindType.FindBySubjectName, _certificateName, false);
                    foreach (X509Certificate2 cert in certsBySubject)
                    {
                        var hasPrivateKey = cert.HasPrivateKey ? "✅ Has Private Key" : "❌ No Private Key";
                        issues.Add($"  📋 FOUND BY SUBJECT: {cert.Subject}");
                        issues.Add($"     Thumbprint: {cert.Thumbprint}");
                        issues.Add($"     {hasPrivateKey}");

                        if (storeInfo.Store == StoreName.TrustedPublisher)
                        {
                            foundInTrustedPublisher = true;
                        }

                        if (cert.HasPrivateKey && storeInfo.Store == StoreName.My)
                        {
                            foundInPersonalWithPrivateKey = true;
                            issues.Add($"     ⭐ PERFECT FOR SIGNING!");
                        }
                    }

                    issues.Add("");
                }

                issues.Add("🔧 DIAGNOSIS & SOLUTION:");

                if (foundInTrustedPublisher && !foundInPersonalWithPrivateKey)
                {
                    issues.Add("❌ PROBLEM: Certificate found in TrustedPublisher but NOT in Personal store with private key");
                    issues.Add("");
                    issues.Add("✅ SOLUTION - You need to import the certificate with private key:");
                    issues.Add("1. Find your original .pfx/.p12 certificate file");
                    issues.Add("2. Run 'certmgr.msc' as Administrator");
                    issues.Add("3. Navigate to Personal > Certificates");
                    issues.Add("4. Right-click > All Tasks > Import...");
                    issues.Add("5. Select your .pfx file");
                    issues.Add("6. Enter the password");
                    issues.Add("7. Check 'Mark this key as exportable'");
                    issues.Add("8. Choose 'Personal' store (not Trusted Publishers)");
                    issues.Add("");
                    issues.Add("OR use PowerShell:");
                    issues.Add($"Import-PfxCertificate -FilePath 'C:\\path\\to\\your.pfx' -CertStoreLocation Cert:\\LocalMachine\\My");
                }
                else if (!foundInTrustedPublisher && !foundInPersonalWithPrivateKey)
                {
                    issues.Add("❌ PROBLEM: Certificate not found in any store");
                    issues.Add("✅ SOLUTION: Import your certificate with private key to Personal store");
                }
                else if (foundInPersonalWithPrivateKey)
                {
                    issues.Add("✅ Certificate is correctly installed for signing!");
                }

                issues.Add("");
                issues.Add("📝 NOTES:");
                issues.Add("• TrustedPublisher = Public keys only (for verification)");
                issues.Add("• Personal (My) = Private keys (for signing)");
                issues.Add("• SignTool requires certificates in Personal store");
                issues.Add("• All files use SHA256 hash algorithm (modern standard)");
                issues.Add("• PowerShell scripts require SHA256 (as requested)");

            }
            catch (Exception ex)
            {
                issues.Add($"❌ Error during diagnosis: {ex.Message}");
            }

            return string.Join("\n", issues);
        }
    }
}