using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class WDACService
    {
        private readonly string _scriptPath;
        private readonly string _catalogOutputPath;

        public WDACService()
        {
            _scriptPath = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\Create-CatfileHyperV.ps1";
            _catalogOutputPath = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\CatTS";
        }

       
        public async Task<CatalogResult> GenerateCatalogAsync(CatalogTask task, IProgress<string>? progress = null)
        {
            return await Task.Run(() => GenerateCatalogInternal(task, progress));
        }

        private CatalogResult GenerateCatalogInternal(CatalogTask task, IProgress<string>? progress)
        {
            var result = new CatalogResult();

            try
            {
                progress?.Report($"Starting catalog generation for {task.AppName}...");

                // Verify the script exists
                if (!File.Exists(_scriptPath))
                {
                    throw new FileNotFoundException($"PowerShell script not found: {_scriptPath}");
                }

                // Verify the package path exists
                if (!Directory.Exists(task.PackagePath))
                {
                    throw new DirectoryNotFoundException($"Package directory not found: {task.PackagePath}");
                }

                var outputPath = GetCatalogOutputPath(task);
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                progress?.Report("Executing PowerShell script...");

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = BuildPowerShellArguments(task.PackagePath, outputPath),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetDirectoryName(task.PackagePath)!
                    }
                };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        progress?.Report(e.Data);
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for process with timeout
                bool completed = process.WaitForExit(600000); // 10 minutes timeout

                if (!completed)
                {
                    process.Kill();
                    throw new TimeoutException("Catalog generation timed out after 10 minutes");
                }

                result.LogOutput = outputBuilder.ToString();

                if (process.ExitCode == 0)
                {
                    result.Success = true;
                    result.CatalogPath = FindGeneratedCatalog(outputPath, result.LogOutput);
                    result.Hash = ExtractHashFromOutput(result.LogOutput);

                    if (string.IsNullOrEmpty(result.CatalogPath))
                    {
                        result.Success = false;
                        result.ErrorMessage = "Catalog generation reported success but no catalog file was found";
                    }
                    else
                    {
                        progress?.Report($"✓ Catalog generated successfully: {result.CatalogPath}");
                    }
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"PowerShell script failed with exit code {process.ExitCode}\n{errorBuilder}";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                progress?.Report($"✗ Error: {ex.Message}");
            }

            return result;
        }

        
        public async Task<List<CatalogResult>> GenerateBatchCatalogsAsync(
            IEnumerable<CatalogTask> tasks,
            IProgress<BatchProgress>? progress = null)
        {
            var results = new List<CatalogResult>();
            var taskList = tasks.ToList();
            var batchProgress = new BatchProgress
            {
                TotalTasks = taskList.Count,
                CurrentTask = 0
            };

            foreach (var task in taskList)
            {
                batchProgress.CurrentTask++;
                batchProgress.CurrentTaskName = task.AppName;
                progress?.Report(batchProgress);

                var taskProgress = new Progress<string>(message =>
                {
                    batchProgress.CurrentMessage = message;
                    progress?.Report(batchProgress);
                });

                var result = await GenerateCatalogAsync(task, taskProgress);
                results.Add(result);
            }

            return results;
        }

       
        public async Task<bool> ValidateCatalogAsync(string catalogPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "powershell.exe",
                            Arguments = $"-Command \"Test-CiCatalog -CatalogFilePath '{catalogPath}'\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

       
        public static ApplicationInfo ParseApplicationInfo(string folderPath)
        {
            var folderName = Path.GetFileName(folderPath);
            var parts = folderName.Split('_');

            return new ApplicationInfo
            {
                Manufacturer = parts.Length > 0 ? parts[0] : "Unknown",
                Name = parts.Length > 1 ? parts[1] : folderName,
                Version = parts.Length > 2 ? parts[2] : "1.0.0"
            };
        }

        private string BuildPowerShellArguments(string packagePath, string outputPath)
        {
            return $"-ExecutionPolicy Bypass -File \"{_scriptPath}\" " +
                   $"-ApplicationPath \"{packagePath}\" " +
                    $"-Verbose";
        }

        private string GetCatalogOutputPath(CatalogTask task)
        {
            var cleanAppName = task.AppName.Replace(" ", "_");
            var cleanVersion = task.Version.Replace(" ", "_");
            var catalogName = $"{cleanAppName}_{cleanVersion}_{DateTime.Now:yyyyMMdd_HHmmss}.cat";
            return Path.Combine(_catalogOutputPath, cleanAppName, catalogName);
        }

        private string FindGeneratedCatalog(string expectedPath, string output)
        {
            // First check if the expected path exists
            if (File.Exists(expectedPath))
            {
                return expectedPath;
            }

            // Try to extract path from output
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(".cat") && line.Contains("Successfully created"))
                {
                    var startIndex = line.IndexOf('"') + 1;
                    var endIndex = line.LastIndexOf('"');
                    if (startIndex > 0 && endIndex > startIndex)
                    {
                        var path = line.Substring(startIndex, endIndex - startIndex);
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
            }

            // Check the package directory for any .cat files
            var packageDir = Path.GetDirectoryName(expectedPath);
            if (Directory.Exists(packageDir))
            {
                var catFiles = Directory.GetFiles(packageDir, "*.cat", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => new FileInfo(f).CreationTime)
                    .FirstOrDefault();

                if (catFiles != null)
                {
                    return catFiles;
                }
            }

            return "";
        }

        private string ExtractHashFromOutput(string output)
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("Hash:") || line.Contains("SHA256:"))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        return parts[1].Trim();
                    }
                }
            }
            return "";
        }

        public class BatchProgress
        {
            public int TotalTasks { get; set; }
            public int CurrentTask { get; set; }
            public string CurrentTaskName { get; set; } = "";
            public string CurrentMessage { get; set; } = "";
            public int PercentComplete => TotalTasks > 0 ? (CurrentTask * 100) / TotalTasks : 0;
        }
    }
}