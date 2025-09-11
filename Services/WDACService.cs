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
        
        private readonly string _vmScriptPath;
        private readonly string _catalogOutputPath;
        private readonly bool _useSimpleScript;

        public WDACService()
        {
           
            _vmScriptPath = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\Create_CatVM.ps1";
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

                // Choose which script to use
                string scriptPath;
                string arguments;


                {
                    progress?.Report("Using VM-based catalog generation...");
                    scriptPath = _vmScriptPath;
                    arguments = BuildVMScriptArguments(task);
                }

               

                // Verify the package path exists
                if (!Directory.Exists(task.PackagePath))
                {
                    throw new DirectoryNotFoundException($"Package directory not found: {task.PackagePath}");
                }

                progress?.Report("Executing PowerShell script...");
                result = ExecutePowerShellScript(scriptPath, arguments, progress);

                if (result.Success && !string.IsNullOrEmpty(result.CatalogPath))
                {
                    progress?.Report($"✓ Catalog generated successfully: {Path.GetFileName(result.CatalogPath)}");
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

        private CatalogResult ExecutePowerShellScript(string scriptPath, string arguments, IProgress<string>? progress)
        {
            var result = new CatalogResult();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)!
                }
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);

                    // Parse the output for real-time progress
                    if (e.Data.Contains(":"))
                    {
                        progress?.Report(e.Data.Substring(e.Data.IndexOf(':') + 1).Trim());
                    }
                    else
                    {
                        progress?.Report(e.Data);
                    }

                    Debug.WriteLine($"WDAC Script: {e.Data}");
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    Debug.WriteLine($"WDAC Error: {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for process with timeout
            bool completed = process.WaitForExit(900000); // 15 minutes timeout

            if (!completed)
            {
                process.Kill();
                throw new TimeoutException("Catalog generation timed out after 15 minutes");
            }

            result.LogOutput = outputBuilder.ToString();
            var errorOutput = errorBuilder.ToString();

            // Parse the result from output
            var outputLines = result.LogOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var resultLine = outputLines.LastOrDefault(l => l.StartsWith("SUCCESS|") || l.StartsWith("ERROR|"));

            if (resultLine != null)
            {
                var parts = resultLine.Split('|');
                if (parts.Length >= 3)
                {
                    if (parts[0] == "SUCCESS")
                    {
                        result.Success = true;
                        result.CatalogPath = parts[1];
                        result.Hash = parts[2];
                    }
                    else
                    {
                        result.Success = false;
                        result.ErrorMessage = parts[1];
                    }
                }
            }
            else if (process.ExitCode == 0)
            {
                // Fallback parsing for VM script
                result.Success = true;
                result.CatalogPath = FindCatalogInOutput(result.LogOutput);
                result.Hash = ExtractHashFromOutput(result.LogOutput);
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = $"PowerShell script failed with exit code {process.ExitCode}";

                if (!string.IsNullOrEmpty(errorOutput))
                {
                    result.ErrorMessage += $"\nError output: {errorOutput}";
                }
            }

            return result;
        }

       

        private string BuildVMScriptArguments(CatalogTask task)
        {
            return $"-ExecutionPolicy Bypass -File \"{_vmScriptPath}\" " +
                   $"-ApplicationPath \"{task.PackagePath}\" " +
                   $"-Verbose";
        }

    

        // Existing methods for backward compatibility
        private string FindCatalogInOutput(string output)
        {
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(".cat") && (line.Contains("created") || line.Contains("generated")))
                {
                    // Try to extract file path
                    var parts = line.Split(new char[] { '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.EndsWith(".cat") && File.Exists(part))
                        {
                            return part;
                        }
                    }
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

        // Existing methods...
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
                            Arguments = $"-Command \"Test-Path '{catalogPath}'\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    return process.ExitCode == 0 && output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
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