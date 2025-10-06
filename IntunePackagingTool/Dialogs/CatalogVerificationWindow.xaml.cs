using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace IntunePackagingTool.Dialogs
{
    public partial class CatalogVerificationWindow : Window
    {
        public CatalogVerificationWindow(List<string> catalogPaths)
        {
            InitializeComponent();

            // Show immediate feedback
            SummaryText.Text = $"Starting verification for {catalogPaths.Count} catalog file(s)...";

            // Start verification after window is loaded
            Loaded += async (s, e) =>
            {
                try
                {
                    await VerifyCatalogs(catalogPaths);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fatal error: {ex.Message}\n\n{ex.StackTrace}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
        }

        private async System.Threading.Tasks.Task VerifyCatalogs(List<string> catalogPaths)
        {
            try
            {
                SummaryText.Text = $"Verifying {catalogPaths.Count} catalog file(s)...";

                foreach (var catalogPath in catalogPaths)
                {
                    try
                    {
                        await AddCatalogResult(catalogPath);
                    }
                    catch (Exception ex)
                    {
                        // Add error to UI
                        var errorBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(255, 235, 235)),
                            BorderBrush = Brushes.Red,
                            BorderThickness = new Thickness(1),
                            Margin = new Thickness(0, 0, 0, 20),
                            Padding = new Thickness(20)
                        };

                        var errorText = new TextBlock
                        {
                            Text = $"Exception for {Path.GetFileName(catalogPath)}:\n{ex.Message}",
                            Foreground = Brushes.Red,
                            TextWrapping = TextWrapping.Wrap
                        };

                        errorBorder.Child = errorText;
                        ResultsPanel.Children.Add(errorBorder);
                    }
                }

                SummaryText.Text = $"Verification complete for {catalogPaths.Count} catalog file(s)";
            }
            catch (Exception ex)
            {
                SummaryText.Text = $"Error: {ex.Message}";
                MessageBox.Show($"Error during verification: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async System.Threading.Tasks.Task AddCatalogResult(string catalogPath)
        {
            // Create catalog section
            var catalogBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(222, 226, 230)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 20),
                Padding = new Thickness(20)
            };

            var stackPanel = new StackPanel();

            // Header
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 15)
            };

            var icon = new TextBlock
            {
                Text = File.Exists(catalogPath) ? "✅" : "❌",
                FontSize = 20,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var titlePanel = new StackPanel();

            var title = new TextBlock
            {
                Text = Path.GetFileName(catalogPath),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
            };

            var path = new TextBlock
            {
                Text = catalogPath,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 117, 125)),
                Margin = new Thickness(0, 3, 0, 0)
            };

            titlePanel.Children.Add(title);
            titlePanel.Children.Add(path);

            headerPanel.Children.Add(icon);
            headerPanel.Children.Add(titlePanel);

            stackPanel.Children.Add(headerPanel);

            // Add to UI immediately so user sees progress
            catalogBorder.Child = stackPanel;
            ResultsPanel.Children.Add(catalogBorder);

            if (!File.Exists(catalogPath))
            {
                var errorText = new TextBlock
                {
                    Text = "File not found",
                    Foreground = Brushes.Red,
                    FontWeight = FontWeights.Medium
                };
                stackPanel.Children.Add(errorText);
                return;
            }

            // Run verification
            var results = await System.Threading.Tasks.Task.Run(() => VerifyCatalogWithPowerShell(catalogPath));

            if (results.HasError)
            {
                var errorText = new TextBlock
                {
                    Text = $"Error: {results.ErrorMessage}",
                    Foreground = Brushes.Red,
                    FontWeight = FontWeights.Medium,
                    TextWrapping = TextWrapping.Wrap
                };
                stackPanel.Children.Add(errorText);
            }
            else
            {
                // Status
                var statusPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 15)
                };

                var statusLabel = new TextBlock
                {
                    Text = "Status: ",
                    FontWeight = FontWeights.SemiBold
                };

                var statusValue = new TextBlock
                {
                    Text = results.IsValid ? "Valid" : "Invalid",
                    Foreground = results.IsValid ? Brushes.Green : Brushes.Red,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(5, 0, 0, 0)
                };

                statusPanel.Children.Add(statusLabel);
                statusPanel.Children.Add(statusValue);
                stackPanel.Children.Add(statusPanel);

                // File count
                var countText = new TextBlock
                {
                    Text = $"Total files in catalog: {results.FileCount}",
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                stackPanel.Children.Add(countText);

                // Files DataGrid
                if (results.Files.Count > 0)
                {
                    var filesLabel = new TextBlock
                    {
                        Text = "Catalog Items:",
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 10, 0, 10)
                    };
                    stackPanel.Children.Add(filesLabel);

                    var dataGrid = new DataGrid
                    {
                        ItemsSource = results.Files,
                        AutoGenerateColumns = false,
                        IsReadOnly = true,
                        Height = 300,
                        CanUserSortColumns = true,
                        GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                        HeadersVisibility = DataGridHeadersVisibility.Column,
                        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250))
                    };

                    dataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = "File Path",
                        Binding = new System.Windows.Data.Binding("Path"),
                        Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                    });

                    dataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = "Hash",
                        Binding = new System.Windows.Data.Binding("Hash"),
                        Width = 500
                    });

                    stackPanel.Children.Add(dataGrid);
                }
                else
                {
                    var noFilesText = new TextBlock
                    {
                        Text = "No files found in catalog",
                        Foreground = Brushes.Orange,
                        FontStyle = FontStyles.Italic
                    };
                    stackPanel.Children.Add(noFilesText);
                }
            }
        }

        private CatalogVerificationResult VerifyCatalogWithPowerShell(string catalogPath)
        {
            var result = new CatalogVerificationResult();
            string tempCatalogPath = null;
            string tempScriptPath = null;
            Process process = null;
            bool useLocalCopy = false;

            try
            {
                Debug.WriteLine($"=== Starting catalog verification ===");
                Debug.WriteLine($"Original path: {catalogPath}");

                if (!File.Exists(catalogPath))
                {
                    result.HasError = true;
                    result.ErrorMessage = $"Catalog file not found: {catalogPath}";
                    return result;
                }

                var fileInfo = new FileInfo(catalogPath);
                Debug.WriteLine($"File size: {fileInfo.Length:N0} bytes");

                // Always copy network paths locally to avoid Test-FileCatalog issues
                if (catalogPath.StartsWith(@"\\"))
                {
                    useLocalCopy = true;
                    tempCatalogPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.cat");
                    Debug.WriteLine($"Network path detected. Copying to: {tempCatalogPath}");

                    try
                    {
                        File.Copy(catalogPath, tempCatalogPath, overwrite: true);
                        Debug.WriteLine("Catalog copied successfully");
                    }
                    catch (Exception copyEx)
                    {
                        Debug.WriteLine($"Failed to copy catalog file: {copyEx.Message}");
                        result.HasError = true;
                        result.ErrorMessage = $"Failed to copy catalog file: {copyEx.Message}";
                        return result;
                    }
                }

                string pathToVerify = useLocalCopy ? tempCatalogPath : catalogPath;

                // Get the embedded PowerShell script
                tempScriptPath = Path.Combine(Path.GetTempPath(), $"Verify-Catalog_{Guid.NewGuid()}.ps1");

                // Extract embedded resource (adjust namespace as needed)
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "IntunePackagingTool.Scripts.Verify-Catalog.ps1";

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        // Fallback to inline script if resource not found
                        
                    }
                    else
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            File.WriteAllText(tempScriptPath, reader.ReadToEnd());
                        }
                    }
                }

                // Build PowerShell arguments
                var arguments = $"-NoProfile -ExecutionPolicy Bypass -NonInteractive -File \"{tempScriptPath}\" -CatalogPath \"{pathToVerify}\"";
                if (useLocalCopy)
                {
                    arguments += $" -OriginalPath \"{catalogPath}\"";
                }

                process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetTempPath()
                    }
                };

                Debug.WriteLine("Starting PowerShell process...");

                // Set up async reading
                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();
                var outputComplete = new System.Threading.ManualResetEventSlim(false);
                var errorComplete = new System.Threading.ManualResetEventSlim(false);

                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputBuilder.AppendLine(e.Data);
                        if (e.Data.StartsWith("STATUS:") || e.Data.StartsWith("ITEM:") ||
                            e.Data.StartsWith("ERROR:") || e.Data.StartsWith("WARNING:"))
                        {
                            Debug.WriteLine($"[OUTPUT] {e.Data}");
                        }
                    }
                    else
                    {
                        outputComplete.Set();
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        errorBuilder.AppendLine(e.Data);
                        Debug.WriteLine($"[ERROR] {e.Data}");
                    }
                    else
                    {
                        errorComplete.Set();
                    }
                };

                var stopwatch = Stopwatch.StartNew();
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait with timeout
                if (!process.WaitForExit(60000))
                {
                    Debug.WriteLine($"Process timed out after {stopwatch.Elapsed.TotalSeconds:F1} seconds");
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(5000); // Wait for graceful termination
                        }
                    }
                    catch (Exception killEx)
                    {
                        Debug.WriteLine($"Error killing process: {killEx.Message}");
                    }

                    result.HasError = true;
                    result.ErrorMessage = "Verification process timed out";
                    return result;
                }

                outputComplete.Wait(TimeSpan.FromSeconds(5));
                errorComplete.Wait(TimeSpan.FromSeconds(5));

                stopwatch.Stop();
                Debug.WriteLine($"Process completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds");

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();

                // Update the parsing section in VerifyCatalogWithPowerShell method

                // Parse output
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    string signatureStatus = "Unknown";
                    string catalogStatus = "Unknown";
                    string verificationMethod = "Unknown";
                    int itemCount = 0;
                    bool statusFound = false;

                    foreach (var line in lines)
                    {
                        if (line.StartsWith("STATUS:"))
                        {
                            // This is the final verdict on catalog validity
                            var status = line.Substring(7).Trim();
                            result.IsValid = status.Equals("Valid", StringComparison.OrdinalIgnoreCase);
                            statusFound = true;
                            Debug.WriteLine($"Final catalog status: {status}");
                        }
                        else if (line.StartsWith("SIGNATURE:"))
                        {
                            // This is just the signature status (signed/unsigned)
                            signatureStatus = line.Substring(10).Trim();
                            Debug.WriteLine($"Signature status: {signatureStatus}");
                        }
                        else if (line.StartsWith("CATALOG_STATUS:"))
                        {
                            // Intermediate catalog validation status
                            catalogStatus = line.Substring(15).Trim();
                            Debug.WriteLine($"Catalog validation: {catalogStatus}");
                        }
                        else if (line.StartsWith("SIGNATURE_MESSAGE:"))
                        {
                            var sigMessage = line.Substring(18).Trim();
                            Debug.WriteLine($"Signature message: {sigMessage}");
                        }
                        else if (line.StartsWith("METHOD:"))
                        {
                            verificationMethod = line.Substring(7).Trim();
                            Debug.WriteLine($"Verification method: {verificationMethod}");
                        }
                        else if (line.StartsWith("ITEM_COUNT:"))
                        {
                            int.TryParse(line.Substring(11).Trim(), out itemCount);
                            Debug.WriteLine($"Item count: {itemCount}");
                        }
                        else if (line.StartsWith("ITEM:"))
                        {
                            var itemData = line.Substring(5);
                            var parts = itemData.Split('|');

                            if (parts.Length >= 2)
                            {
                                result.Files.Add(new CatalogFileItem
                                {
                                    Path = parts[0].Trim(),
                                    Hash = parts[1].Trim()
                                });
                            }
                        }
                        else if (line.StartsWith("ERROR:"))
                        {
                            var errorMsg = line.Substring(6).Trim();
                            // Only set as error if it's not a minor issue
                            if (!errorMsg.Contains("Wait-Job") && !errorMsg.Contains("not digitally signed"))
                            {
                                result.HasError = true;
                                result.ErrorMessage = errorMsg;
                            }
                        }
                        else if (line.StartsWith("WARNING:"))
                        {
                            var warningMsg = line.Substring(8).Trim();
                            if (string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                result.ErrorMessage = warningMsg;
                            }
                        }
                    }

                    result.FileCount = result.Files.Count;

                    // Build informative message
                    if (statusFound)
                    {
                        var messages = new List<string>();

                        if (result.IsValid)
                        {
                            messages.Add("Catalog is valid");
                        }
                        else
                        {
                            messages.Add("Catalog is invalid");
                        }

                        if (signatureStatus == "NotSigned")
                        {
                            messages.Add("(unsigned)");
                        }
                        else if (signatureStatus == "Valid")
                        {
                            messages.Add("(signed)");
                        }

                        if (result.FileCount > 0)
                        {
                            messages.Add($"Contains {result.FileCount} files");
                        }
                        else if (result.IsValid && itemCount == 0)
                        {
                            messages.Add("File list unavailable");
                        }

                        if (!result.HasError && messages.Count > 0)
                        {
                            result.ErrorMessage = string.Join(" ", messages);
                            result.HasError = false; // This is informational, not an error
                        }
                    }

                    Debug.WriteLine($"=== Verification complete: Valid={result.IsValid}, Files={result.FileCount}, Signed={signatureStatus} ===");
                }
                else
                {
                    result.HasError = true;
                    result.ErrorMessage = "No output received from verification process";
                }

                // Handle non-zero exit codes only if we don't have a valid status
                if (process.ExitCode != 0 && !result.IsValid && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.HasError = true;
                    result.ErrorMessage = !string.IsNullOrWhiteSpace(error)
                        ? error.Trim()
                        : $"Verification failed with exit code {process.ExitCode}";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EXCEPTION: {ex.Message}");
                result.HasError = true;
                result.ErrorMessage = ex.Message;
                result.IsValid = false;
            }
            finally
            {
                // Cleanup
                process?.Dispose();

                if (tempCatalogPath != null && File.Exists(tempCatalogPath))
                {
                    try { File.Delete(tempCatalogPath); } catch { }
                }

                if (tempScriptPath != null && File.Exists(tempScriptPath))
                {
                    try { File.Delete(tempScriptPath); } catch { }
                }
            }

            return result;
        }

     

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class CatalogVerificationResult
    {
        public bool IsValid { get; set; }
        public int FileCount { get; set; }
        public List<CatalogFileItem> Files { get; set; } = new List<CatalogFileItem>();
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class CatalogFileItem
    {
        public string Path { get; set; }
        public string Hash { get; set; }
    }
}