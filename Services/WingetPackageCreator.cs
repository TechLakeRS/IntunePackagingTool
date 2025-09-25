// Services/WingetPackageCreator.cs - FIXED VERSION
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntunePackagingTool.Models;
using IntunePackagingTool.Services;

namespace IntunePackagingTool.Services
{
    public class WingetPackageCreator
    {
        private readonly PSADTGenerator _psadtGenerator;
        private readonly HttpClient _httpClient;
        private readonly string _tempPath = @"C:\Temp\WingetPackages";
        private readonly string _outputPath = @"\\nbb.local\sys\SCCMData\IntuneApplications";

        public event EventHandler<string> ProgressChanged;

        public WingetPackageCreator()
        {
            _psadtGenerator = new PSADTGenerator();
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "winget-rest-client");
        }

        public async Task<PackageResult> CreatePackageFromWinget(string wingetId, PackageOptions options, string preferredArchitecture = "x64")
        {
            var result = new PackageResult();

            try
            {
                UpdateProgress($"Creating package for {wingetId} (Architecture: {preferredArchitecture})...");

                // Try REST API first
                try
                {
                    UpdateProgress("Attempting to use REST API for package creation...");
                    result = await CreatePackageFromRestApi(wingetId, options, preferredArchitecture);
                    if (result.Success)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    UpdateProgress($"REST API failed: {ex.Message}. Falling back to winget CLI...");
                }

                // Fallback to winget CLI
                result = await CreatePackageFromWingetCli(wingetId, options);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                UpdateProgress($"❌ Failed: {ex.Message}");
            }

            return result;
        }

