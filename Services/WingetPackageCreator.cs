// Services/WingetPackageCreator.cs
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
        private readonly string _tempPath = @"C:\Temp\WingetPackages";
        private readonly string _outputPath = @"\\nbb.local\sys\SCCMData\IntuneApplications";

        public event EventHandler<string> ProgressChanged;

        public WingetPackageCreator()
        {
            _psadtGenerator = new PSADTGenerator();
        }

        public async Task<PackageResult> CreatePackageFromWinget(string wingetId, PackageOptions options)
        {
            var result = new PackageResult();

            try
            {
                // 1. Get package info
                UpdateProgress("Getting package information from Winget...");
                var packageInfo = await GetWingetPackageInfo(wingetId);

                // 2. Download the installer
                UpdateProgress($"Downloading {packageInfo.Name}...");
                var installerPath = await DownloadWingetPackage(wingetId, packageInfo);

                // 3. Create ApplicationInfo from WingetPackageInfo
                var appInfo = new ApplicationInfo
                {
                    Name = packageInfo.Name,
                    Manufacturer = packageInfo.Publisher,
                    Version = packageInfo.Version,
                    SourcesPath = installerPath,
                    InstallContext = "System"
                };

                // 4. Create PSADTOptions from PackageOptions
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

                // 5. Use existing PSADTGenerator to create package
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
            ProgressChanged?.Invoke(this, message);
        }
    }
}