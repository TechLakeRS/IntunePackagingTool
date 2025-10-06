// Services/SettingsService.cs
using System;
using System.IO;
using System.Text.Json;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class SettingsService
    {
        private readonly string _appSettingsFile;
        private readonly string _userSettingsFile;
        private AppSettings? _settings;

        public SettingsService()
        {
            // appsettings.json is in the application directory (for default config)
            _appSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            // User settings are stored in LocalApplicationData (for user overrides)
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntunePackagingTool"
            );

            Directory.CreateDirectory(appDataPath);
            _userSettingsFile = Path.Combine(appDataPath, "settings.json");

            LoadSettings();
        }

        public void LoadSettings()
        {
            try
            {
                // First, load from appsettings.json (required config file)
                if (File.Exists(_appSettingsFile))
                {
                    var json = File.ReadAllText(_appSettingsFile);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    _settings = JsonSerializer.Deserialize<AppSettings>(json, options);

                    if (_settings == null)
                    {
                        throw new Exception("Failed to deserialize appsettings.json");
                    }

                    // Ensure Authentication is not null
                    if (_settings.Authentication == null)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: Authentication settings is null, creating default");
                        _settings.Authentication = new AppSettings.AuthenticationSettings();
                    }

                    // Debug: Log what was loaded
                    System.Diagnostics.Debug.WriteLine($"Loaded appsettings.json from: {_appSettingsFile}");
                    System.Diagnostics.Debug.WriteLine($"  TenantId: {_settings.Authentication?.TenantId ?? "(null)"}");
                    System.Diagnostics.Debug.WriteLine($"  ClientId: {_settings.Authentication?.ClientId ?? "(null)"}");
                    System.Diagnostics.Debug.WriteLine($"  CertificateThumbprint: {_settings.Authentication?.CertificateThumbprint ?? "(null)"}");
                }
                else
                {
                    throw new FileNotFoundException($"appsettings.json not found at: {_appSettingsFile}");
                }

                // Then, merge/override with user settings if they exist
                if (File.Exists(_userSettingsFile))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading user settings from: {_userSettingsFile}");
                    var userJson = File.ReadAllText(_userSettingsFile);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    var userSettings = JsonSerializer.Deserialize<AppSettings>(userJson, options);

                    if (userSettings != null)
                    {
                        // Merge user settings (user settings override defaults)
                        if (userSettings.Authentication != null)
                        {
                            System.Diagnostics.Debug.WriteLine("User settings Authentication found:");
                            System.Diagnostics.Debug.WriteLine($"  TenantId: {userSettings.Authentication?.TenantId ?? "(null)"}");
                            System.Diagnostics.Debug.WriteLine($"  ClientId: {userSettings.Authentication?.ClientId ?? "(null)"}");
                            System.Diagnostics.Debug.WriteLine($"  CertificateThumbprint: {userSettings.Authentication?.CertificateThumbprint ?? "(null)"}");

                            // Only override non-empty values
                            if (!string.IsNullOrEmpty(userSettings.Authentication.TenantId))
                                _settings.Authentication.TenantId = userSettings.Authentication.TenantId;
                            if (!string.IsNullOrEmpty(userSettings.Authentication.ClientId))
                                _settings.Authentication.ClientId = userSettings.Authentication.ClientId;
                            if (!string.IsNullOrEmpty(userSettings.Authentication.CertificateThumbprint))
                                _settings.Authentication.CertificateThumbprint = userSettings.Authentication.CertificateThumbprint;
                        }
                        if (userSettings.Paths != null)
                            _settings.Paths = userSettings.Paths;
                        if (userSettings.Upload != null)
                            _settings.Upload = userSettings.Upload;
                        if (userSettings.UI != null)
                            _settings.UI = userSettings.UI;
                        if (userSettings.Logging != null)
                            _settings.Logging = userSettings.Logging;
                        if (userSettings.CodeSigning != null)
                            _settings.CodeSigning = userSettings.CodeSigning;
                        if (userSettings.NetworkPaths != null)
                            _settings.NetworkPaths = userSettings.NetworkPaths;
                        if (userSettings.WDACHyperV != null)
                            _settings.WDACHyperV = userSettings.WDACHyperV;
                        if (userSettings.GroupAssignment != null)
                            _settings.GroupAssignment = userSettings.GroupAssignment;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load application settings: {ex.Message}", ex);
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

                File.WriteAllText(_userSettingsFile, json);
            }
            catch
            {
                // Silent fail - don't crash over settings
            }
        }

        public AppSettings Settings
        {
            get
            {
                if (_settings == null)
                {
                    LoadSettings();
                }
                return _settings!;
            }
        }
    }
}