        private async Task<PackageResult> CreatePackageFromRestApi(string wingetId, PackageOptions options, string preferredArchitecture = "x64")
        {
            var result = new PackageResult();

            try
            {
                // 1. Get package manifest from REST API
                UpdateProgress("Getting package information from REST API...");
                var packageInfo = await GetPackageInfoFromRestApi(wingetId);

                if (packageInfo == null)
                {
                    throw new Exception("Package not found in REST API");
                }

                // 2. Get installer information
                UpdateProgress("Getting installer information...");
                var installerInfo = await GetInstallerInfoFromManifest(wingetId, preferredArchitecture);

                if (installerInfo == null || string.IsNullOrEmpty(installerInfo.Url))
                {
                    throw new Exception("No installer URL found in manifest");
                }

                // 3. Download the installer
                UpdateProgress($"Downloading {packageInfo.Name}...");
                var installerPath = await DownloadInstaller(installerInfo.Url, wingetId, installerInfo.FileName);

                // 4. Create ApplicationInfo
                var appInfo = new ApplicationInfo
                {
                    Name = packageInfo.Name,
                    Manufacturer = packageInfo.Publisher,
                    Version = packageInfo.Version,
                    SourcesPath = installerPath,
                    InstallContext = "System"
                };

                // 5. Create PSADTOptions
                var psadtOptions = new PSADTOptions
                {
                    PackageType = DetectPackageType(installerPath),
                    SilentInstall = true,
                    AllUsersInstall = true,
                    VerboseLogging = true,
                    CreateInstallMarker = true,
                    RemovePreviousVersions = options.RemoveOldVersions,
                    CloseRunningApps = options.CloseApps,
                    CheckDiskSpace = true
                };

                // 7. Use PSADTGenerator to create package
                UpdateProgress("Creating PSADT package...");
                var packagePath = await _psadtGenerator.CreatePackageAsync(appInfo, psadtOptions);

                // 8. Modify script with REST API fallback
                await ModifyScriptForRestApi(packagePath, wingetId, installerInfo);

                result.Success = true;
                result.PackagePath = packagePath;
                result.PackageInfo = packageInfo;

                UpdateProgress($"✅ Package created successfully at: {packagePath}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"REST API method failed: {ex.Message}";
                throw;
            }

            return result;
        }

        private async Task<WingetPackageInfo> GetPackageInfoFromRestApi(string packageId)
        {
            try
            {
                // Search for the package first to get basic info
                var searchUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0/manifestSearch";
                var searchRequest = new
                {
                    Query = new { KeyWord = packageId, MatchType = "Exact" },
                    MaximumResults = 1,
                    Filters = new[] { new { PackageMatchField = "PackageIdentifier", RequestMatch = new { KeyWord = packageId, MatchType = "Exact" } } },
                    IncludeUnknown = false
                };

                var searchJson = JsonSerializer.Serialize(searchRequest);
                var searchContent = new StringContent(searchJson, Encoding.UTF8, "application/json");

                var searchResponse = await _httpClient.PostAsync(searchUrl, searchContent);
                searchResponse.EnsureSuccessStatusCode();

                var searchResult = await searchResponse.Content.ReadAsStringAsync();

                // FIX: Use JsonSerializer.Deserialize instead of Serialize
                using var doc = JsonDocument.Parse(searchResult);
                var root = doc.RootElement;

                if (!root.TryGetProperty("Data", out var data) || data.GetArrayLength() == 0)
                {
                    return null;
                }

                var package = data[0];

                return new WingetPackageInfo
                {
                    WingetId = packageId,
                    Name = package.TryGetProperty("PackageName", out var name) ? name.GetString() : packageId,
                    Publisher = package.TryGetProperty("Publisher", out var pub) ? pub.GetString() : "Unknown",
                    Version = GetVersionFromPackage(package),
                    Description = GetDescriptionFromPackage(package)
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"REST API search failed: {ex.Message}");
                return null;
            }
        }

        private string GetVersionFromPackage(JsonElement package)
        {
            if (package.TryGetProperty("Versions", out var versions) &&
                versions.GetArrayLength() > 0 &&
                versions[0].TryGetProperty("PackageVersion", out var version))
            {
                return version.GetString() ?? "1.0.0";
            }
            return "1.0.0";
        }

        private string GetDescriptionFromPackage(JsonElement package)
        {
            if (package.TryGetProperty("Versions", out var versions) &&
                versions.GetArrayLength() > 0)
            {
                var firstVersion = versions[0];
                if (firstVersion.TryGetProperty("DefaultLocale", out var locale))
                {
                    if (locale.TryGetProperty("ShortDescription", out var desc))
                    {
                        return desc.GetString() ?? "";
                    }
                }
                // Try to get description directly from version
                if (firstVersion.TryGetProperty("ShortDescription", out var directDesc))
                {
                    return directDesc.GetString() ?? "";
                }
            }
            return "";
        }

        private async Task<InstallerInfo> GetInstallerInfoFromManifest(string packageId, string preferredArchitecture = "x64")
        {
            try
            {
                // Get the full manifest with installer details
                var manifestUrl = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/packageManifests/{packageId}";

                var response = await _httpClient.GetAsync(manifestUrl);
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Failed to get manifest: {response.StatusCode}");
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("Data", out var data))
                {
                    return null;
                }

                if (!data.TryGetProperty("Versions", out var versions) || versions.GetArrayLength() == 0)
                {
                    return null;
                }

                var version = versions[0];
                if (!version.TryGetProperty("Installers", out var installers) || installers.GetArrayLength() == 0)
                {
                    return null;
                }

                // Find the best installer - prioritize based on preferred architecture
                JsonElement? bestInstaller = null;
                int bestPriority = -1;

                foreach (var installer in installers.EnumerateArray())
                {
                    var arch = installer.TryGetProperty("Architecture", out var archProp)
                        ? archProp.GetString()?.ToLower()
                        : "neutral";

                    // Skip if no installer URL
                    if (!installer.TryGetProperty("InstallerUrl", out var _))
                    {
                        continue;
                    }

                    // Determine priority based on architecture and preference
                    int priority = 0;

                    if (preferredArchitecture.ToLower() == "x64")
                    {
                        priority = arch switch
                        {
                            "x64" => 100,        // Exact match - highest priority
                            "x86_64" => 100,     // Alternative notation for x64
                            "amd64" => 100,      // Another alternative for x64
                            "x86" => 50,         // Fallback to 32-bit if no 64-bit
                            "neutral" => 40,     // Neutral/universal installers
                            "all" => 40,         // All architectures
                            _ when arch.Contains("arm") => 10,  // Lowest priority for ARM
                            _ => 30              // Unknown architectures
                        };
                    }
                    else if (preferredArchitecture.ToLower() == "x86")
                    {
                        priority = arch switch
                        {
                            "x86" => 100,        // Exact match - highest priority
                            "x64" => 50,         // Can run 32-bit on 64-bit
                            "x86_64" => 50,
                            "amd64" => 50,
                            "neutral" => 40,
                            "all" => 40,
                            _ when arch.Contains("arm") => 10,
                            _ => 30
                        };
                    }
                    else // Any other preference or "neutral"
                    {
                        priority = arch switch
                        {
                            var a when a == preferredArchitecture.ToLower() => 100,
                            "neutral" => 50,
                            "all" => 50,
                            _ => 30
                        };
                    }

                    Debug.WriteLine($"Found installer with architecture: {arch}, priority: {priority}");

                    if (priority > bestPriority)
                    {
                        bestInstaller = installer;
                        bestPriority = priority;
                    }
                }

                if (!bestInstaller.HasValue)
                {
                    Debug.WriteLine("No suitable installer found");
                    return null;
                }

                var selectedInstaller = bestInstaller.Value;

                if (!selectedInstaller.TryGetProperty("InstallerUrl", out var urlElement))
                {
                    return null;
                }

                var installerUrl = urlElement.GetString();
                if (string.IsNullOrEmpty(installerUrl))
                {
                    return null;
                }

                var selectedArch = selectedInstaller.TryGetProperty("Architecture", out var selectedArchProp)
                    ? selectedArchProp.GetString()
                    : "unknown";

                UpdateProgress($"Selected {selectedArch} installer (preferred: {preferredArchitecture})");
                Debug.WriteLine($"Selected installer: Architecture={selectedArch}, URL={installerUrl}");

                return new InstallerInfo
                {
                    Url = installerUrl,
                    FileName = GetFileNameFromUrl(installerUrl),
                    InstallerType = selectedInstaller.TryGetProperty("InstallerType", out var type) ? type.GetString() : "exe",
                    Architecture = selectedArch,
                    SilentArgs = GetSilentArgsFromInstaller(selectedInstaller),
                    ProductCode = selectedInstaller.TryGetProperty("ProductCode", out var prodCode) ? prodCode.GetString() : null,
                    Sha256 = selectedInstaller.TryGetProperty("InstallerSha256", out var sha) ? sha.GetString() : null
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get installer info: {ex.Message}");
                return null;
            }
        }

        private string GetSilentArgsFromInstaller(JsonElement installer)
        {
            // Check for explicit silent switches in manifest
            if (installer.TryGetProperty("InstallerSwitches", out var switches))
            {
                if (switches.TryGetProperty("Silent", out var silent))
                {
                    var silentStr = silent.GetString();
                    if (!string.IsNullOrEmpty(silentStr))
                    {
                        return silentStr;
                    }
                }

                if (switches.TryGetProperty("SilentWithProgress", out var silentWithProgress))
                {
                    var silentProgressStr = silentWithProgress.GetString();
                    if (!string.IsNullOrEmpty(silentProgressStr))
                    {
                        return silentProgressStr;
                    }
                }
            }

            // Default silent arguments based on installer type
            var installerType = "exe";
            if (installer.TryGetProperty("InstallerType", out var typeElement))
            {
                installerType = typeElement.GetString()?.ToLower() ?? "exe";
            }

            return installerType switch
            {
                "msi" or "wix" or "burn" => "/qn /norestart",
                "exe" => "/S",
                "inno" => "/VERYSILENT /NORESTART",
                "nullsoft" => "/S",
                "msix" or "appx" => "",
                _ => "/quiet"
            };
        }

        private async Task<string> DownloadInstaller(string url, string packageId, string fileName)
        {
            var downloadPath = Path.Combine(_tempPath, packageId.Replace(".", "_"));
            Directory.CreateDirectory(downloadPath);

            var filePath = Path.Combine(downloadPath, fileName);

            // Check if already downloaded
            if (File.Exists(filePath))
            {
                UpdateProgress($"Using cached installer: {fileName}");
                return filePath;
            }

            UpdateProgress($"Downloading from: {url}");

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (int)((downloadedBytes * 100) / totalBytes);
                    UpdateProgress($"Downloading: {progress}% ({downloadedBytes}/{totalBytes} bytes)");
                }
            }

