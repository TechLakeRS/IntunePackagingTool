// Services/SettingsService.cs
using System;
using System.IO;
using System.Text.Json;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class SettingsService
    {
        private readonly string _settingsFile;
        private AppSettings _settings;

        public SettingsService()
        {
            // Use portable path
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntunePackagingTool"
            );

            Directory.CreateDirectory(appDataPath);
            _settingsFile = Path.Combine(appDataPath, "settings.json");

            LoadSettings();
        }

        public AppSettings GetDefaultSettings()
        {
            return new AppSettings
            {
                Authentication = new AppSettings.AuthenticationSettings
                {
                    ClientId = "b47987a1-70b4-415a-9a4e-9775473e382b",
                    TenantId = "43f10d24-b9bf-46da-a9c8-15c1b0990ce7",
                    CertificateThumbprint = "CF6DCE7DF3377CA65D9B40F06BF8C2228AC7821F",
                    Method = AppSettings.AuthMethod.Certificate
                },
                Paths = new AppSettings.PathSettings
                {
                    PSADTTemplatePath = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\20250811\Application",
                    IntuneWinAppUtilPath = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\IntuneWinAppUtil.exe",
                    OutputDirectory = @"\\nbb.local\sys\SCCMData\IntuneApplications",
                    TempDirectory = @"C:\Temp\IntunePackaging",
                    UsePSADT = true,
                    AutoCleanupTemp = true
                },
                Upload = new AppSettings.UploadSettings
                {
                    ChunkSizeMB = 6,
                    UploadTimeoutMinutes = 30,
                    MaxRetryAttempts = 3,
                    RetryDelaySeconds = 10
                },
                UI = new AppSettings.UISettings(),
                Logging = new AppSettings.LoggingSettings()
            };
        }


        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    var json = File.ReadAllText(_settingsFile);
                    _settings = JsonSerializer.Deserialize<AppSettings>(json) ?? GetDefaultSettings();
                }
                else
                {
                    // First run - use your organization's defaults
                    _settings = GetDefaultSettings();
                    SaveSettings();
                }
            }
            catch
            {
                _settings = GetDefaultSettings();
            }
        }





        private void MigrateSettings()
        {
            // Handle version upgrades
            if (string.IsNullOrEmpty(_settings.AppVersion))
            {
                _settings.AppVersion = "1.0.0";
                _settings.FirstRun = false;
            }
        }

        public void SaveSettings()
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_settingsFile, json);
            }
            catch
            {
                // Silent fail - don't crash over settings
            }
        }

        public void ResetToDefaults()
        {
            _settings = new AppSettings();
           
            SaveSettings();
        }
    }
}