using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace IntunePackagingTool.Dialogs
{
    public partial class CdfViewerWindow : Window
    {
        private ObservableCollection<CdfFileEntry> fileEntries = new ObservableCollection<CdfFileEntry>();
        private string currentCdfPath;
        private string cdfContent;

        public CdfViewerWindow()
        {
            InitializeComponent();
            FilesDataGrid.ItemsSource = fileEntries;

            // Optional: Load CDF if passed as parameter
            if (Application.Current.Properties.Contains("CdfPath"))
            {
                string path = Application.Current.Properties["CdfPath"] as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    LoadCdfFile(path);
                }
            }
        }

        public CdfViewerWindow(string cdfPath) : this()
        {
            if (!string.IsNullOrEmpty(cdfPath) && File.Exists(cdfPath))
            {
                LoadCdfFile(cdfPath);
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "CDF Files (*.cdf)|*.cdf|All Files (*.*)|*.*",
                Title = "Select CDF File"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                LoadCdfFile(openFileDialog.FileName);
            }
        }

        private void LoadCdfFile(string cdfPath)
        {
            try
            {
                currentCdfPath = cdfPath;
                CdfPathTextBox.Text = cdfPath;

                // Read the file content
                cdfContent = File.ReadAllText(cdfPath);

                // Display raw content
                RawContentTextBox.Text = cdfContent;
                LineCountText.Text = $"Lines: {cdfContent.Split('\n').Length}";

                // Parse the CDF
                ParseCdfContent(cdfContent);

                // Enable buttons
                OpenInNotepadButton.IsEnabled = true;
                RefreshButton.IsEnabled = true;

                // Check for corresponding catalog file
                CheckForCatalogFile();

                StatusText.Text = $"Loaded: {Path.GetFileName(cdfPath)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading CDF file: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error loading file";
            }
        }

        private void ParseCdfContent(string content)
        {
            fileEntries.Clear();
            var cdfInfo = new CdfInfo();

            // Split into lines
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            string currentSection = "";
            var currentFileEntry = new CdfFileEntry();
            var attributes = new StringBuilder();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Skip comments
                if (trimmedLine.StartsWith(";") || string.IsNullOrEmpty(trimmedLine))
                    continue;

                // Check for section headers
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    // Save previous file entry if exists
                    if (!string.IsNullOrEmpty(currentFileEntry.FilePath))
                    {
                        fileEntries.Add(currentFileEntry);
                        currentFileEntry = new CdfFileEntry();
                    }

                    currentSection = trimmedLine.Substring(1, trimmedLine.Length - 2);
                    continue;
                }

                // Parse key=value pairs
                var equalIndex = trimmedLine.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = trimmedLine.Substring(0, equalIndex).Trim();
                    var value = trimmedLine.Substring(equalIndex + 1).Trim().Trim('"');

                    switch (currentSection.ToUpper())
                    {
                        case "CATALOGHEADER":
                            switch (key.ToUpper())
                            {
                                case "NAME":
                                    cdfInfo.CatalogName = value;
                                    break;
                                case "RESULTATTRCOUNT":
                                    cdfInfo.ResultAttrCount = value;
                                    break;
                                case "HASHALGORITHMS":
                                    cdfInfo.HashAlgorithm = value;
                                    break;
                                case "CATALOGVERSION":
                                    cdfInfo.CatalogVersion = value;
                                    break;
                            }
                            attributes.AppendLine($"{key}={value}");
                            break;

                        case "CATALOGFILES":
                            // Handle file entries
                            if (key.Contains("\\") || key.Contains("/") || key.StartsWith("<"))
                            {
                                // This is likely a file path
                                currentFileEntry.FilePath = key.Trim('<', '>');
                                currentFileEntry.HashAlgorithm = value;

                                // Check if file exists (relative to CDF location)
                                if (!string.IsNullOrEmpty(currentCdfPath))
                                {
                                    var cdfDir = Path.GetDirectoryName(currentCdfPath);
                                    var fullPath = Path.IsPathRooted(currentFileEntry.FilePath)
                                        ? currentFileEntry.FilePath
                                        : Path.Combine(cdfDir, currentFileEntry.FilePath);

                                    currentFileEntry.Exists = File.Exists(fullPath);
                                    if (currentFileEntry.Exists)
                                    {
                                        try
                                        {
                                            var fileInfo = new FileInfo(fullPath);
                                            currentFileEntry.FileSize = FormatFileSize(fileInfo.Length);
                                        }
                                        catch
                                        {
                                            currentFileEntry.FileSize = "N/A";
                                        }
                                    }
                                }

                                fileEntries.Add(currentFileEntry);
                                currentFileEntry = new CdfFileEntry();
                            }
                            else if (!string.IsNullOrEmpty(currentFileEntry.FilePath))
                            {
                                // This is an attribute for the current file
                                currentFileEntry.Attributes += $"{key}={value}; ";
                            }
                            break;

                        default:
                            // Handle file-specific sections
                            if (currentSection.StartsWith("FILE") || currentSection.Contains("\\"))
                            {
                                if (key.ToUpper() == "PATH" || currentSection.Contains("\\"))
                                {
                                    currentFileEntry.FilePath = value;
                                }
                                else if (key.ToUpper().Contains("HASH"))
                                {
                                    currentFileEntry.HashAlgorithm = key;
                                }
                                else
                                {
                                    currentFileEntry.Attributes += $"{key}={value}; ";
                                }
                            }
                            break;
                    }
                }
            }

            // Add last file entry if exists
            if (!string.IsNullOrEmpty(currentFileEntry.FilePath))
            {
                fileEntries.Add(currentFileEntry);
            }

            // Update UI with parsed information
            UpdateCatalogInfo(cdfInfo);
            UpdateSummary();
            AttributesTextBox.Text = attributes.ToString();
        }

        private void UpdateCatalogInfo(CdfInfo info)
        {
            CatalogNameText.Text = $"Catalog Name: {info.CatalogName ?? "Not specified"}";
            HashAlgorithmText.Text = $"Hash Algorithm: {info.HashAlgorithm ?? "SHA256 (default)"}";
            CatalogVersionText.Text = $"Catalog Version: {info.CatalogVersion ?? "2"}";
        }

        private void UpdateSummary()
        {
            var totalFiles = fileEntries.Count;
            var existingFiles = fileEntries.Count(f => f.Exists);
            var missingFiles = totalFiles - existingFiles;

            var summary = $"Total Files: {totalFiles}";
            if (missingFiles > 0)
            {
                summary += $" | Existing: {existingFiles} | Missing: {missingFiles}";
            }

            // Get unique hash algorithms used
            var hashAlgorithms = fileEntries
                .Select(f => f.HashAlgorithm)
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct()
                .ToList();

            if (hashAlgorithms.Any())
            {
                summary += $" | Hash Algorithms: {string.Join(", ", hashAlgorithms)}";
            }

            SummaryText.Text = summary;
        }

        private void CheckForCatalogFile()
        {
            if (string.IsNullOrEmpty(currentCdfPath))
                return;

            // Check for .cat file with same name
            var catPath = Path.ChangeExtension(currentCdfPath, ".cat");
            if (File.Exists(catPath))
            {
                CatalogFilePathText.Text = $"Found: {catPath}";
                VerifyCatalogButton.IsEnabled = true;
            }
            else
            {
                CatalogFilePathText.Text = "No corresponding .cat file found";
                VerifyCatalogButton.IsEnabled = false;
            }

            CreateCatalogButton.IsEnabled = true;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void OpenInNotepad_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(currentCdfPath) && File.Exists(currentCdfPath))
            {
                try
                {
                    Process.Start("notepad.exe", currentCdfPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening file in Notepad: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(currentCdfPath) && File.Exists(currentCdfPath))
            {
                LoadCdfFile(currentCdfPath);
            }
        }

        private void WordWrap_Changed(object sender, RoutedEventArgs e)
        {
            if (RawContentTextBox != null)
            {
                RawContentTextBox.TextWrapping = WordWrapCheckBox.IsChecked == true
                    ? TextWrapping.Wrap
                    : TextWrapping.NoWrap;
            }
        }

        private void CopyRaw_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(cdfContent))
            {
                Clipboard.SetText(cdfContent);
                StatusText.Text = "Content copied to clipboard";
            }
        }

        private void VerifyCatalog_Click(object sender, RoutedEventArgs e)
        {
            var catPath = Path.ChangeExtension(currentCdfPath, ".cat");
            if (File.Exists(catPath))
            {
                // Launch your existing catalog verification window
                var verifyWindow = new CatalogVerificationWindow(new List<string> { catPath });
                verifyWindow.ShowDialog();
            }
        }

        private void CreateCatalog_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentCdfPath))
                return;

            try
            {
                // Validate CDF path to prevent command injection
                if (!File.Exists(currentCdfPath))
                {
                    MessageBox.Show("CDF file not found.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Ensure path doesn't contain potentially dangerous characters
                var fileName = Path.GetFileName(currentCdfPath);
                if (fileName.IndexOfAny(new[] { '&', '|', ';', '>', '<', '^' }) >= 0)
                {
                    MessageBox.Show("CDF file path contains invalid characters.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var catPath = Path.ChangeExtension(currentCdfPath, ".cat");
                var result = MessageBox.Show(
                    $"Create catalog from this CDF?\n\nCDF: {currentCdfPath}\nOutput: {catPath}\n\nThis will run: makecat.exe",
                    "Create Catalog",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var workingDir = Path.GetDirectoryName(currentCdfPath);
                    if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
                    {
                        MessageBox.Show("Invalid working directory.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "makecat.exe",
                            Arguments = $"\"{currentCdfPath}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            WorkingDirectory = workingDir
                        }
                    };

                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);

                    if (process.ExitCode == 0 && File.Exists(catPath))
                    {
                        MessageBox.Show($"Catalog created successfully!\n\n{catPath}", "Success",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        CheckForCatalogFile();
                    }
                    else
                    {
                        var message = $"Failed to create catalog.\nExit code: {process.ExitCode}";
                        if (!string.IsNullOrEmpty(error))
                            message += $"\nError: {error}";
                        if (!string.IsNullOrEmpty(output))
                            message += $"\nOutput: {output}";

                        MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating catalog: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class CdfFileEntry
    {
        public string FilePath { get; set; }
        public string HashAlgorithm { get; set; }
        public bool Exists { get; set; }
        public string FileSize { get; set; } = "N/A";
        public string Attributes { get; set; } = "";
    }

    public class CdfInfo
    {
        public string CatalogName { get; set; }
        public string HashAlgorithm { get; set; }
        public string CatalogVersion { get; set; }
        public string ResultAttrCount { get; set; }
    }
}