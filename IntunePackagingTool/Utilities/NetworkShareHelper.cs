using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace IntunePackagingTool.Utilities
{
    public static class NetworkShareHelper
    {
        private static readonly string SharePath = @"\\nbb.local\sys\sccmdata\intuneapplications";

        public static string? FindApplicationPath(string displayName, string? version = null)
        {
            try
            {
                if (!Directory.Exists(SharePath))
                {
                    Debug.WriteLine($"Network share not accessible: {SharePath}");
                    return null;
                }

                // Extract version from display name if present
                var (nameWithoutVersion, extractedVersion) = ExtractVersion(displayName);

                // Use extracted version if no explicit version provided
                if (string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(extractedVersion))
                {
                    version = extractedVersion;
                }

                Debug.WriteLine($"Looking for: '{nameWithoutVersion}' version '{version}'");

                // Convert display name to expected folder format (replace spaces with underscores)
                var expectedFolderName = nameWithoutVersion.Replace(" ", "_");
                Debug.WriteLine($"Expected folder name: {expectedFolderName}");

                // Find the base folder
                var baseFolder = FindBaseFolder(expectedFolderName);

                if (baseFolder != null)
                {
                    Debug.WriteLine($"Found base folder: {baseFolder}");

                    // If we have a version, look for the version subfolder
                    if (!string.IsNullOrEmpty(version))
                    {
                        var versionPath = FindVersionFolder(baseFolder, version);
                        if (versionPath != null)
                        {
                            Debug.WriteLine($"✓ Found complete path: {versionPath}");
                            return versionPath;
                        }
                        else
                        {
                            Debug.WriteLine($"Version folder '{version}' not found, returning base folder");
                            return baseFolder;
                        }
                    }

                    return baseFolder;
                }

                Debug.WriteLine($"❌ No network share folder found for: {displayName}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding app path: {ex.Message}");
                return null;
            }
        }

        private static (string nameWithoutVersion, string? version) ExtractVersion(string displayName)
        {
            if (string.IsNullOrEmpty(displayName))
                return (displayName, null);

            // Look for version pattern at the end (e.g., "1.2.3.4" or "1.2.3")
            var versionMatch = Regex.Match(displayName, @"\s+(\d+(?:\.\d+)+)$");

            if (versionMatch.Success)
            {
                var version = versionMatch.Groups[1].Value;
                var nameWithoutVersion = displayName.Substring(0, versionMatch.Index).Trim();
                Debug.WriteLine($"Extracted version '{version}' from display name");
                return (nameWithoutVersion, version);
            }

            return (displayName, null);
        }

        private static string? FindBaseFolder(string expectedFolderName)
        {
            try
            {
                var folders = Directory.GetDirectories(SharePath);

                // Try exact match first (case-insensitive)
                var exactMatch = folders.FirstOrDefault(f =>
                    string.Equals(Path.GetFileName(f), expectedFolderName, StringComparison.OrdinalIgnoreCase));

                if (exactMatch != null)
                {
                    Debug.WriteLine($"Found exact match: {Path.GetFileName(exactMatch)}");
                    return exactMatch;
                }

                // Generate variations to try
                var variations = GenerateFolderVariations(expectedFolderName);

                foreach (var variation in variations)
                {
                    var match = folders.FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), variation, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        Debug.WriteLine($"Found with variation '{variation}': {Path.GetFileName(match)}");
                        return match;
                    }
                }

                // Try partial match as last resort
                var partialMatch = FindByPartialMatch(expectedFolderName, folders);
                if (partialMatch != null)
                {
                    Debug.WriteLine($"Found by partial match: {Path.GetFileName(partialMatch)}");
                    return partialMatch;
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FindBaseFolder: {ex.Message}");
                return null;
            }
        }

        private static string[] GenerateFolderVariations(string folderName)
        {
            var variations = new HashSet<string>();

            // Original
            variations.Add(folderName);

            // Replace different separators
            variations.Add(folderName.Replace("_", "-"));
            variations.Add(folderName.Replace("_", "."));
            variations.Add(folderName.Replace("_", " "));

            // Handle multiple underscores (in case of extra underscores)
            variations.Add(Regex.Replace(folderName, @"_+", "_"));

            // Remove trailing/leading underscores
            variations.Add(folderName.Trim('_'));

            // Try with common suffixes/prefixes that might be added or removed
            variations.Add(folderName + "_x64");
            variations.Add(folderName + "_x86");
            variations.Add(folderName.Replace("_x64", "").Replace("_x86", ""));

            return variations.ToArray();
        }

        private static string? FindByPartialMatch(string expectedName, string[] folders)
        {
            // Clean the expected name for comparison
            var cleanExpected = Regex.Replace(expectedName.ToLower(), @"[_\-\.\s]", "");

            // If the name is too short, don't do partial matching
            if (cleanExpected.Length < 5)
                return null;

            string? bestMatch = null;
            double bestScore = 0.6; // Minimum 60% match

            foreach (var folder in folders)
            {
                var folderName = Path.GetFileName(folder);
                var cleanFolder = Regex.Replace(folderName.ToLower(), @"[_\-\.\s]", "");

                // Calculate similarity score
                double score = CalculateSimilarity(cleanExpected, cleanFolder);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = folder;
                    Debug.WriteLine($"Potential match: {folderName} (score: {score:P0})");
                }
            }

            return bestMatch;
        }

        private static double CalculateSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            // Check if one contains the other
            if (s1.Contains(s2) || s2.Contains(s1))
                return 0.8;

            // Calculate Levenshtein distance for similarity
            int maxLen = Math.Max(s1.Length, s2.Length);
            if (maxLen == 0)
                return 1.0;

            int distance = LevenshteinDistance(s1, s2);
            return 1.0 - (double)distance / maxLen;
        }

        private static int LevenshteinDistance(string s1, string s2)
        {
            int[,] distance = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                distance[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                distance[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[s1.Length, s2.Length];
        }

        private static string? FindVersionFolder(string basePath, string version)
        {
            try
            {
                if (!Directory.Exists(basePath))
                    return null;

                var subFolders = Directory.GetDirectories(basePath);

                if (subFolders.Length == 0)
                    return null;

                // Clean the version
                var cleanVersion = version.Trim().TrimStart('v', 'V');

                // Generate version variations
                var versionVariations = new HashSet<string>
                {
                    cleanVersion,
                    $"v{cleanVersion}",
                    $"V{cleanVersion}",
                    cleanVersion.Replace(".", "_"),
                    cleanVersion.Replace(".", "-"),
                    cleanVersion.Replace(".", "")
                };

                // Add variations with different zero padding
                if (Regex.IsMatch(cleanVersion, @"^\d+\.\d+\.\d+\.\d+$"))
                {
                    // If it's a 4-part version, try 3-part as well
                    var parts = cleanVersion.Split('.');
                    if (parts[3] == "0")
                    {
                        versionVariations.Add($"{parts[0]}.{parts[1]}.{parts[2]}");
                    }
                }

                // Try exact match with variations
                foreach (var variant in versionVariations)
                {
                    var match = subFolders.FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f), variant, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        Debug.WriteLine($"Found version folder: {Path.GetFileName(match)}");
                        return match;
                    }
                }

                // Try folders that start with the version
                foreach (var variant in versionVariations)
                {
                    var match = subFolders.FirstOrDefault(f =>
                        Path.GetFileName(f).StartsWith(variant, StringComparison.OrdinalIgnoreCase));

                    if (match != null)
                    {
                        Debug.WriteLine($"Found version folder by prefix: {Path.GetFileName(match)}");
                        return match;
                    }
                }

                // Log available folders for debugging
                Debug.WriteLine($"Version '{version}' not found. Available subfolders:");
                foreach (var folder in subFolders.Take(5))
                {
                    Debug.WriteLine($"  - {Path.GetFileName(folder)}");
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