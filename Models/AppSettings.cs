// Models/AppSettings.cs
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

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

        // Properties
        public bool FirstRun { get; set; } = true;
        public string AppVersion { get; set; } = "1.0.0";
        public AuthenticationSettings Authentication { get; set; } = new();
        public PathSettings Paths { get; set; } = new();
        public UploadSettings Upload { get; set; } = new();
        public UISettings UI { get; set; } = new();
        public LoggingSettings Logging { get; set; } = new();

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}