            UpdateProgress($"Download complete: {fileName}");
            return filePath;
        }

        private async Task ModifyScriptForRestApi(string packagePath, string wingetId, InstallerInfo installerInfo)
        {
            var scriptPath = Path.Combine(packagePath, "Application", "Deploy-Application.ps1");

            if (File.Exists(scriptPath))
            {
                var scriptContent = await File.ReadAllTextAsync(scriptPath);

                // Find the installation section
                var installIndex = scriptContent.IndexOf("## <Perform Installation tasks here>");
                if (installIndex > 0)
                {
                    // Add enhanced installation with winget fallback
                    var enhancedInstall = $@"
        ## Enhanced installation with multiple fallback options
        Write-Log -Message 'Starting installation for {wingetId}' -Source $appDeployToolkitName
        
        $installSuccess = $false
        
        # Method 1: Try Winget if available
        if (Get-Command winget -ErrorAction SilentlyContinue) {{
            Write-Log -Message 'Attempting installation via Winget' -Source $appDeployToolkitName
            Try {{
                $wingetResult = Execute-Process -Path 'winget' -Parameters 'install --id ""{wingetId}"" --silent --accept-package-agreements --accept-source-agreements' -WindowStyle 'Hidden' -PassThru
                if ($wingetResult.ExitCode -eq 0) {{
                    Write-Log -Message 'Winget installation completed successfully' -Source $appDeployToolkitName
                    $installSuccess = $true
                }}
            }}
            Catch {{
                Write-Log -Message ""Winget installation failed: $_"" -Severity 2 -Source $appDeployToolkitName
            }}
        }}
        
        # Method 2: Use local installer if winget failed
        if (-not $installSuccess) {{
            Write-Log -Message 'Using local installer' -Source $appDeployToolkitName
            # Local installer code will be added by PSADT generator
        }}
";
                    scriptContent = scriptContent.Insert(installIndex + 40, enhancedInstall);
                }

                await File.WriteAllTextAsync(scriptPath, scriptContent);
            }
        }

        private string GetFileNameFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "installer.exe";
            }

            try
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);

                // Simple fallback without HttpUtility
                if (string.IsNullOrEmpty(fileName) || fileName == "/")
                {
                    // Try to extract from the URL path
                    var segments = uri.Segments;
                    if (segments.Length > 0)
                    {
                        fileName = segments[segments.Length - 1];
                        if (string.IsNullOrEmpty(fileName) || fileName == "/")
                        {
                            fileName = "installer.exe";
                        }
                    }
                    else
                    {
                        fileName = "installer.exe";
                    }
                }

                return fileName;
            }
            catch
            {
                return "installer.exe";
            }
        }

        // Keep the existing CLI-based method as fallback
        private async Task<PackageResult> CreatePackageFromWingetCli(string wingetId, PackageOptions options)
        {
            var result = new PackageResult();

            try
            {
                // 1. Get package info using winget CLI
                UpdateProgress("Getting package information from Winget CLI...");
                var packageInfo = await GetWingetPackageInfo(wingetId);

                // 2. Download the installer using winget
                UpdateProgress($"Downloading {packageInfo.Name} via Winget...");
                var installerPath = await DownloadWingetPackage(wingetId, packageInfo);

                // 3. Create ApplicationInfo
                var appInfo = new ApplicationInfo
                {
                    Name = packageInfo.Name,
                    Manufacturer = packageInfo.Publisher,
                    Version = packageInfo.Version,
                    SourcesPath = installerPath,
                    InstallContext = "System"
                };

                // 4. Create PSADTOptions
                var psadtOptions = new PSADTOptions
                {
                    PackageType = DetectPackageType(installerPath),
                    SilentInstall = true,
                    AllUsersInstall = true,
                    VerboseLogging = true,
                    CreateInstallMarker = true,
                    RemovePreviousVersions = options.RemoveOldVersions,
                    CloseRunningApps = options.CloseApps,
                    CheckDiskSpace = true
                };

                // 5. Use PSADTGenerator to create package
                UpdateProgress("Creating PSADT package...");
                var packagePath = await _psadtGenerator.CreatePackageAsync(appInfo, psadtOptions);

                // 6. Modify the script to add Winget fallback
                await ModifyScriptForWinget(packagePath, wingetId);

                result.Success = true;
                result.PackagePath = packagePath;
                result.PackageInfo = packageInfo;

                UpdateProgress($"✅ Package created successfully at: {packagePath}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                UpdateProgress($"❌ Failed: {ex.Message}");
            }

            return result;
        }

        // Keep existing winget CLI methods
        private async Task<WingetPackageInfo> GetWingetPackageInfo(string wingetId)
        {
            var info = new WingetPackageInfo { WingetId = wingetId };

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "winget",
                        Arguments = $"show \"{wingetId}\" --accept-source-agreements",
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

                // Parse the output
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Found"))
                    {
                        var match = Regex.Match(line, @"Found (.+) \[(.+)\]");
                        if (match.Success)
                        {
                            info.Name = match.Groups[1].Value.Trim();
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
                                    info.Version = value;
                                    break;
                                case "Publisher":
                                    info.Publisher = value;
                                    break;
                                case "Description":
                                    info.Description = value;
                                    break;
                                case "Homepage":
                                    info.Homepage = value;
                                    break;
                                case "License":
                                    info.License = value;
                                    break;
                                case "Installer Type":
                                    info.InstallerType = value;
                                    break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get package info: {ex.Message}");
            }

            return info;
        }

        private async Task<string> DownloadWingetPackage(string wingetId, WingetPackageInfo info)
        {
            var downloadPath = Path.Combine(_tempPath, wingetId.Replace(".", "_"));
            Directory.CreateDirectory(downloadPath);

            // Use winget download command
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"download --id \"{wingetId}\" --download-directory \"{downloadPath}\" --accept-package-agreements --accept-source-agreements",
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

            // Find the downloaded file
            var files = Directory.GetFiles(downloadPath, "*.*", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                // Return the first installer file found
                var installerFile = files.FirstOrDefault(f =>
                    f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) ?? files[0];

                return installerFile;
            }

            throw new Exception($"Failed to download package {wingetId}");
        }

        private string DetectPackageType(string installerPath)
        {
            var extension = Path.GetExtension(installerPath)?.ToLower();
            return extension switch
            {
                ".msi" => "MSI",
                ".exe" => "EXE",
                _ => "EXE"
            };
        }

        private async Task ModifyScriptForWinget(string packagePath, string wingetId)
        {
            var scriptPath = Path.Combine(packagePath, "Application", "Deploy-Application.ps1");

            if (File.Exists(scriptPath))
            {
                var scriptContent = await File.ReadAllTextAsync(scriptPath);

                // Find the installation section
                var installIndex = scriptContent.IndexOf("## <Perform Installation tasks here>");
                if (installIndex > 0)
                {
                    // Add Winget fallback installation
                    var wingetInstall = $@"
        ## Try Winget installation as primary method
        Write-Log -Message 'Attempting installation via Winget' -Source $appDeployToolkitName
        Try {{
            $wingetResult = Execute-Process -Path 'winget' -Parameters 'install --id ""{wingetId}"" --silent --accept-package-agreements --accept-source-agreements' -WindowStyle 'Hidden' -PassThru
            If ($wingetResult.ExitCode -eq 0) {{
                Write-Log -Message 'Winget installation completed successfully' -Source $appDeployToolkitName
            }}
            Else {{
                Write-Log -Message ""Winget installation failed, trying local installer"" -Severity 2 -Source $appDeployToolkitName
                # Fallback to local installer will go here
            }}
        }}
        Catch {{
            Write-Log -Message ""Winget not available, using local installer: $_"" -Severity 2 -Source $appDeployToolkitName
        }}
";
                    // Insert after the comment
                    scriptContent = scriptContent.Insert(installIndex + 40, wingetInstall);
                }

                await File.WriteAllTextAsync(scriptPath, scriptContent);
            }
        }

        private void UpdateProgress(string message)
        {
            Debug.WriteLine($"WingetPackageCreator: {message}");
            ProgressChanged?.Invoke(this, message);
        }
    }

    public class InstallerInfo
    {
        public string Url { get; set; }
        public string FileName { get; set; }
        public string InstallerType { get; set; }
        public string Architecture { get; set; }
        public string SilentArgs { get; set; }
        public string ProductCode { get; set; }
        public string Sha256 { get; set; }
    }
}