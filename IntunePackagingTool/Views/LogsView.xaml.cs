using IntunePackagingTool.Services;
using IntunePackagingTool.Utilities;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IntunePackagingTool.Views
{
    public partial class LogsView : UserControl
    {
        private IntuneDiagnosticsService _diagnosticsService;
        private ObservableCollection<LogEntry> _logEntries;
        private ObservableCollection<Win32AppStatus> _win32Apps;
        private ObservableCollection<LogFileInfo> _allLogs;
        private ObservableCollection<LogFileInfo> _intuneLogs;
        private ObservableCollection<LogFileInfo> _systemLogs;
        private ObservableCollection<LogFileInfo> _userLogs;
        private string _currentComputerName;
        private bool _isLocalComputer;

        public LogsView()
        {
            InitializeComponent();
            _diagnosticsService = new IntuneDiagnosticsService();
            _logEntries = new ObservableCollection<LogEntry>();
            _win32Apps = new ObservableCollection<Win32AppStatus>();
            _allLogs = new ObservableCollection<LogFileInfo>();
            _intuneLogs = new ObservableCollection<LogFileInfo>();
            _systemLogs = new ObservableCollection<LogFileInfo>();
            _userLogs = new ObservableCollection<LogFileInfo>();

            // Bind grids to collections
            AllLogsGrid.ItemsSource = _allLogs;
            IntuneLogsGrid.ItemsSource = _intuneLogs;
            SystemLogsGrid.ItemsSource = _systemLogs;
            UserLogsGrid.ItemsSource = _userLogs;

            // Set default computer name
            ComputerNameTextBox.Text = Environment.MachineName;
        }

        private async void GetLogsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadLogs();
        }

      

        private async Task LoadLogs()
        {
            _currentComputerName = ComputerNameTextBox.Text.Trim();

            if (string.IsNullOrEmpty(_currentComputerName))
            {
                MessageBox.Show("Please enter a computer name", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check if local computer
            _isLocalComputer = string.IsNullOrEmpty(_currentComputerName) ||
                              _currentComputerName.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                              _currentComputerName.Equals("localhost", StringComparison.OrdinalIgnoreCase);

            // Show loading
            LoadingOverlay.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            LogTabControl.Visibility = Visibility.Collapsed;
            GetLogsButton.IsEnabled = false;

            StatusText.Text = $"Retrieving logs from {_currentComputerName}...";

            try
            {
                var result = await _diagnosticsService.GetDiagnosticsAsync(_currentComputerName);

                if (result.IsSuccess)
                {
                    // Clear existing data
                    _logEntries.Clear();
                    _win32Apps.Clear();
                    _allLogs.Clear();
                    _intuneLogs.Clear();
                    _systemLogs.Clear();
                    _userLogs.Clear();

                    // Populate log entries
                    foreach (var entry in result.RecentLogEntries)
                    {
                        _logEntries.Add(ParseLogEntry(entry));
                    }

                    // Populate Win32 apps
                    foreach (var app in result.Win32Apps)
                    {
                        _win32Apps.Add(app);
                    }

                    // Categorize and populate log files
                    foreach (var file in result.LogFiles)
                    {
                        // Add to "All Logs"
                        _allLogs.Add(file);

                        // Categorize by source
                        if (file.Source == "Intune Management Extension")
                        {
                            _intuneLogs.Add(file);
                        }
                        else if (file.Source == "NBB System Installations")
                        {
                            _systemLogs.Add(file);
                        }
                        else if (file.Source == "NBB User Installations")
                        {
                            _userLogs.Add(file);
                        }
                    }

                    // Show/hide user logs empty state for remote computers
                    if (!_isLocalComputer && _userLogs.Count == 0)
                    {
                        UserLogsEmptyState.Visibility = Visibility.Visible;
                        UserLogsGrid.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        UserLogsEmptyState.Visibility = Visibility.Collapsed;
                        UserLogsGrid.Visibility = Visibility.Visible;
                    }

                    // Show results
                    LogTabControl.Visibility = Visibility.Visible;

                    // Update status with counts
                    var statusParts = new List<string>();
                    if (_intuneLogs.Count > 0) statusParts.Add($"{_intuneLogs.Count} Intune log(s)");
                    if (_systemLogs.Count > 0) statusParts.Add($"{_systemLogs.Count} system app log(s)");
                    if (_userLogs.Count > 0) statusParts.Add($"{_userLogs.Count} user app log(s)");

                    StatusText.Text = $"Loaded from {_currentComputerName}: {string.Join(", ", statusParts)}";
                    LastUpdatedText.Text = $"Last updated: {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    var errorMessage = string.Join("\n", result.Errors);
                    MessageBox.Show($"Failed to retrieve logs:\n\n{errorMessage}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = "Failed to retrieve logs";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error occurred";
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                GetLogsButton.IsEnabled = true;

                if (_allLogs.Count == 0)
                {
                    EmptyState.Visibility = Visibility.Visible;
                }
            }
        }

        private LogEntry ParseLogEntry(string logLine)
        {
            var entry = new LogEntry { Message = logLine };

            // Determine log level by keywords
            if (logLine.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = "ERROR";
                entry.LevelColor = Brushes.Red;
            }
            else if (logLine.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = "WARN";
                entry.LevelColor = Brushes.Orange;
            }
            else if (logLine.Contains("INFO", StringComparison.OrdinalIgnoreCase))
            {
                entry.Level = "INFO";
                entry.LevelColor = Brushes.Blue;
            }
            else
            {
                entry.Level = "DEBUG";
                entry.LevelColor = Brushes.Gray;
            }

            return entry;
        }






        // LogsView.xaml.cs - Updated ViewLogFile_Click method
        private async void ViewLogFile_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var logFile = button?.DataContext as LogFileInfo;

            if (logFile != null && !string.IsNullOrEmpty(logFile.Path))
            {
                try
                {
                    string cmTracePath = @"C:\Windows\CCM\CMTrace.exe";

                    // Check if CMTrace exists
                    if (!File.Exists(cmTracePath))
                    {
                        // Try alternative location
                        cmTracePath = @"C:\Windows\System32\CMTrace.exe";

                        if (!File.Exists(cmTracePath))
                        {
                            MessageBox.Show(
                                "CMTrace.exe not found.\n\n" +
                                "Please ensure Configuration Manager client tools are installed.\n" +
                                "Opening in Notepad instead.",
                                "CMTrace Not Found",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);

                            // Fallback to notepad
                            try
                            {
                                var psi = new ProcessStartInfo
                                {
                                    FileName = "notepad.exe",
                                    Arguments = $"\"{logFile.Path}\"",
                                    UseShellExecute = true
                                };
                                Process.Start(psi);
                            }
                            catch (Exception notepadEx)
                            {
                                MessageBox.Show($"Error opening file in Notepad: {notepadEx.Message}",
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                            return;
                        }
                    }

                    // For remote files, copy to temp first (CMTrace might have issues with UNC paths)
                    string fileToOpen = logFile.Path;

                    if (logFile.Path.StartsWith(@"\\"))
                    {
                        button.Content = "Copying...";
                        button.IsEnabled = false;

                        string tempFile = Path.Combine(Path.GetTempPath(), logFile.Name);

                        await Task.Run(() =>
                        {
                            using (var source = new FileStream(logFile.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            using (var dest = new FileStream(tempFile, FileMode.Create))
                            {
                                source.CopyTo(dest);
                            }
                        });

                        fileToOpen = tempFile;
                    }

                    // Launch CMTrace with the log file
                    Process.Start(cmTracePath, $"\"{fileToOpen}\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    button.Content = "View";
                    button.IsEnabled = true;
                }
            }
        }

      

        

        private void SaveLogFile_Click(object sender, RoutedEventArgs e)
        {
            // Save selected log file
        }
    }

    public class LogEntry
    {
        public string Level { get; set; }
        public string Message { get; set; }
        public Brush LevelColor { get; set; }
    }
}