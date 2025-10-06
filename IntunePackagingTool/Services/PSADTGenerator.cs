using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using IntunePackagingTool.Models;
using IntunePackagingTool.Configuration;

namespace IntunePackagingTool.Services
{
    public class PSADTOptions
    {
        // Installation Options
        public bool SilentInstall { get; set; }
        public bool SuppressRestart { get; set; }
        public bool AllUsersInstall { get; set; }
        public bool VerboseLogging { get; set; }
        public bool WaitForProcessCompletion { get; set; }  
        public bool ImportRegFile { get; set; }             
        public bool UninstallPreviousByCode { get; set; }   
        public bool UserInstall { get; set; }  

        // User Interaction
        public bool CloseRunningApps { get; set; }
        public bool AllowUserDeferrals { get; set; }
        public bool CheckDiskSpace { get; set; }
        public bool ShowProgress { get; set; }

        // Prerequisites
        public bool CheckDotNet { get; set; }
        public bool ImportCertificates { get; set; }
        public bool CheckVCRedist { get; set; }
        public bool RegisterDLLs { get; set; }

        // File & Registry Operations
        public bool CopyToAllUsers { get; set; }
        public bool SetHKCUAllUsers { get; set; }
        public bool SetCustomRegistry { get; set; }
        public bool CopyConfigFiles { get; set; }
        public bool RemoveSpecificFiles { get; set; }       
        public bool RemoveEmptyFolders { get; set; }        
        public bool ModifyFilePermissions { get; set; }     

        // Shortcuts & Cleanup
        public bool DesktopShortcut { get; set; }
        public bool StartMenuEntry { get; set; }
        public bool RemovePreviousVersions { get; set; }
        public bool CreateInstallMarker { get; set; }
        public bool ExecuteVBScript { get; set; }           
        public bool CreateActiveSetup { get; set; }         

        public bool AddToPath { get; set; }                 
        public bool UnregisterDLLs { get; set; }            
        public bool ImportDrivers { get; set; }             

        // Package Info
        public string PackageType { get; set; }    
    }

    public class PSADTGenerator
    {
        private readonly string _baseOutputPath = Paths.IntuneApplications;
        private readonly string _templatePath = Paths.PSADTTemplate;
        public string BaseOutputPath => _baseOutputPath;
        public string TemplatePath => _templatePath;

        public async Task<string> CreatePackageAsync(ApplicationInfo appInfo, PSADTOptions? psadtOptions = null)
        {
            try
            {
                // Create folder structure: Manufacturer_AppName\Version\
                var cleanManufacturer = appInfo.Manufacturer.Replace(" ", "_");
                var cleanAppName = appInfo.Name.Replace(" ", "_");
                var appFolderName = $"{cleanManufacturer}_{cleanAppName}";
                var appBasePath = Path.Combine(_baseOutputPath, appFolderName);
                var packagePath = Path.Combine(appBasePath, appInfo.Version);

                Debug.WriteLine($"Creating package at: {packagePath}");

                // Check if version folder already exists
                if (Directory.Exists(packagePath))
                {
                    var result = MessageBox.Show(
                        $"Version {appInfo.Version} already exists for {cleanManufacturer}_{cleanAppName}.\n\n" +
                        $"Do you want to overwrite it?",
                        "Version Exists",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.No)
                    {
                        throw new InvalidOperationException("Package creation cancelled by user.");
                    }

                    Directory.Delete(packagePath, true);
                    await Task.Delay(500); // Brief delay to ensure deletion completes
                }

                // Create version folder
                Directory.CreateDirectory(packagePath);
                Debug.WriteLine($"Created version folder: {packagePath}");

                // Create required subfolders
                var folders = new[] { "Application", "Documentation", "Icon", "Intune", "NBB_Info", "Project Files", "Sources" };
                foreach (var folder in folders)
                {
                    var fullPath = Path.Combine(packagePath, folder);
                    Directory.CreateDirectory(fullPath);
                }

                // Copy template files from archive to Application folder
                await CopyTemplateFilesAsync(packagePath);

                // Copy source files to Application\Files if provided
                if (!string.IsNullOrWhiteSpace(appInfo.SourcesPath))
                {
                    bool hasValidSource = false;

                    if (appInfo.SourcesPath.Contains(";"))
                    {
                        // Multiple files - check if at least one exists
                        var filePaths = appInfo.SourcesPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        hasValidSource = filePaths.Any(path => File.Exists(path.Trim()));
                    }
                    else
                    {
                        // Single file or directory
                        hasValidSource = File.Exists(appInfo.SourcesPath) || Directory.Exists(appInfo.SourcesPath);
                    }

                    if (hasValidSource)
                    {
                        await CopySourceFilesToApplicationAsync(appInfo.SourcesPath, packagePath);
                    }
                    else
                    {
                        Debug.WriteLine($"Warning: Source path not found or invalid: {appInfo.SourcesPath}");
                    }
                }

                // Modify the Deploy-Application.ps1 with metadata AND cheatsheet functions
                await ModifyPSADTScriptAsync(packagePath, appInfo, psadtOptions);

                if (psadtOptions != null && psadtOptions.UserInstall)
                {
                    await ModifyAppDeployToolkitConfigAsync(packagePath, psadtOptions);
                }

                Debug.WriteLine($"✅ Package created successfully at: {packagePath}");
                return packagePath;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error creating package: {ex.Message}", ex);
            }
        }

