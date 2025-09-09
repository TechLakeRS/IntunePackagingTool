using System.IO;
using System.Threading.Tasks;
using System;
using System.Linq;
using System.Text;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class PSADTOptions
    {
        // Installation Options
        public bool SilentInstall { get; set; }
        public bool SuppressRestart { get; set; }
        public bool AllUsersInstall { get; set; }
        public bool VerboseLogging { get; set; }

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

        // Shortcuts & Cleanup
        public bool DesktopShortcut { get; set; }
        public bool StartMenuEntry { get; set; }
        public bool RemovePreviousVersions { get; set; }
        public bool CreateInstallMarker { get; set; }

        // Package Info
        public string PackageType { get; set; } = null!;     // "MSI" or "EXE"
    }

    public class PSADTGenerator
    {
        private readonly string _baseOutputPath = @"\\nbb.local\sys\SCCMData\IntuneApplications";
        private readonly string _templatePath = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\20250811\Application";
        public string BaseOutputPath => _baseOutputPath;
        public string TemplatePath => _templatePath;

        public async Task<string> CreatePackageAsync(ApplicationInfo appInfo, PSADTOptions? psadtOptions =  null)
        {
            try
            {
                // Create folder structure: Manufacturer_AppName\Version\ (replace spaces with underscores)
                var cleanManufacturer = appInfo.Manufacturer.Replace(" ", "_");
                var cleanAppName = appInfo.Name.Replace(" ", "_");
                var appFolderName = $"{cleanManufacturer}_{cleanAppName}";
                var appBasePath = Path.Combine(_baseOutputPath, appFolderName);
                var packagePath = Path.Combine(appBasePath, appInfo.Version);

                // Check if Manufacturer_Appname already exists
                if (!Directory.Exists(appBasePath))
                {
                    Directory.CreateDirectory(appBasePath);
                    Console.WriteLine($"Created new application folder: {appBasePath}");
                }
                else
                {
                    Console.WriteLine($"Application folder already exists: {appBasePath}");
                }

                // Check if version folder already exists
                if (Directory.Exists(packagePath))
                {
                    throw new InvalidOperationException($"Version {appInfo.Version} already exists for {cleanManufacturer}_{cleanAppName}");
                }

                // Create version folder
                Directory.CreateDirectory(packagePath);
                Console.WriteLine($"Created version folder: {packagePath}");

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
                    // Check if it's multiple files (semicolon separated), single file, or directory
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
                        Console.WriteLine($"Warning: Source path not found or invalid: {appInfo.SourcesPath}");
                    }
                }

                // Modify the Deploy-Application.ps1 in the Application folder with metadata AND cheatsheet functions
                await ModifyPSADTScriptAsync(packagePath, appInfo, psadtOptions = null!);

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
                    // Copy all files and folders from template to Application folder
                    CopyDirectory(_templatePath, destinationAppFolder, true);
                });

                Console.WriteLine($"Copied template files from {_templatePath} to {destinationAppFolder}");
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

                // Create Application\Files folder if it doesn't exist
                Directory.CreateDirectory(applicationFilesPath);

                await Task.Run(() =>
                {
                    // Check if sourcePath contains multiple files (semicolon separated) or single file/directory
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
                                Console.WriteLine($"Copied file: {fileName}");
                            }
                        }
                    }
                    else if (File.Exists(sourcePath))
                    {
                        // Handle single file
                        var fileName = Path.GetFileName(sourcePath);
                        var destinationFile = Path.Combine(applicationFilesPath, fileName);
                        File.Copy(sourcePath, destinationFile, true);
                        Console.WriteLine($"Copied file: {fileName}");
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        // Handle directory (existing functionality)
                        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
                        {
                            var relativePath = Path.GetRelativePath(sourcePath, file);
                            var destinationFile = Path.Combine(applicationFilesPath, relativePath);

                            var destinationDir = Path.GetDirectoryName(destinationFile);
                            if (!string.IsNullOrEmpty(destinationDir))
                            {
                                Directory.CreateDirectory(destinationDir);
                            }
                            File.Copy(file, destinationFile, true);
                        }
                        Console.WriteLine($"Copied all files from directory: {sourcePath}");
                    }
                    else
                    {
                        throw new FileNotFoundException($"Source path not found: {sourcePath}");
                    }
                });

                Console.WriteLine($"Copied source files to {applicationFilesPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error copying source files: {ex.Message}", ex);
            }
        }

        private void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            // Get the subdirectory info
            var dir = new DirectoryInfo(sourceDir);

            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            // Cache directories before we start copying
            DirectoryInfo[] dirs = dir.GetDirectories();

            // Create the destination directory
            Directory.CreateDirectory(destinationDir);

            // Get the files in the directory and copy them to the new location
            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            // If we are copying subdirectories, copy them and their contents to the new location
            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }

        private async Task ModifyPSADTScriptAsync(string packagePath, ApplicationInfo appInfo, PSADTOptions psadtOptions)
        {
            try
            {
                var scriptPath = Path.Combine(packagePath, "Application", "Deploy-Application.ps1");

                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException($"Deploy-Application.ps1 not found in copied template: {scriptPath}");
                }

                // Read the existing script
                var scriptContent = await File.ReadAllTextAsync(scriptPath);

                // Update the metadata variables in the script
                scriptContent = UpdateScriptMetadata(scriptContent, appInfo);

                // If PSADT options are provided, inject cheatsheet functions
                if (psadtOptions != null)
                {
                    scriptContent = InjectPSADTCheatsheetFunctions(scriptContent, appInfo, psadtOptions);
                }

                // Write the modified script back
                await File.WriteAllTextAsync(scriptPath, scriptContent);

                Console.WriteLine($"Modified Deploy-Application.ps1 with metadata and cheatsheet functions: {scriptPath}");
            }
            catch (Exception ex)
            {
                throw new Exception($"Error modifying PSADT script: {ex.Message}", ex);
            }
        }

        private string UpdateScriptMetadata(string scriptContent, ApplicationInfo appInfo)
        {
            string currentUser = Environment.UserName;
            // Update the variable declarations section with user's metadata
            var lines = scriptContent.Split('\n').ToList();

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();

                // Update specific variables (handle both [string] and [String], with or without values)
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
                    lines[i] = $"\t[string]$appScriptDate = '{DateTime.Now:yyyy/MM/dd}'";
                }
                else if (line.Contains("$appScriptAuthor") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$appScriptAuthor = '{currentUser}'";
                }
                // Add ServiceNow SRI if it doesn't exist, or update if it does
                else if (line.Contains("$ServiceNowSRI") && (line.StartsWith("[string]") || line.StartsWith("[String]")))
                {
                    lines[i] = $"\t[string]$ServiceNowSRI = '{appInfo.ServiceNowSRI}'";
                }
                // If we're after the appScriptAuthor line and ServiceNowSRI doesn't exist, add it
                else if (line.Contains("$appScriptAuthor") && !scriptContent.Contains("$ServiceNowSRI"))
                {
                    // Insert ServiceNow SRI after appScriptAuthor
                    lines.Insert(i + 1, $"\t[string]$ServiceNowSRI = '{appInfo.ServiceNowSRI}'");
                    break; // Exit loop to avoid infinite insertion
                }
            }

            return string.Join('\n', lines);
        }

        private string InjectPSADTCheatsheetFunctions(string scriptContent, ApplicationInfo appInfo, PSADTOptions options)
        {
            var lines = scriptContent.Split('\n').ToList();

            // Find insertion points for different sections
            int preInstallIndex = FindSectionIndex(lines, "Pre-Installation");
            int installIndex = FindSectionIndex(lines, "Installation");
            int postInstallIndex = FindSectionIndex(lines, "Post-Installation");
            int uninstallIndex = FindSectionIndex(lines, "Uninstallation");

            // Insert PRE-INSTALLATION functions
            if (preInstallIndex > 0)
            {
                var preInstallCode = GeneratePreInstallationCode(options);
                if (!string.IsNullOrWhiteSpace(preInstallCode))
                {
                    InsertCodeBlock(lines, preInstallIndex, preInstallCode);
                    // Update indices since we inserted lines
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
                }
            }

            return string.Join('\n', lines);
        }

        private int FindSectionIndex(List<string> lines, string sectionName)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                // Look for the placeholder comment, then insert AFTER it
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

        private string GeneratePreInstallationCode(PSADTOptions options)
        {
            var sb = new StringBuilder();

            // User Interaction
            if (options.CloseRunningApps || options.AllowUserDeferrals || options.CheckDiskSpace)
            {
                var welcomeParams = new List<string>();

                if (options.CloseRunningApps)
                    welcomeParams.Add("-CloseApps 'notepad,excel,winword'");

                if (options.AllowUserDeferrals)
                {
                    welcomeParams.Add("-AllowDefer");
                    welcomeParams.Add("-DeferTimes 3");
                }

                if (options.CheckDiskSpace)
                {
                    welcomeParams.Add("-CheckDiskSpace");
                    welcomeParams.Add("-RequiredDiskSpace 500");
                }

                sb.AppendLine($"\t\t# Show welcome dialog with user interaction options");
                sb.AppendLine($"\t\tShow-InstallationWelcome {string.Join(" ", welcomeParams)}");
                sb.AppendLine();
            }

            // Prerequisites
            if (options.CheckDotNet)
            {
                sb.AppendLine("\t\t# Check .NET Framework version (from cheatsheet)");
                sb.AppendLine("\t\t$version_we_require = [version]\"4.5.2\"");
                sb.AppendLine("\t\tif((Get-RegistryKey \"HKLM:\\SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\" -Value Version) -lt $version_we_require) {");
                sb.AppendLine("\t\t\tWrite-Log \"Installing .NET Framework 4.5.2...\"");
                sb.AppendLine("\t\t\tExecute-Process -Path \"$dirFiles\\NDP452-KB2901907-x86-x64-AllOS-ENU.exe\" -Parameters \"/q /norestart\"");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            if (options.ImportCertificates)
            {
                sb.AppendLine("\t\t# Import certificates to Trusted Publishers store (from cheatsheet)");
                sb.AppendLine("\t\tExecute-Process -Path \"certutil.exe\" -Parameters \"-f -addstore -enterprise TrustedPublisher `\"$dirFiles\\cert.cer`\"\"");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateInstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();

            if (options.ShowProgress)
            {
                sb.AppendLine("\t\t# Show installation progress");
                sb.AppendLine($"\t\tShow-InstallationProgress -StatusMessage 'Installing {appInfo.Name}...'");
                sb.AppendLine();
            }

            // Main installation based on package type
            if (options.PackageType == "MSI")
            {
                var msiParams = new List<string>();

                if (options.SilentInstall) msiParams.Add("/quiet");
                if (options.SuppressRestart) msiParams.Add("/norestart");
                if (options.AllUsersInstall) msiParams.Add("ALLUSERS=1");

                sb.AppendLine("\t\t# MSI Installation with selected options");
                sb.AppendLine($"\t\tExecute-MSI -Action 'Install' -Path '$dirFiles\\YourApp.msi' -Parameters '{string.Join(" ", msiParams)}'");

                if (options.SuppressRestart)
                {
                    sb.AppendLine("\t\t\t-AddParameters 'REBOOT=ReallySuppress'");
                }
            }
            else if (options.PackageType == "EXE")
            {
                var exeParams = new List<string>();

                if (options.SilentInstall) exeParams.Add("/S");
                if (options.SuppressRestart) exeParams.Add("/norestart");
                if (options.AllUsersInstall) exeParams.Add("ALLUSERS=1");

                sb.AppendLine("\t\t# EXE Installation with selected options");
                sb.AppendLine($"\t\tExecute-Process -Path '$dirFiles\\setup.exe' -Parameters '{string.Join(" ", exeParams)}' -WindowStyle 'Hidden'");
            }

            sb.AppendLine();
            return sb.ToString();
        }

        private string GeneratePostInstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();

            if (options.RegisterDLLs)
            {
                sb.AppendLine("\t\t# Register DLL modules (from cheatsheet)");
                sb.AppendLine("\t\tRegister-DLL -FilePath \"$dirFiles\\codec.dll\"");
                sb.AppendLine();
            }

            if (options.CopyToAllUsers)
            {
                sb.AppendLine("\t\t# Copy files to all user profiles (from cheatsheet)");
                sb.AppendLine("\t\t$ProfilePaths = Get-UserProfiles | Select-Object -ExpandProperty 'ProfilePath'");
                sb.AppendLine("\t\tForEach ($Profile in $ProfilePaths) {");
                sb.AppendLine("\t\t\tCopy-File -Path \"$dirFiles\\config.ini\" -Destination \"$Profile\\AppData\\Local\\App\\\"");
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            if (options.SetHKCUAllUsers)
            {
                sb.AppendLine("\t\t# Set HKCU registry for all users (from cheatsheet)");
                sb.AppendLine("\t\t[scriptblock]$HKCURegistrySettings = {");
                sb.AppendLine($"\t\t\tSet-RegistryKey -Key 'HKCU\\Software\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'Version' -Value '{appInfo.Version}' -Type String -SID $UserProfile.SID");
                sb.AppendLine("\t\t}");
                sb.AppendLine("\t\tInvoke-HKCURegistrySettingsForAllUsers -RegistrySettings $HKCURegistrySettings");
                sb.AppendLine();
            }

            if (options.SetCustomRegistry)
            {
                sb.AppendLine("\t\t# Set custom registry keys");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'Version' -Value '{appInfo.Version}' -Type String");
                sb.AppendLine($"\t\tSet-RegistryKey -Key 'HKLM:\\SOFTWARE\\{appInfo.Manufacturer}\\{appInfo.Name}' -Name 'InstallDate' -Value (Get-Date) -Type String");
                sb.AppendLine();
            }

            if (options.DesktopShortcut || options.StartMenuEntry)
            {
                sb.AppendLine("\t\t# Create shortcuts");

                if (options.DesktopShortcut)
                {
                    sb.AppendLine($"\t\tNew-Shortcut -Path \"$envCommonDesktop\\{appInfo.Name}.lnk\" -TargetPath \"$envProgramFiles\\{appInfo.Name}\\app.exe\"");
                }

                if (options.StartMenuEntry)
                {
                    sb.AppendLine($"\t\tNew-Shortcut -Path \"$envCommonStartMenuPrograms\\{appInfo.Name}.lnk\" -TargetPath \"$envProgramFiles\\{appInfo.Name}\\app.exe\"");
                }

                sb.AppendLine();
            }

            if (options.CreateInstallMarker)
            {
                sb.AppendLine("\t\t# Create install marker (from cheatsheet)");
                sb.AppendLine("\t\tSet-RegistryKey -Key \"$configToolkitRegPath\\$appDeployToolkitName\\InstallMarkers\\$installName\"");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private string GenerateUninstallationCode(ApplicationInfo appInfo, PSADTOptions options)
        {
            var sb = new StringBuilder();

            if (options.RemovePreviousVersions)
            {
                if (options.PackageType == "MSI")
                {
                    sb.AppendLine("\t\t# Uninstall MSI");
                    sb.AppendLine("\t\tExecute-MSI -Action 'Uninstall' -Path '$dirFiles\\YourApp.msi' -Parameters '/quiet /norestart'");
                }
                else
                {
                    sb.AppendLine("\t\t# Remove previous versions (from cheatsheet)");
                    sb.AppendLine($"\t\tRemove-MSIApplications -Name '{appInfo.Name}'");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public string GetScriptPath(string manufacturer, string appName, string version)
        {
            // Script is now in the Application folder, not the root
            var cleanManufacturer = manufacturer.Replace(" ", "_");
            var cleanAppName = appName.Replace(" ", "_");
            return Path.Combine(_baseOutputPath, $"{cleanManufacturer}_{cleanAppName}", version, "Application", "Deploy-Application.ps1");
        }
    }
}