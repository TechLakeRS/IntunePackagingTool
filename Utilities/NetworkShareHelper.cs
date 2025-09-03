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

                // First, try to extract version from the app name if it's embedded
                var (baseName, extractedVersion) = ExtractVersionFromName(appName);

                // Use extracted version if no explicit version provided
                if (string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(extractedVersion))
                {
                    version = extractedVersion;
                    Debug.WriteLine($"Extracted version '{version}' from name '{appName}'");
                }

                // Use the base name (without version) for searching
                var searchName = baseName;
                Debug.WriteLine($"Searching for base folder: '{searchName}' with version: '{version}'");

                var folders = Directory.GetDirectories(SharePath);

                // Look for exact folder match (case-insensitive)
                var baseFolder = folders.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), searchName, StringComparison.OrdinalIgnoreCase));

                if (baseFolder == null)
                {
                    // Try with underscores replaced by spaces or vice versa
                    var alternativeNames = new[]
                    {
                    searchName.Replace("_", " "),
                    searchName.Replace(" ", "_"),
                    searchName.Replace("_", "-"),
                    searchName.Replace("-", "_")
                };

                    foreach (var altName in alternativeNames)
                    {
                        baseFolder = folders.FirstOrDefault(f =>
                            string.Equals(Path.GetFileName(f), altName, StringComparison.OrdinalIgnoreCase));
                        if (baseFolder != null) break;
                    }
                }

                if (baseFolder == null)
                {
                    // Try partial match as last resort
                    baseFolder = folders.FirstOrDefault(f =>
                    {
                        var folderName = Path.GetFileName(f).ToLower();
                        var searchLower = searchName.ToLower();

                        // Remove common suffixes for matching
                        var cleanSearch = searchLower
                            .Replace("_x64", "")
                            .Replace("_x86", "")
                            .Replace("_(user)", "")
                            .Replace("_user", "");

                        return folderName.Contains(cleanSearch) || cleanSearch.Contains(folderName);
                    });
                }

                if (baseFolder != null)
                {
                    Debug.WriteLine($"Found base folder: {baseFolder}");

                    // If we have a version, look for the version subfolder
                    if (!string.IsNullOrEmpty(version))
                    {
                        var versionPath = FindVersionFolder(baseFolder, version);
                        if (versionPath != null)
                        {
                            Debug.WriteLine($"Found complete path with version: {versionPath}");
                            return versionPath;
                        }
                        else
                        {
                            Debug.WriteLine($"Version folder '{version}' not found in {baseFolder}");
                        }
                    }

                    // Return base folder if no version or version not found
                    return baseFolder;
                }

                Debug.WriteLine($"No network share folder found for: {searchName}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding app path: {ex.Message}");
                return null;
            }
        }

        private static (string baseName, string version) ExtractVersionFromName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return (fullName, null);

            // Common version patterns to look for at the end of the name
            var versionPatterns = new[]
            {
            @"_(\d+\.\d+\.\d+\.\d+)$",  // _1.2.3.4
            @"_(\d+\.\d+\.\d+)$",        // _1.2.3
            @"_(\d+\.\d+)$",              // _1.2
            @"_v(\d+\.\d+\.\d+)$",       // _v1.2.3
            @"_V(\d+\.\d+\.\d+)$",       // _V1.2.3
            @"\s+(\d+\.\d+\.\d+)$",      // Space before version
            @"-(\d+\.\d+\.\d+)$"         // -1.2.3
        };

            foreach (var pattern in versionPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(fullName, pattern);
                if (match.Success)
                {
                    var version = match.Groups[1].Value;
                    var baseName = fullName.Substring(0, match.Index);
                    Debug.WriteLine($"Extracted: base='{baseName}', version='{version}'");
                    return (baseName, version);
                }
            }

            // No version found in name
            return (fullName, null);
        }

        private static string FindVersionFolder(string basePath, string version)
        {
            try
            {
                if (!Directory.Exists(basePath))
                {
                    Debug.WriteLine($"Base path does not exist: {basePath}");
                    return null;
                }

                var subFolders = Directory.GetDirectories(basePath);
                Debug.WriteLine($"Found {subFolders.Length} subfolders in {basePath}");

                // Clean the version for comparison
                var cleanVersion = version.Trim();
                var versionVariations = new[]
                {
                cleanVersion,
                cleanVersion.Replace("v", "").Replace("V", ""),
                $"v{cleanVersion}",
                $"V{cleanVersion}",
                cleanVersion.Replace(".", "_"),
                cleanVersion.Replace("_", ".")
            };

                // Try exact match with variations
                foreach (var variant in versionVariations)
                {
                    var match = subFolders.FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), variant, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        Debug.WriteLine($"Found version folder: {match}");
                        return match;
                    }
                }

                // Try partial match
                var partialMatch = subFolders.FirstOrDefault(f =>
                {
                    var folderName = Path.GetFileName(f).ToLower();
                    return versionVariations.Any(v => folderName.Contains(v.ToLower()));
                });

                if (partialMatch != null)
                {
                    Debug.WriteLine($"Found partial version match: {partialMatch}");
                    return partialMatch;
                }

                Debug.WriteLine($"No version folder found for version: {version}");

                // List available versions for debugging
                foreach (var folder in subFolders.Take(5))
                {
                    Debug.WriteLine($"  Available: {Path.GetFileName(folder)}");
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding version folder: {ex.Message}");
                return null;
            }
        }
    }
}