using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace IntunePackagingTool.Utilities
{
    public static class NetworkShareHelper
    {
        private static readonly string SharePath = @"\\nbb.local\sys\sccmdata\intuneapplications";

        public static string FindApplicationPath(string appName, string version = null)
        {
            try
            {
                if (!Directory.Exists(SharePath))
                {
                    Debug.WriteLine($"Network share not accessible: {SharePath}");
                    return null;
                }

                var folders = Directory.GetDirectories(SharePath);

                // Create multiple search patterns to find the folder
                var searchPatterns = CreateSearchPatterns(appName, version);

                foreach (var pattern in searchPatterns)
                {
                    // Try exact match first
                    var exactMatch = folders.FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), pattern, StringComparison.OrdinalIgnoreCase));

                    if (exactMatch != null)
                    {
                        Debug.WriteLine($"Found exact match: {exactMatch}");
                        return exactMatch;
                    }
                }

                // If no exact match, try partial matching
                foreach (var pattern in searchPatterns)
                {
                    var partialMatch = folders.FirstOrDefault(f =>
                        Path.GetFileName(f).ToLower().Contains(pattern.ToLower()));

                    if (partialMatch != null)
                    {
                        Debug.WriteLine($"Found partial match: {partialMatch}");
                        return partialMatch;
                    }
                }

                Debug.WriteLine($"No network share folder found for: {appName}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding app path: {ex.Message}");
                return null;
            }
        }

        private static List<string> CreateSearchPatterns(string appName, string version)
        {
            var patterns = new List<string>();

            if (string.IsNullOrEmpty(appName))
                return patterns;

            // Clean app name variations
            var cleanName = appName.Trim();
            var dashName = cleanName.Replace(" ", "-");
            var noSpaceName = cleanName.Replace(" ", "");

            // Add patterns with and without version
            patterns.Add(cleanName);
            patterns.Add(dashName);
            patterns.Add(noSpaceName);

            if (!string.IsNullOrEmpty(version))
            {
                var cleanVersion = version.Replace("v", "").Replace("V", "");
                patterns.Add($"{cleanName}-{version}");
                patterns.Add($"{dashName}-{version}");
                patterns.Add($"{cleanName}-{cleanVersion}");
                patterns.Add($"{dashName}-{cleanVersion}");
                patterns.Add($"{noSpaceName}-{cleanVersion}");
            }

            return patterns.Distinct().ToList();
        }

        public static bool HasPSADTScript(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return false;

            return File.Exists(Path.Combine(folderPath, "Deploy-Application.ps1"));
        }
    }
}