        private async Task CopyTemplateFilesAsync(string packagePath)
        {
            try
            {
                var destinationAppFolder = Path.Combine(packagePath, "Application");

                if (!Directory.Exists(_templatePath))
                {
                    throw new DirectoryNotFoundException($"Template path not found: {_templatePath}");
                }

                await Task.Run(() =>
                {
                    CopyDirectory(_templatePath, destinationAppFolder, true);
                });

                Debug.WriteLine($"Copied template files to: {destinationAppFolder}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error copying template files: {ex.Message}", ex);
            }
        }

        private async Task CopySourceFilesToApplicationAsync(string sourcePath, string packagePath)
        {
            try
            {
                var applicationFilesPath = Path.Combine(packagePath, "Application", "Files");
                Directory.CreateDirectory(applicationFilesPath);

                await Task.Run(() =>
                {
                    if (sourcePath.Contains(";"))
                    {
                        // Handle multiple files
                        var filePaths = sourcePath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var filePath in filePaths)
                        {
                            var trimmedPath = filePath.Trim();
                            if (File.Exists(trimmedPath))
                            {
                                var fileName = Path.GetFileName(trimmedPath);
                                var destinationFile = Path.Combine(applicationFilesPath, fileName);
                                File.Copy(trimmedPath, destinationFile, true);
                                Debug.WriteLine($"Copied: {fileName}");
                            }
                        }
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // Single file
                        var fileName = Path.GetFileName(sourcePath);
                        var destinationFile = Path.Combine(applicationFilesPath, fileName);
                        File.Copy(sourcePath, destinationFile, true);
                        Debug.WriteLine($"Copied: {fileName}");
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        // Directory
                        CopyDirectory(sourcePath, applicationFilesPath, true);
                        Debug.WriteLine($"Copied directory contents");
                    }
                });
            }
            catch (Exception ex)
            {
                throw new Exception($"Error copying source files: {ex.Message}", ex);
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        private async Task ModifyAppDeployToolkitConfigAsync(string packagePath, PSADTOptions psadtOptions)
        {
            try
            {
                var configPath = Path.Combine(packagePath, "Application", "AppDeployToolkit", "AppDeployToolkitConfig.xml");

                if (!File.Exists(configPath))
                {
                    Debug.WriteLine($"Warning: AppDeployToolkitConfig.xml not found at: {configPath}");
                    return;
                }

                // Read the XML file
                var xmlContent = await File.ReadAllTextAsync(configPath);
                var doc = System.Xml.Linq.XDocument.Parse(xmlContent);

                // Check if UserInstall is enabled
                if (psadtOptions.UserInstall)
                {
                    // Find the Toolkit_RequireAdmin element
                    var requireAdminElement = doc.Descendants("Toolkit_RequireAdmin").FirstOrDefault();

                    if (requireAdminElement != null)
                    {
                        // Change from True to False
                        requireAdminElement.Value = "False";
                        Debug.WriteLine("✅ Set Toolkit_RequireAdmin to False for User Install mode");
                    }
                    else
                    {
                        Debug.WriteLine("⚠️ Toolkit_RequireAdmin element not found in config");
                    }
                }

                    await File.WriteAllTextAsync(configPath, doc.ToString());
                    Debug.WriteLine($"✅ Modified AppDeployToolkitConfig.xml at: {configPath}");
                }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error modifying AppDeployToolkitConfig.xml: {ex.Message}");
                
            }
        }
    

        private async Task ModifyPSADTScriptAsync(string packagePath, ApplicationInfo appInfo, PSADTOptions? psadtOptions)
        {
            try
            {
                var scriptPath = Path.Combine(packagePath, "Application", "Deploy-Application.ps1");

                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException($"Deploy-Application.ps1 not found: {scriptPath}");
                }

                // Read the existing script
                var scriptContent = await File.ReadAllTextAsync(scriptPath);

                // Update the metadata variables in the script
                scriptContent = UpdateScriptMetadata(scriptContent, appInfo);

                // If PSADT options are provided, inject cheatsheet functions
                if (psadtOptions != null)
                {
                    Debug.WriteLine($"Injecting PSADT cheatsheet functions for {CountEnabledOptions(psadtOptions)} enabled options...");
                    scriptContent = InjectPSADTCheatsheetFunctions(scriptContent, appInfo, psadtOptions);
                }
                else
                {
                    Debug.WriteLine("No PSADT options provided, skipping cheatsheet injection");
                }

                // Write the modified script back
                await File.WriteAllTextAsync(scriptPath, scriptContent);

                Debug.WriteLine($"✅ Modified Deploy-Application.ps1 successfully");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error modifying PSADT script: {ex.Message}", ex);
            }
        }

        private int CountEnabledOptions(PSADTOptions options)
        {
            int count = 0;
            var properties = typeof(PSADTOptions).GetProperties();
            foreach (var prop in properties)
            {
                if (prop.PropertyType == typeof(bool) && prop.GetValue(options) is bool value && value)
                {
                    count++;
                }
            }
            return count;
        }

        private string UpdateScriptMetadata(string scriptContent, ApplicationInfo appInfo)
        {
            string currentUser = Environment.UserName;
            var lines = scriptContent.Split('\n').ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();

                if (line.Contains("$appVendor") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appVendor = '{appInfo.Manufacturer}'";
                }
                else if (line.Contains("$appName") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appName = '{appInfo.Name}'";
                }
                else if (line.Contains("$appVersion") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appVersion = '{appInfo.Version}'";
                }
                else if (line.Contains("$appArch") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appArch = 'x64'";
                }
                else if (line.Contains("$appScriptDate") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appScriptDate = '{DateTime.Now:dd/MM/yyyy}'";
                }
                else if (line.Contains("$appScriptAuthor") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appScriptAuthor = '{currentUser}'";
                }
                else if (line.Contains("$ServiceNowSRI") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$ServiceNowSRI = '{appInfo.ServiceNowSRI}'";
                }
                else if (line.Contains("$appScriptAuthor") && !scriptContent.Contains("$ServiceNowSRI"))
                {
                    lines.Insert(i + 1, $"\t[string]$ServiceNowSRI = '{appInfo.ServiceNowSRI}'");
                    break;
                }
            }

