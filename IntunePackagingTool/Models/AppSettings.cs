// Models/AppSettings.cs
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using IntunePackagingTool.Configuration;

namespace IntunePackagingTool.Models
{
    public class AppSettings : INotifyPropertyChanged
    {
        // === AUTHENTICATION ===
        public class AuthenticationSettings
        {
            public AuthMethod Method { get; set; } = AuthMethod.Interactive;
            public string TenantId { get; set; } = "43f10d24-b9bf-46da-a9c8-15c1b0990ce7"; // Your default value
            public string ClientId { get; set; } = "b47987a1-70b4-415a-9a4e-9775473e382b"; // Your default value
            public string CertificateThumbprint { get; set; } = "CF6DCE7DF3377CA65D9B40F06BF8C2228AC7821F"; // Your default value
        }

        public enum AuthMethod
        {
            Interactive,
            Certificate
        }

        // === PATHS ===
        public class PathSettings
        {
            public string OutputDirectory { get; set; } = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "IntunePackages"
            );

            public string IntuneWinAppUtilPath { get; set; } = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Tools",
                "IntuneWinAppUtil.exe"
            );

            public string PSADTTemplatePath { get; set; } = "";
            public bool UsePSADT { get; set; } = false;
            public string TempDirectory { get; set; } = Path.GetTempPath();
            public bool AutoCleanupTemp { get; set; } = true;
        }

        // === UPLOAD ===
        public class UploadSettings
        {
            public int ChunkSizeMB { get; set; } = 6;
            public int UploadTimeoutMinutes { get; set; } = 30;
            public int MaxRetryAttempts { get; set; } = 3;
            public int RetryDelaySeconds { get; set; } = 10;
        }

        // === UI ===
        public class UISettings
        {
            public string Theme { get; set; } = "Auto";
            public bool DarkMode { get; set; } = false;
            public bool ConfirmBeforeUpload { get; set; } = true;
            public bool RememberWindowSize { get; set; } = true;
            public int WindowWidth { get; set; } = 1200;
            public int WindowHeight { get; set; } = 800;
            public string Language { get; set; } = "en-US";
        }

        // === LOGGING ===
        public class LoggingSettings
        {
            public bool EnableLogging { get; set; } = true;
            public string LogLevel { get; set; } = "Information";
            public string LogDirectory { get; set; } = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntunePackagingTool",
                "Logs"
            );
            public int LogRetentionDays { get; set; } = 7;
        }

        // === CODE SIGNING ===
        public class CodeSigningSettings
        {
            public string CertificateName { get; set; } = "NBB Digital Workplace";
            public string CertificateSubject { get; set; } = "CN=NBB Digital Workplace, OU=National Bank of Belgium (BE), O=EUROPEAN SYSTEM OF CENTRAL BANKS, C=BE";
            public string CertificateThumbprint { get; set; } = "B74452FD21BE6AD24CA9D61BCE156FD75E774716";
            public string TimestampServer { get; set; } = "http://timestamp.digicert.com";
        }

        // === NETWORK PATHS ===
        public class NetworkPathSettings
        {
            public string NetworkRoot { get; set; } = @"\\nbb.local\sys\SCCMData";
            public string IntuneApplications { get; set; } = @"\\nbb.local\sys\SCCMData\IntuneApplications";
            public string Tools { get; set; } = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool";
            public string Scripts { get; set; } = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog";
            public string IntuneWinAppUtil { get; set; } = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\IntuneWinAppUtil.exe";
            public string PSADTTemplate { get; set; } = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\20250811\Application";
            public string HyperVScript { get; set; } = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\Create-CatfileHyperVRS.ps1";
            public string VMScript { get; set; } = @"\\nbb.local\sys\SCCMData\SCRIPTS\Intune\CreateSecurityCatalog\Create_CatVM.ps1";
        }

        // === WDAC HYPER-V ===
        public class WDACHyperVSettings
        {
            public string Host { get; set; } = "PC0030172";
            public string VMName { get; set; } = "WDACVM2";
            public string SnapshotName { get; set; } = "START05";
            public string PsExecPath { get; set; } = @"C:\Windows\System32\PsExec.exe";
        }

        // === GROUP ASSIGNMENT ===
        public class GroupAssignmentSettings
        {
            public List<string> OwnerIds { get; set; } = new()
            {
                "793c1724-5d8b-4ec0-91b0-7c060725a70b",
                "34b301bd-6e41-41b9-b925-a0825a99cdb7",
                "4d570640-6a0c-49e1-bded-0088b4b99507",
                "e8d9d8eb-84dc-4fac-8305-b2b178abbc19"
            };
        }

        // Properties
        public bool FirstRun { get; set; } = true;
        public string AppVersion { get; set; } = "1.0.0";
        public AuthenticationSettings Authentication { get; set; } = new();
        public PathSettings Paths { get; set; } = new();
        public UploadSettings Upload { get; set; } = new();
        public UISettings UI { get; set; } = new();
        public LoggingSettings Logging { get; set; } = new();
        public CodeSigningSettings CodeSigning { get; set; } = new();
        public NetworkPathSettings NetworkPaths { get; set; } = new();
        public WDACHyperVSettings WDACHyperV { get; set; } = new();
        public GroupAssignmentSettings GroupAssignment { get; set; } = new();

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}