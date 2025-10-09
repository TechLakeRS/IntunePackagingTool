using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace IntunePackagingTool.Services
{
    public enum AppTheme
    {
        Light,
        Dark,
        System
    }

    public class ThemeService
    {
        private const string ThemeRegistryKey = @"SOFTWARE\NBB\IntunePackagingTool";
        private const string ThemeValueName = "Theme";
        private static ThemeService? _instance;
        private AppTheme _currentTheme;

        public static ThemeService Instance => _instance ??= new ThemeService();

        public event EventHandler<AppTheme>? ThemeChanged;

        public AppTheme CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ApplyTheme(value);
                    SaveThemePreference(value);
                    ThemeChanged?.Invoke(this, value);
                }
            }
        }

        private ThemeService()
        {
            LoadThemePreference();
        }

        public void Initialize()
        {
            // Apply the loaded theme on initialization
            ApplyTheme(_currentTheme);

            // If system theme, monitor for system theme changes
            if (_currentTheme == AppTheme.System)
            {
                MonitorSystemTheme();
            }
        }

        private void ApplyTheme(AppTheme theme)
        {
            var application = Application.Current;
            if (application == null) return;

            // Determine actual theme to apply
            var actualTheme = theme;
            if (theme == AppTheme.System)
            {
                actualTheme = GetSystemTheme();
            }

            // Clear existing theme dictionaries (but NOT ModernTheme.xaml)
            var mergedDictionaries = application.Resources.MergedDictionaries;
            ResourceDictionary? existingTheme = null;

            foreach (var dict in mergedDictionaries)
            {
                // Only remove DarkTheme or LightTheme, NOT ModernTheme
                if (dict.Source != null &&
                    (dict.Source.OriginalString.Contains("DarkTheme.xaml") ||
                     dict.Source.OriginalString.Contains("LightTheme.xaml")))
                {
                    existingTheme = dict;
                    break;
                }
            }

            if (existingTheme != null)
            {
                mergedDictionaries.Remove(existingTheme);
            }

            // Add new theme dictionary
            var themeUri = actualTheme == AppTheme.Dark
                ? new Uri("Themes/DarkTheme.xaml", UriKind.Relative)
                : new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            var themeDictionary = new ResourceDictionary { Source = themeUri };
            mergedDictionaries.Add(themeDictionary);
        }

        private AppTheme GetSystemTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    var value = key.GetValue("AppsUseLightTheme");
                    if (value is int intValue)
                    {
                        return intValue == 0 ? AppTheme.Dark : AppTheme.Light;
                    }
                }
            }
            catch
            {
                // Fallback to light theme if we can't read system preference
            }

            return AppTheme.Light;
        }

        private void MonitorSystemTheme()
        {
            // Set up a timer to check for system theme changes
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };

            var lastSystemTheme = GetSystemTheme();

            timer.Tick += (s, e) =>
            {
                if (_currentTheme != AppTheme.System)
                {
                    timer.Stop();
                    return;
                }

                var currentSystemTheme = GetSystemTheme();
                if (currentSystemTheme != lastSystemTheme)
                {
                    lastSystemTheme = currentSystemTheme;
                    ApplyTheme(AppTheme.System);
                }
            };

            timer.Start();
        }

        private void LoadThemePreference()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryKey);
                if (key != null)
                {
                    var value = key.GetValue(ThemeValueName);
                    if (value is string stringValue && Enum.TryParse<AppTheme>(stringValue, out var theme))
                    {
                        _currentTheme = theme;
                        return;
                    }
                }
            }
            catch
            {
                // Ignore registry errors
            }

            // Default to system theme
            _currentTheme = AppTheme.System;
        }

        private void SaveThemePreference(AppTheme theme)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(ThemeRegistryKey);
                key?.SetValue(ThemeValueName, theme.ToString());
            }
            catch
            {
                // Ignore registry errors
            }
        }

        public void ToggleTheme()
        {
            // Cycle through themes: Light -> Dark -> System -> Light
            CurrentTheme = CurrentTheme switch
            {
                AppTheme.Light => AppTheme.Dark,
                AppTheme.Dark => AppTheme.System,
                AppTheme.System => AppTheme.Light,
                _ => AppTheme.Light
            };
        }
    }
}