            return string.Join('\n', lines);
        }

        private string InjectPSADTCheatsheetFunctions(string scriptContent, ApplicationInfo appInfo, PSADTOptions options)
        {
            var lines = scriptContent.Split('\n').ToList();
            int functionsInjected = 0;

            // Find insertion points for different sections
            int preInstallIndex = FindSectionIndex(lines, "Pre-Installation");
            int installIndex = FindSectionIndex(lines, "Installation");
            int postInstallIndex = FindSectionIndex(lines, "Post-Installation");
            int uninstallIndex = FindSectionIndex(lines, "Uninstallation");

            Debug.WriteLine($"Section indices - PreInstall: {preInstallIndex}, Install: {installIndex}, PostInstall: {postInstallIndex}, Uninstall: {uninstallIndex}");

            // Insert PRE-INSTALLATION functions
            if (preInstallIndex > 0)
            {
                var preInstallCode = GeneratePreInstallationCode(appInfo, options);
                if (!string.IsNullOrWhiteSpace(preInstallCode))
                {
                    InsertCodeBlock(lines, preInstallIndex, preInstallCode);
                    functionsInjected++;

                    var insertedLines = preInstallCode.Split('\n').Length;
                    installIndex += insertedLines;
                    postInstallIndex += insertedLines;
                    uninstallIndex += insertedLines;
                }
            }

            // Insert INSTALLATION functions
            if (installIndex > 0)
            {
                var installCode = GenerateInstallationCode(appInfo, options);
                if (!string.IsNullOrWhiteSpace(installCode))
                {
                    InsertCodeBlock(lines, installIndex, installCode);
                    functionsInjected++;

                    var insertedLines = installCode.Split('\n').Length;
                    postInstallIndex += insertedLines;
                    uninstallIndex += insertedLines;
                }
            }

            // Insert POST-INSTALLATION functions
            if (postInstallIndex > 0)
            {
                var postInstallCode = GeneratePostInstallationCode(appInfo, options);
                if (!string.IsNullOrWhiteSpace(postInstallCode))
                {
                    InsertCodeBlock(lines, postInstallIndex, postInstallCode);
                    functionsInjected++;

                    var insertedLines = postInstallCode.Split('\n').Length;
                    uninstallIndex += insertedLines;
                }
            }

            // Insert UNINSTALLATION functions
            if (uninstallIndex > 0)
            {
                var uninstallCode = GenerateUninstallationCode(appInfo, options);
                if (!string.IsNullOrWhiteSpace(uninstallCode))
                {
                    InsertCodeBlock(lines, uninstallIndex, uninstallCode);
                    functionsInjected++;
                }
            }

            Debug.WriteLine($"✅ Injected {functionsInjected} PSADT cheatsheet sections");
            return string.Join('\n', lines);
        }

        private int FindSectionIndex(List<string> lines, string sectionName)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains($"<Perform {sectionName} tasks here>"))
                {
                    return i + 1; // Insert AFTER this line
                }
            }
            return -1;
        }

        private void InsertCodeBlock(List<string> lines, int index, string codeBlock)
        {
            var codeLines = codeBlock.Split('\n');
            for (int i = 0; i < codeLines.Length; i++)
            {
                lines.Insert(index + i, codeLines[i]);
            }
        }

        private string GeneratePreInstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();
            

            // Show Progress
            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t## Show installation progress");
                sb.AppendLine($"\t\tShow-InstallationProgress -StatusMessage 'Preparing to install {appInfo.Name}...'");
                sb.AppendLine();
            }

            // User Interaction
            if (options.CloseRunningApps || options.AllowUserDeferrals || options.CheckDiskSpace)
            {
                var welcomeParams = new List<string>();

                if (options.CloseRunningApps)
                {
                    welcomeParams.Add("-CloseApps 'notepad,excel,winword,outlook'");
                    welcomeParams.Add("-CloseAppsCountdown 300");
                }

                if (options.AllowUserDeferrals)
                {
                    welcomeParams.Add("-AllowDefer");
                    welcomeParams.Add("-DeferTimes 3");
                    welcomeParams.Add("-DeferDeadline '12/31/2025 23:59:59'");
                }

                if (options.CheckDiskSpace)
                {
                    welcomeParams.Add("-CheckDiskSpace");
                    welcomeParams.Add("-RequiredDiskSpace 1024");
                }

                if (welcomeParams.Any())
                {
                    sb.AppendLine("\t\t## Show welcome dialog with user interaction options");
                    sb.AppendLine($"\t\tShow-InstallationWelcome {string.Join(" ", welcomeParams)} -PersistPrompt");
                    sb.AppendLine();
                }
            }

            // Uninstall Previous Versions by Product Code
            if (options.UninstallPreviousByCode)
            {
                sb.AppendLine("\t\t## Uninstall previous versions by MSI product code");
                sb.AppendLine("\t\t# Replace with actual product codes");
                sb.AppendLine("\t\t$msiCodes = @(");
                sb.AppendLine("\t\t\t'{12345678-1234-1234-1234-123456789012}',");
                sb.AppendLine("\t\t\t'{87654321-4321-4321-4321-210987654321}'");
                sb.AppendLine("\t\t)");
                sb.AppendLine("\t\tForEach ($code in $msiCodes) {");
                sb.AppendLine("\t\t\tWrite-Log -Message \"Removing product code: $code\" -Severity 1");
                sb.AppendLine("\t\t\tRemove-MSIApplications -ProductCode $code");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Remove Previous Versions by Name
            if (options.RemovePreviousVersions)
            {
                sb.AppendLine("\t\t## Remove previous versions by application name");
                sb.AppendLine($"\t\t$oldVersions = Get-InstalledApplication -Name '{appInfo.Name}' -WildCard");
                sb.AppendLine("\t\tForEach ($oldApp in $oldVersions) {");
                sb.AppendLine("\t\t\tWrite-Log -Message \"Removing: $($oldApp.DisplayName) $($oldApp.DisplayVersion)\" -Severity 1");
                sb.AppendLine("\t\t\tIf ($oldApp.UninstallString) {");
                sb.AppendLine("\t\t\t\tExecute-Process -Path $oldApp.UninstallString -Parameters '/S'");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Check .NET Framework
            if (options.CheckDotNet)
            {
                sb.AppendLine("\t\t## Check .NET Framework version (minimum 4.7.2)");
                sb.AppendLine("\t\t$dotNetVersion = Get-RegistryKey -Key 'HKLM\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full' -Value 'Release'");
                sb.AppendLine("\t\tIf ($dotNetVersion -lt 461808) {");
                sb.AppendLine("\t\t\tWrite-Log -Message '.NET Framework 4.7.2 or higher is required.' -Severity 2");
                sb.AppendLine("\t\t\tIf (Test-Path \"$dirFiles\\NDP472-KB4054530-x86-x64-AllOS-ENU.exe\") {");
                sb.AppendLine("\t\t\t\tShow-InstallationProgress -StatusMessage 'Installing .NET Framework 4.7.2...'");
                sb.AppendLine("\t\t\t\tExecute-Process -Path \"$dirFiles\\NDP472-KB4054530-x86-x64-AllOS-ENU.exe\" -Parameters '/quiet /norestart'");
                sb.AppendLine("\t\t\t} Else {");
                sb.AppendLine("\t\t\t\tShow-InstallationPrompt -Message '.NET Framework 4.7.2 or higher is required.' -ButtonRightText 'Exit' -CloseApps $false");
                sb.AppendLine("\t\t\t\tExit-Script -ExitCode 1618");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Import Certificates
            if (options.ImportCertificates)
            {
                sb.AppendLine("\t\t## Import security certificates to Trusted Publishers");
                sb.AppendLine("\t\t$certFiles = Get-ChildItem -Path \"$dirFiles\" -Filter '*.cer' -Recurse");
                sb.AppendLine("\t\tForEach ($cert in $certFiles) {");
                sb.AppendLine("\t\t\tWrite-Log -Message \"Installing certificate: $($cert.Name)\" -Severity 1");
                sb.AppendLine("\t\t\tExecute-Process -Path 'certutil.exe' -Parameters \"-addstore `\"TrustedPublisher`\" `\"$($cert.FullName)`\"\" -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Check Visual C++ Redistributables
            if (options.CheckVCRedist)
            {
                sb.AppendLine("\t\t## Check and install Visual C++ Redistributables");
                sb.AppendLine("\t\t$vcRedist2019x64 = Get-InstalledApplication -Name 'Microsoft Visual C++ 2015-2022 Redistributable (x64)'");
                sb.AppendLine("\t\tIf (-not $vcRedist2019x64) {");
                sb.AppendLine("\t\t\tWrite-Log -Message 'Installing Visual C++ 2015-2022 Redistributable x64' -Severity 1");
                sb.AppendLine("\t\t\tIf (Test-Path \"$dirFiles\\vc_redist.x64.exe\") {");
                sb.AppendLine("\t\t\t\tExecute-Process -Path \"$dirFiles\\vc_redist.x64.exe\" -Parameters '/quiet /norestart' -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Import Drivers
            if (options.ImportDrivers)
            {
                sb.AppendLine("\t\t## Import device drivers");
                sb.AppendLine("\t\t$driverPath = \"$dirFiles\\Drivers\"");
                sb.AppendLine("\t\tIf (Test-Path $driverPath) {");
                sb.AppendLine("\t\t\t$driverFiles = Get-ChildItem -Path $driverPath -Filter '*.inf' -Recurse");
                sb.AppendLine("\t\t\tForEach ($driver in $driverFiles) {");
                sb.AppendLine("\t\t\t\tWrite-Log -Message \"Installing driver: $($driver.Name)\" -Severity 1");
                sb.AppendLine("\t\t\t\tExecute-Process -Path 'pnputil.exe' -Parameters \"/add-driver `\"$($driver.FullName)`\" /install\" -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateInstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();
            

            // Update progress if shown
            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t## Update installation progress");
                sb.AppendLine($"\t\tShow-InstallationProgress -StatusMessage 'Installing {appInfo.Name} {appInfo.Version}...'");
                sb.AppendLine();
            }

            string installerFile = "";
            if (!string.IsNullOrEmpty(appInfo.SourcesPath) && !appInfo.SourcesPath.Contains(";"))
            {
                // Use the actual source file name
                installerFile = Path.GetFileName(appInfo.SourcesPath);
                Debug.WriteLine($"Using actual installer file: {installerFile}");
            }
            else
            {
                // Fallback to generic names
                installerFile = options.PackageType == "MSI" ?
                    $"{appInfo.Name}.msi" :
                    "setup.exe";
                Debug.WriteLine($"Using generic installer file: {installerFile}");
            }

            // Main installation
            if (options.PackageType == "MSI")
            {
                var msiParams = new List<string>();
                if (options.SilentInstall) msiParams.Add("/qn");
                

                if (options.SuppressRestart) msiParams.Add("REBOOT=ReallySuppress");
                if (options.AllUsersInstall) msiParams.Add("ALLUSERS=1");

                string transforms = "";
                if (File.Exists(Path.Combine("$dirFiles", $"{appInfo.Name}.mst")))
                {
                    transforms = $"-Transform '$dirFiles\\{appInfo.Name}.mst'";
                }

                if (options.VerboseLogging)
                {
                    sb.AppendLine("\t\t## MSI Installation with verbose logging");
                    sb.AppendLine($"\t\t$logFile = \"$configToolkitLogDir\\{appInfo.Name}_{appInfo.Version}_Install.log\"");
                    sb.AppendLine($"\t\tExecute-MSI -Action 'Install' -Path '$dirFiles\\{installerFile}' {transforms} -Parameters '{string.Join(" ", msiParams)} /l*v \"$logFile\"'");
                }
                else
                {
                    sb.AppendLine("\t\t## MSI Installation");
                    sb.AppendLine($"\t\tExecute-MSI -Action 'Install' -Path '$dirFiles\\{installerFile}' {transforms} -Parameters '{string.Join(" ", msiParams)}'");
                }
            }
            else // EXE
            {
                var exeParams = new List<string>();
                if (options.SilentInstall)
                {
                    exeParams.Add("/S /v\"/qn\""); // Common silent switches
                }
                if (options.SuppressRestart) exeParams.Add("/norestart");
                if (options.AllUsersInstall) exeParams.Add("ALLUSERS=1");

                if (options.VerboseLogging)
                {
                    sb.AppendLine("\t\t## EXE Installation with logging");
                    sb.AppendLine($"\t\t$logFile = \"$configToolkitLogDir\\{appInfo.Name}_{appInfo.Version}_Install.log\"");
                    sb.AppendLine($"\t\tExecute-Process -Path \"$dirFiles\\{installerFile}\" -Parameters '{string.Join(" ", exeParams)} /log \"$logFile\"' -WindowStyle 'Hidden' -PassThru");
                }
                else
                {
                    sb.AppendLine("\t\t## EXE Installation");
                    sb.AppendLine($"\t\tExecute-Process -Path \"$dirFiles\\{installerFile}\" -Parameters '{string.Join(" ", exeParams)}' -WindowStyle 'Hidden'");
                }
            }
            sb.AppendLine();

            // Wait for Process Completion
            if (options.WaitForProcessCompletion)
            {
                sb.AppendLine("\t\t## Wait for installation process to complete");
                sb.AppendLine("\t\t$processNames = @('setup', 'install', 'msiexec')");
                sb.AppendLine("\t\tForEach ($procName in $processNames) {");
                sb.AppendLine("\t\t\tWhile (Get-Process -Name $procName -ErrorAction SilentlyContinue) {");
                sb.AppendLine("\t\t\t\tWrite-Log -Message \"Waiting for $procName process to complete...\" -Severity 1");
                sb.AppendLine("\t\t\t\tStart-Sleep -Seconds 5");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GeneratePostInstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();
            

            // Import Registry Files
            if (options.ImportRegFile)
            {
                sb.AppendLine("\t\t## Import registry settings from .reg files");
                sb.AppendLine("\t\t$regFiles = Get-ChildItem -Path \"$dirFiles\" -Filter '*.reg' -Recurse");
                sb.AppendLine("\t\tForEach ($regFile in $regFiles) {");
                sb.AppendLine("\t\t\tWrite-Log -Message \"Importing registry file: $($regFile.Name)\" -Severity 1");
                sb.AppendLine("\t\t\tExecute-Process -Path 'reg.exe' -Parameters \"import `\"$($regFile.FullName)`\"\" -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Register DLLs
            if (options.RegisterDLLs)
            {
                sb.AppendLine("\t\t## Register DLL modules and COM components");
                sb.AppendLine("\t\t$dllFiles = Get-ChildItem -Path \"$dirFiles\" -Filter '*.dll' -Recurse");
                sb.AppendLine("\t\tForEach ($dll in $dllFiles) {");
                sb.AppendLine("\t\t\tTry {");
                sb.AppendLine("\t\t\t\tWrite-Log -Message \"Registering DLL: $($dll.Name)\" -Severity 1");
                sb.AppendLine("\t\t\t\tRegister-DLL -FilePath $dll.FullName");
                sb.AppendLine("\t\t\t} Catch {");
                sb.AppendLine("\t\t\t\tWrite-Log -Message \"Failed to register $($dll.Name): $_\" -Severity 2");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Copy Configuration Files
            if (options.CopyConfigFiles)
            {
                sb.AppendLine("\t\t## Copy configuration files to ProgramData");
                sb.AppendLine($"\t\t$configPath = \"$envProgramData\\{appInfo.Manufacturer}\\{appInfo.Name}\"");
                sb.AppendLine("\t\tIf (-not (Test-Path $configPath)) { New-Folder -Path $configPath }");
                sb.AppendLine("\t\tIf (Test-Path \"$dirFiles\\Config\") {");
                sb.AppendLine("\t\t\tCopy-File -Path \"$dirFiles\\Config\\*\" -Destination $configPath -Recurse");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Copy to All Users
            if (options.CopyToAllUsers)
            {
                sb.AppendLine("\t\t## Copy application settings to all user profiles");
                sb.AppendLine("\t\t$ProfilePaths = Get-UserProfiles | Select-Object -ExpandProperty 'ProfilePath'");
                sb.AppendLine("\t\tForEach ($ProfilePath in $ProfilePaths) {");
                sb.AppendLine($"\t\t\t$UserAppData = \"$ProfilePath\\AppData\\Roaming\\{appInfo.Manufacturer}\\{appInfo.Name}\"");
                sb.AppendLine("\t\t\tIf (-not (Test-Path $UserAppData)) { New-Folder -Path $UserAppData }");
                sb.AppendLine("\t\t\tIf (Test-Path \"$dirFiles\\UserSettings\") {");
                sb.AppendLine("\t\t\t\tCopy-File -Path \"$dirFiles\\UserSettings\\*\" -Destination $UserAppData -Recurse");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Set HKCU for All Users
            if (options.SetHKCUAllUsers)
            {
                sb.AppendLine("\t\t## Set HKCU registry settings for all users");
                sb.AppendLine("\t\t[scriptblock]$HKCURegistrySettings = {");
                sb.AppendLine($"\t\t\tSet-RegistryKey -Key 'HKCU\\Software\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'Version' -Value '{appInfo.Version}' -Type String -SID $UserProfile.SID");
                sb.AppendLine($"\t\t\tSet-RegistryKey -Key 'HKCU\\Software\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'FirstRun' -Value '0' -Type DWord -SID $UserProfile.SID");
                sb.AppendLine($"\t\t\tSet-RegistryKey -Key 'HKCU\\Software\\{appInfo.Manufacturer}\\{appInfo.Name}\\Settings' -Name 'AutoUpdate' -Value '0' -Type DWord -SID $UserProfile.SID");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tInvoke-HKCURegistrySettingsForAllUsers -RegistrySettings $HKCURegistrySettings");
                sb.AppendLine();
            }

            // Set Custom Registry
            if (options.SetCustomRegistry)
            {
                sb.AppendLine("\t\t## Set custom registry keys for application");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'Version' -Value '{appInfo.Version}' -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'InstallDate' -Value (Get-Date -Format 'yyyy-MM-dd') -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'InstallPath' -Value \"$envProgramFiles\\{appInfo.Name}\" -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'InstallUser' -Value $env:USERNAME -Type String");
                sb.AppendLine();
            }

            // Modify File Permissions
            if (options.ModifyFilePermissions)
            {
                sb.AppendLine("\t\t## Modify file permissions (ACL) for application folder");
                sb.AppendLine($"\t\t$appPath = \"$envProgramFiles\\{appInfo.Name}\"");
                sb.AppendLine("\t\tIf (Test-Path $appPath) {");
                sb.AppendLine("\t\t\t$acl = Get-Acl $appPath");
                sb.AppendLine("\t\t\t$permission = 'Users','Modify','ContainerInherit,ObjectInherit','None','Allow'");
                sb.AppendLine("\t\t\t$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission");
                sb.AppendLine("\t\t\t$acl.SetAccessRule($accessRule)");
                sb.AppendLine("\t\t\tSet-Acl $appPath $acl");
                sb.AppendLine("\t\t\tWrite-Log -Message 'Modified folder permissions for Users group' -Severity 1");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Execute VBScript
            if (options.ExecuteVBScript)
            {
                sb.AppendLine("\t\t## Execute VBScript for legacy configuration");
                sb.AppendLine("\t\t$vbsFiles = Get-ChildItem -Path \"$dirFiles\" -Filter '*.vbs' -Recurse");
                sb.AppendLine("\t\tForEach ($vbs in $vbsFiles) {");
                sb.AppendLine("\t\t\tWrite-Log -Message \"Executing VBScript: $($vbs.Name)\" -Severity 1");
                sb.AppendLine("\t\t\tExecute-Process -Path 'cscript.exe' -Parameters \"//NoLogo `\"$($vbs.FullName)`\"\" -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Create Active Setup
            if (options.CreateActiveSetup)
            {
                sb.AppendLine("\t\t## Create Active Setup for per-user configuration");
                sb.AppendLine($"\t\t$activeSetupKey = \"HKLM:\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{appInfo.Name}\"");
                sb.AppendLine($"\t\tSet-RegistryKey -Key $activeSetupKey -Name '' -Value '{appInfo.Name} {appInfo.Version}' -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key $activeSetupKey -Name 'Version' -Value '{appInfo.Version}' -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key $activeSetupKey -Name 'StubPath' -Value \"powershell.exe -ExecutionPolicy Bypass -File `\"$envProgramFiles\\{appInfo.Name}\\UserSetup.ps1`\"\" -Type String");
                sb.AppendLine("\t\tWrite-Log -Message 'Created Active Setup entry' -Severity 1");
                sb.AppendLine();
            }

            // Add to PATH
            if (options.AddToPath)
            {
                sb.AppendLine("\t\t## Add application to system PATH environment variable");
                sb.AppendLine($"\t\t$appBinPath = \"$envProgramFiles\\{appInfo.Name}\\bin\"");
                sb.AppendLine("\t\tIf (Test-Path $appBinPath) {");
                sb.AppendLine("\t\t\t$currentPath = [Environment]::GetEnvironmentVariable('Path', 'Machine')");
                sb.AppendLine("\t\t\tIf ($currentPath -notlike \"*$appBinPath*\") {");
                sb.AppendLine("\t\t\t\t[Environment]::SetEnvironmentVariable('Path', \"$currentPath;$appBinPath\", 'Machine')");
                sb.AppendLine("\t\t\t\tWrite-Log -Message 'Added application to system PATH' -Severity 1");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Create Shortcuts
            if (options.DesktopShortcut || options.StartMenuEntry)
            {
                sb.AppendLine("\t\t## Create application shortcuts");
                string targetPath = $"$envProgramFiles\\{appInfo.Name}\\{appInfo.Name}.exe";

                if (options.DesktopShortcut)
                {
                    sb.AppendLine($"\t\t## Create desktop shortcut for all users");
                    sb.AppendLine($"\t\tNew-Shortcut -Path \"$envCommonDesktop\\{appInfo.Name}.lnk\" `");
                    sb.AppendLine($"\t\t\t-TargetPath \"{targetPath}\" `");
                    sb.AppendLine($"\t\t\t-IconLocation \"{targetPath}\" `");
                    sb.AppendLine($"\t\t\t-Description '{appInfo.Name} version {appInfo.Version}' `");
                    sb.AppendLine($"\t\t\t-WorkingDirectory \"$envProgramFiles\\{appInfo.Name}\"");
                }

                if (options.StartMenuEntry)
                {
                    sb.AppendLine($"\t\t## Create Start Menu entry");
                    sb.AppendLine($"\t\t$startMenuPath = \"$envCommonStartMenuPrograms\\{appInfo.Manufacturer}\"");
                    sb.AppendLine("\t\tIf (-not (Test-Path $startMenuPath)) { New-Folder -Path $startMenuPath }");
                    sb.AppendLine($"\t\tNew-Shortcut -Path \"$startMenuPath\\{appInfo.Name}.lnk\" `");
                    sb.AppendLine($"\t\t\t-TargetPath \"{targetPath}\" `");
                    sb.AppendLine($"\t\t\t-IconLocation \"{targetPath}\" `");
                    sb.AppendLine($"\t\t\t-Description '{appInfo.Name} version {appInfo.Version}' `");
                    sb.AppendLine($"\t\t\t-WorkingDirectory \"$envProgramFiles\\{appInfo.Name}\"");
                }

                sb.AppendLine();
            }

            // Create Install Marker
            if (options.CreateInstallMarker)
            {
                sb.AppendLine("\t\t## Create installation marker for detection");
                sb.AppendLine("\t\tSet-RegistryKey -Key \"$configToolkitRegPath\\$appDeployToolkitName\\InstalledApplications\\$installName\" `");
                sb.AppendLine($"\t\t\t-Name 'DisplayName' -Value '{appInfo.Manufacturer} {appInfo.Name}' -Type String");
                sb.AppendLine("\t\tSet-RegistryKey -Key \"$configToolkitRegPath\\$appDeployToolkitName\\InstalledApplications\\$installName\" `");
                sb.AppendLine($"\t\t\t-Name 'DisplayVersion' -Value '{appInfo.Version}' -Type String");
                sb.AppendLine("\t\tSet-RegistryKey -Key \"$configToolkitRegPath\\$appDeployToolkitName\\InstalledApplications\\$installName\" `");
                sb.AppendLine("\t\t\t-Name 'InstallDate' -Value (Get-Date -Format 'yyyyMMdd') -Type String");
                sb.AppendLine();
            }

            // Close Progress if shown
            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t## Close progress dialog");
                sb.AppendLine("\t\tClose-InstallationProgress");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateUninstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();
           

            // Show Progress
            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t## Show uninstallation progress");
                sb.AppendLine($"\t\tShow-InstallationProgress -StatusMessage 'Uninstalling {appInfo.Name}...'");
                sb.AppendLine();
            }

            // Close Running Apps
            if (options.CloseRunningApps)
            {
                sb.AppendLine("\t\t## Close running applications before uninstall");
                sb.AppendLine("\t\tShow-InstallationWelcome -CloseApps 'notepad,excel,winword,outlook' -CloseAppsCountdown 300");
                sb.AppendLine();
            }

            // Unregister DLLs
            if (options.UnregisterDLLs)
            {
                sb.AppendLine("\t\t## Unregister DLL modules before uninstall");
                sb.AppendLine($"\t\t$appPath = \"$envProgramFiles\\{appInfo.Name}\"");
                sb.AppendLine("\t\tIf (Test-Path $appPath) {");
                sb.AppendLine("\t\t\t$dllFiles = Get-ChildItem -Path $appPath -Filter '*.dll' -Recurse");
                sb.AppendLine("\t\t\tForEach ($dll in $dllFiles) {");
                sb.AppendLine("\t\t\t\tUnregister-DLL -FilePath $dll.FullName -ContinueOnError $true");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Main uninstall logic
            if (options.PackageType == "MSI")
            {
                sb.AppendLine("\t\t## Uninstall MSI application");
                var msiParams = options.SilentInstall ? "/qn" : "/qb-!";
                if (options.SuppressRestart) msiParams += " REBOOT=ReallySuppress";

                sb.AppendLine($"\t\tRemove-MSIApplications -Name '{appInfo.Name}' -Parameters '{msiParams}'");
            }
            else
            {
                sb.AppendLine("\t\t## Uninstall EXE application");
                sb.AppendLine($"\t\t$uninstallExe = Get-ChildItem -Path \"$envProgramFiles\\{appInfo.Name}\" -Filter 'unins*.exe' -Recurse | Select-Object -First 1");
                sb.AppendLine("\t\tIf ($uninstallExe) {");
                sb.AppendLine("\t\t\tExecute-Process -Path $uninstallExe.FullName -Parameters '/S' -WindowStyle 'Hidden'");
                sb.AppendLine("\t\t} Else {");
                sb.AppendLine($"\t\t\tRemove-MSIApplications -Name '{appInfo.Name}'");
                sb.AppendLine("\t\t}");
            }
            sb.AppendLine();

            // Remove Specific Files
            if (options.RemoveSpecificFiles)
            {
                sb.AppendLine("\t\t## Remove specific files and temporary data");
                sb.AppendLine($"\t\tRemove-File -Path \"$envTemp\\{appInfo.Name}*\" -Recurse");
                sb.AppendLine($"\t\tRemove-File -Path \"$envProgramData\\{appInfo.Manufacturer}\\{appInfo.Name}\\*.log\" -Recurse");
                sb.AppendLine($"\t\tRemove-File -Path \"$envProgramData\\{appInfo.Manufacturer}\\{appInfo.Name}\\*.tmp\" -Recurse");
                sb.AppendLine();
            }

            // Remove Shortcuts
            if (options.DesktopShortcut || options.StartMenuEntry)
            {
                sb.AppendLine("\t\t## Remove application shortcuts");
                if (options.DesktopShortcut)
                {
                    sb.AppendLine($"\t\tRemove-File -Path \"$envCommonDesktop\\{appInfo.Name}.lnk\"");
                }
                if (options.StartMenuEntry)
                {
                    sb.AppendLine($"\t\tRemove-Folder -Path \"$envCommonStartMenuPrograms\\{appInfo.Manufacturer}\" -ContinueOnError $true");
                }
                sb.AppendLine();
            }

            // Clean up Registry
            if (options.SetCustomRegistry || options.CreateInstallMarker)
            {
                sb.AppendLine("\t\t## Clean up registry entries");
                sb.AppendLine($"\t\tRemove-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -ContinueOnError $true");
                sb.AppendLine("\t\tRemove-RegistryKey -Key \"$configToolkitRegPath\\$appDeployToolkitName\\InstalledApplications\\$installName\" -ContinueOnError $true");
                sb.AppendLine();
            }

            // Remove from PATH
            if (options.AddToPath)
            {
                sb.AppendLine("\t\t## Remove application from system PATH");
                sb.AppendLine($"\t\t$appBinPath = \"$envProgramFiles\\{appInfo.Name}\\bin\"");
                sb.AppendLine("\t\t$currentPath = [Environment]::GetEnvironmentVariable('Path', 'Machine')");
                sb.AppendLine("\t\tIf ($currentPath -like \"*$appBinPath*\") {");
                sb.AppendLine("\t\t\t$newPath = ($currentPath -split ';' | Where-Object { $_ -ne $appBinPath }) -join ';'");
                sb.AppendLine("\t\t\t[Environment]::SetEnvironmentVariable('Path', $newPath, 'Machine')");
                sb.AppendLine("\t\t\tWrite-Log -Message 'Removed application from system PATH' -Severity 1");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Remove Active Setup
            if (options.CreateActiveSetup)
            {
                sb.AppendLine("\t\t## Remove Active Setup entry");
                sb.AppendLine($"\t\tRemove-RegistryKey -Key \"HKLM:\\SOFTWARE\\Microsoft\\Active Setup\\Installed Components\\{appInfo.Name}\" -ContinueOnError $true");
                sb.AppendLine();
            }

            // Clean up User Profiles
            if (options.CopyToAllUsers || options.SetHKCUAllUsers)
            {
                sb.AppendLine("\t\t## Clean up user profile data");
                sb.AppendLine("\t\t$ProfilePaths = Get-UserProfiles | Select-Object -ExpandProperty 'ProfilePath'");
                sb.AppendLine("\t\tForEach ($ProfilePath in $ProfilePaths) {");

                if (options.CopyToAllUsers)
                {
                    sb.AppendLine($"\t\t\tRemove-Folder -Path \"$ProfilePath\\AppData\\Roaming\\{appInfo.Manufacturer}\\{appInfo.Name}\" -ContinueOnError $true");
                }

                if (options.SetHKCUAllUsers)
                {
                    sb.AppendLine($"\t\t\tRemove-RegistryKey -Key \"HKCU\\Software\\{appInfo.Manufacturer}\\{appInfo.Name}\" -ContinueOnError $true -SID (Get-UserSID $ProfilePath)");
                }

                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Remove Empty Folders
            if (options.RemoveEmptyFolders)
            {
                sb.AppendLine("\t\t## Remove empty folders after uninstall");
                sb.AppendLine("\t\t$folders = @(");
                sb.AppendLine($"\t\t\t\"$envProgramFiles\\{appInfo.Name}\",");
                sb.AppendLine($"\t\t\t\"$envProgramData\\{appInfo.Manufacturer}\\{appInfo.Name}\",");
                sb.AppendLine($"\t\t\t\"$envProgramData\\{appInfo.Manufacturer}\"");
                sb.AppendLine("\t\t)");
                sb.AppendLine("\t\tForEach ($folder in $folders) {");
                sb.AppendLine("\t\t\tIf ((Test-Path $folder) -and ((Get-ChildItem $folder -Recurse -Force | Measure-Object).Count -eq 0)) {");
                sb.AppendLine("\t\t\t\tRemove-Folder -Path $folder");
                sb.AppendLine("\t\t\t\tWrite-Log -Message \"Removed empty folder: $folder\" -Severity 1");
                sb.AppendLine("\t\t\t}");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Close Progress if shown
            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t## Close progress dialog");
                sb.AppendLine("\t\tClose-InstallationProgress");
                sb.AppendLine();
            }

            return sb.ToString();
        }

      
    }
}