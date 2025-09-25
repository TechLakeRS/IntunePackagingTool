// Services/WingetCatalogService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;  // <-- THIS IS FOR Path CLASS
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class WingetCatalogService
    {
        private readonly string _packageRepository = @"\\nbb.local\sys\SCCMData\IntuneApplications";
        private readonly string _wingetCachePath = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\WingetCache";
        private List<WingetPackage> _catalogCache;
        private readonly WingetRestApiService _apiService;  // <-- THIS FIELD WAS MISSING

        public event EventHandler<string> ProgressChanged;

        public WingetCatalogService()
        {
            _apiService = new WingetRestApiService();  // <-- INITIALIZE IT HERE
            _apiService.ProgressChanged += (s, e) => OnProgressChanged(e);
        }

        public async Task<List<WingetPackage>> GetFullCatalogAsync()
        {
            System.Diagnostics.Debug.WriteLine("=== GetFullCatalogAsync called ===");

            // Check if running on Windows Server
            if (IsWindowsServer())
            {
                System.Diagnostics.Debug.WriteLine("Windows Server detected, REST API will work but local installs won't");
            }

            // Use REST API instead of command line
            return await _apiService.SearchPackagesAsync("", 50); // Get 50 popular packages
        }

        public async Task<List<WingetPackage>> SearchPackagesAsync(string searchTerm)
        {
            System.Diagnostics.Debug.WriteLine($"=== SearchPackagesAsync called with term: '{searchTerm}' ===");

            // Use REST API for searching
            return await _apiService.SearchPackagesAsync(searchTerm, 50);
        }

        private bool IsWindowsServer()
        {
            var productName = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                "ProductName",
                "").ToString();

            return productName.Contains("Server");
        }

        private List<WingetPackage> ParseListOutput(string output)
        {
            var packages = new List<WingetPackage>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            bool startParsing = false;
            foreach (var line in lines)
            {
                // Look for the header line to start parsing
                if (line.Contains("Name") && line.Contains("Id") && line.Contains("Version"))
                {
                    startParsing = true;
                    continue;
                }

                if (line.StartsWith("-"))
                    continue;

                if (startParsing && !string.IsNullOrWhiteSpace(line))
                {
                    var package = ParsePackageLine(line);
                    if (package != null)
                    {
                        packages.Add(package);
                    }
                }
            }

            return packages;
        }

        private List<WingetPackage> ParseSearchOutput(string output)
        {
            var packages = new List<WingetPackage>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            System.Diagnostics.Debug.WriteLine($"=== ParseSearchOutput: Processing {lines.Length} lines ===");

            bool startParsing = false;
            int headerLineIndex = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                // Look for the header line to start parsing
                if ((line.Contains("Name") && line.Contains("Id") && line.Contains("Version")) ||
                    (line.Contains("Name") && line.Contains("ID") && line.Contains("Version"))) // Sometimes it's "ID" not "Id"
                {
                    startParsing = true;
                    headerLineIndex = i;
                    System.Diagnostics.Debug.WriteLine($"Found header at line {i}: {line}");
                    continue;
                }

                // Skip the separator line (usually dashes)
                if (startParsing && i == headerLineIndex + 1 && line.StartsWith("-"))
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping separator line {i}: {line}");
                    continue;
                }

                if (startParsing && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("-"))
                {
                    System.Diagnostics.Debug.WriteLine($"Parsing line {i}: {line}");
                    var package = ParsePackageLine(line);
                    if (package != null)
                    {
                        packages.Add(package);
                        System.Diagnostics.Debug.WriteLine($"  Added package: {package.Name} ({package.Id})");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"=== Total packages parsed: {packages.Count} ===");
            return packages;
        }

        private WingetPackage ParsePackageLine(string line)
        {
            try
            {
                // Winget output is typically formatted with fixed-width columns
                // The format is usually: Name | Id | Version | Available/Match | Source

                // Split by multiple spaces (winget uses spacing for columns)
                var parts = Regex.Split(line, @"\s{2,}");

                if (parts.Length >= 3)
                {
                    return new WingetPackage
                    {
                        Name = parts[0].Trim(),
                        Id = parts[1].Trim(),
                        Version = parts[2].Trim(),
                        Source = parts.Length > 4 ? parts[4].Trim() : "winget",
                        IsInstalled = false,
                        LastUpdated = DateTime.Now
                    };
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Error parsing line: {ex.Message}");
            }

            return null;
        }

        public async Task<WingetPackage> GetPackageDetailsAsync(string packageId)
        {
            try
            {
                var details = await _apiService.GetPackageDetailsAsync(packageId);
                if (details != null)
                {
                    return new WingetPackage
                    {
                        Id = details.PackageId,
                        Name = details.PackageName,
                        Publisher = details.Publisher,
                        Version = details.LatestVersion,
                        Description = details.Description,
                        Homepage = details.Homepage,
                        License = details.License,
                        InstallerType = details.InstallerType,
                        Source = "Winget"
                    };
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Error getting package details: {ex.Message}");
            }

            return null;
        }

        private WingetPackage ParseShowOutput(string output, string packageId)
        {
            var lines = output.Split('\n');
            var package = new WingetPackage { Id = packageId };

            foreach (var line in lines)
            {
                if (line.StartsWith("Found"))
                {
                    var match = Regex.Match(line, @"Found (.+) \[(.+)\]");
                    if (match.Success)
                    {
                        package.Name = match.Groups[1].Value.Trim();
                    }
                }
                else if (line.Contains(":"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();

                        switch (key)
                        {
                            case "Version":
                                package.Version = value;
                                break;
                            case "Publisher":
                                package.Publisher = value;
                                break;
                            case "Description":
                                package.Description = value;
                                break;
                            case "Homepage":
                                package.Homepage = value;
                                break;
                            case "License":
                                package.License = value;
                                break;
                            case "Installer Type":
                                package.InstallerType = value;
                                break;
                        }
                    }
                }
            }

            return package;
        }

        public async Task<List<WingetPackage>> GetInstalledPackagesAsync()
        {
            var packages = new List<WingetPackage>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = "list --accept-source-agreements",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                packages = ParseListOutput(output);

                // Mark all as installed
                foreach (var package in packages)
                {
                    package.IsInstalled = true;
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Error getting installed packages: {ex.Message}");
            }

            return packages;
        }

        public async Task<bool> InstallPackageAsync(string packageId)
        {
            try
            {
                OnProgressChanged($"Installing {packageId}...");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = $"install --id \"{packageId}\" --silent --accept-package-agreements --accept-source-agreements",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    OnProgressChanged($"Successfully installed {packageId}");
                    return true;
                }
                else
                {
                    OnProgressChanged($"Failed to install {packageId}: {error}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Error installing package: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UninstallPackageAsync(string packageId)
        {
            try
            {
                OnProgressChanged($"Uninstalling {packageId}...");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = $"uninstall --id \"{packageId}\" --silent",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    OnProgressChanged($"Successfully uninstalled {packageId}");
                    return true;
                }
                else
                {
                    OnProgressChanged($"Failed to uninstall {packageId}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Error uninstalling package: {ex.Message}");
                return false;
            }
        }

        public async Task<string> DownloadPackageInstallerAsync(string packageId, string targetPath)
        {
            try
            {
                var details = await _apiService.GetPackageDetailsAsync(packageId);
                if (details != null && !string.IsNullOrEmpty(details.InstallerUrl))
                {
                    var fileName = Path.GetFileName(new Uri(details.InstallerUrl).LocalPath);  // NOW Path WILL WORK
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = $"{packageId.Replace(".", "_")}.exe";
                    }

                    var outputPath = Path.Combine(targetPath, fileName);  // NOW Path WILL WORK

                    if (await _apiService.DownloadPackageAsync(packageId, targetPath))
                    {
                        return outputPath;
                    }
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Download failed: {ex.Message}");
            }

            return null;
        }

        private void OnProgressChanged(string message)
        {
            ProgressChanged?.Invoke(this, message);
        }

        // Helper method to extract value from a line
        private string ExtractValue(string line, string key)
        {
            if (line.Contains(key))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    return parts[1].Trim();
                }
            }
            return string.Empty;
        }
    }
}