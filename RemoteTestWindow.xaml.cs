using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace IntunePackagingTool
{
    /// <summary>
    /// Interaction logic for RemoteTestWindow.xaml
    /// </summary>
    public partial class RemoteTestWindow : Window
    {
        private string _currentPackagePath;
        private bool _isComputerOnline = false;

        // Constructor with package path parameter
        public RemoteTestWindow(string packagePath)
        {
            InitializeComponent();
            _currentPackagePath = packagePath;
            txtPackagePath.Text = packagePath;
        }

        // Default constructor for designer support
        public RemoteTestWindow()
        {
            InitializeComponent();
            _currentPackagePath = @"\\FileServer\Apps\DefaultPackage";
            txtPackagePath.Text = _currentPackagePath;
        }

        // Event Handler: Check Online Button Click
        private async void btnCheckOnline_Click(object sender, RoutedEventArgs e)
        {
            string computerName = txtComputerName.Text.Trim();

            if (string.IsNullOrEmpty(computerName))
            {
                MessageBox.Show("Please enter a computer name.", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatus("Checking...", Colors.Orange);
            btnCheckOnline.IsEnabled = false;

            bool isOnline = await Task.Run(() => IsComputerOnline(computerName));

            if (isOnline)
            {
                UpdateStatus("Online", Colors.Green);
                _isComputerOnline = true;
                btnDeploy.IsEnabled = true;
                AppendOutput($"✓ {computerName} is online and ready for deployment.");
            }
            else
            {
                UpdateStatus("Offline", Colors.Red);
                _isComputerOnline = false;
                btnDeploy.IsEnabled = false;
                AppendOutput($"✗ {computerName} is not reachable. Please check the computer name and network connection.");
            }

            btnCheckOnline.IsEnabled = true;
        }

        // Event Handler: Deploy Button Click
        private async void btnDeploy_Click(object sender, RoutedEventArgs e)
        {
            string computerName = txtComputerName.Text.Trim();
            string deploymentType = GetDeploymentType();
            bool cleanup = chkCleanup.IsChecked ?? true;

            // Disable controls during deployment
            SetControlsEnabled(false);
            progressBar.Visibility = Visibility.Visible;
            progressBar.IsIndeterminate = true;
            txtOutput.Text = "";

            AppendOutput($"Starting remote deployment to {computerName}...");
            AppendOutput($"Package: {_currentPackagePath}");
            AppendOutput($"Deployment Type: {deploymentType}");
            AppendOutput("----------------------------------------");

            try
            {
                bool success = await ExecuteRemoteDeployment(
                    computerName, _currentPackagePath, deploymentType, cleanup);

                if (success)
                {
                    AppendOutput("========================================");
                    AppendOutput("✓ DEPLOYMENT COMPLETED SUCCESSFULLY!");
                    AppendOutput("========================================");

                    MessageBox.Show($"Deployment to {computerName} completed successfully!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    AppendOutput("========================================");
                    AppendOutput("✗ DEPLOYMENT FAILED!");
                    AppendOutput("========================================");

                    MessageBox.Show($"Deployment to {computerName} failed. Check the output for details.",
                        "Deployment Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                AppendOutput($"ERROR: {ex.Message}");
                MessageBox.Show($"An error occurred: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
                progressBar.IsIndeterminate = false;
                SetControlsEnabled(true);
            }
        }

        // Event Handler: Close Button Click
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Execute the existing PowerShell script file
        private async Task<bool> ExecuteRemoteDeployment(string computerName,
            string sourcePath, string deploymentType, bool cleanup)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Path to your PowerShell script
                    string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                        "Scripts", "RemoteDeployment.ps1");

                    // Alternative paths to check
                    if (!File.Exists(scriptPath))
                    {
                        // Try embedded resource location
                        scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                            "RemoteDeployment.ps1");
                    }

                    if (!File.Exists(scriptPath))
                    {
                        AppendOutput($"ERROR: PowerShell script not found at {scriptPath}");
                        return false;
                    }

                    AppendOutput($"Using script: {scriptPath}");

                    // Build PowerShell arguments
                    string arguments = $@"-ExecutionPolicy Bypass -File ""{scriptPath}"" " +
                                     $@"-TargetComputer ""{computerName}"" " +
                                     $@"-SourcePath ""{sourcePath}"" " +
                                     $@"-DeploymentType ""{deploymentType}""";

                    if (cleanup)
                    {
                        arguments += " -CleanupAfterDeploy";
                    }

                    // Execute PowerShell
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (Process process = Process.Start(psi))
                    {
                        // Read output asynchronously
                        process.OutputDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                AppendOutput(e.Data);
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            if (!string.IsNullOrEmpty(e.Data))
                            {
                                AppendOutput($"ERROR: {e.Data}");
                            }
                        };

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        return process.ExitCode == 0 || process.ExitCode == 3010;
                    }
                }
                catch (Exception ex)
                {
                    AppendOutput($"Execution error: {ex.Message}");
                    return false;
                }
            });
        }

        // Helper method to check if computer is online
        private bool IsComputerOnline(string computerName)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(computerName, 2000);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        // Helper method to update status indicator
        private void UpdateStatus(string status, Color color)
        {
            Dispatcher.Invoke(() =>
            {
                txtStatus.Text = status;
                statusIndicator.Fill = new SolidColorBrush(color);
            });
        }

        // Helper method to append output to the console
        private void AppendOutput(string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (txtOutput.Text == "Ready to deploy...")
                {
                    txtOutput.Text = "";
                }
                txtOutput.Text += $"[{DateTime.Now:HH:mm:ss}] {text}\n";

                // Auto-scroll to bottom
                txtOutput.CaretIndex = txtOutput.Text.Length;
                txtOutput.ScrollToEnd();
            });
        }

        // Get selected deployment type
        private string GetDeploymentType()
        {
            if (rbInstall.IsChecked == true) return "Install";
            if (rbUninstall.IsChecked == true) return "Uninstall";
            if (rbRepair.IsChecked == true) return "Repair";
            return "Install";
        }

        // Enable/disable controls
        private void SetControlsEnabled(bool enabled)
        {
            btnDeploy.IsEnabled = enabled && _isComputerOnline;
            btnCheckOnline.IsEnabled = enabled;
            txtComputerName.IsEnabled = enabled;
            rbInstall.IsEnabled = enabled;
            rbUninstall.IsEnabled = enabled;
            rbRepair.IsEnabled = enabled;
            chkCleanup.IsEnabled = enabled;
        }
    }
}