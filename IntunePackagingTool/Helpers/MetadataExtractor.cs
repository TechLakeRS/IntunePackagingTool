using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace IntunePackagingTool.Helpers
{
    public static class MetadataExtractor
    {
        /// <summary>
        /// Extracts metadata from MSI file using Windows Installer COM API
        /// </summary>
        public static (string productName, string manufacturer, string version, string productCode) ExtractMsiMetadata(string msiPath)
        {
            object? installer = null;
            object? database = null;

            try
            {
                Type? installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                if (installerType == null)
                {
                    Debug.WriteLine("Windows Installer not available");
                    return (string.Empty, string.Empty, string.Empty, string.Empty);
                }

                installer = Activator.CreateInstance(installerType);
                if (installer == null)
                {
                    Debug.WriteLine("Failed to create Windows Installer instance");
                    return (string.Empty, string.Empty, string.Empty, string.Empty);
                }

                database = installerType.InvokeMember("OpenDatabase",
                    System.Reflection.BindingFlags.InvokeMethod, null, installer,
                    new object[] { msiPath, 0 });

                string productName = GetMsiProperty(database, "ProductName");
                string manufacturer = GetMsiProperty(database, "Manufacturer");
                string version = GetMsiProperty(database, "ProductVersion");
                string productCode = GetMsiProperty(database, "ProductCode");

                Debug.WriteLine($"Extracted MSI metadata: {productName} v{version} by {manufacturer}");
                Debug.WriteLine($"Product Code: {productCode}");

                return (productName, manufacturer, version, productCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MSI metadata extraction failed: {ex.Message}");
                return (string.Empty, string.Empty, string.Empty, string.Empty);
            }
            finally
            {
                // Release COM objects
                if (database != null) Marshal.ReleaseComObject(database);
                if (installer != null) Marshal.ReleaseComObject(installer);
            }
        }

        /// <summary>
        /// Gets a property value from MSI database
        /// </summary>
        private static string GetMsiProperty(object? database, string property)
        {
            object? view = null;
            object? record = null;

            try
            {
                if (database == null) return "";

                var databaseType = database.GetType();
                view = databaseType.InvokeMember("OpenView",
                    System.Reflection.BindingFlags.InvokeMethod, null, database,
                    new object[] { $"SELECT Value FROM Property WHERE Property = '{property}'" });

                if (view == null) return "";

                var viewType = view.GetType();
                viewType.InvokeMember("Execute", System.Reflection.BindingFlags.InvokeMethod,
                    null, view, null);

                record = viewType.InvokeMember("Fetch", System.Reflection.BindingFlags.InvokeMethod,
                    null, view, null);

                if (record == null) return "";

                var recordType = record.GetType();
                var stringData = recordType.InvokeMember("StringData",
                    System.Reflection.BindingFlags.GetProperty, null, record, new object[] { 1 });

                return stringData?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
            finally
            {
                if (record != null) Marshal.ReleaseComObject(record);
                if (view != null) Marshal.ReleaseComObject(view);
            }
        }

        /// <summary>
        /// Extracts metadata from EXE file version info
        /// </summary>
        public static (string productName, string companyName, string version) ExtractExeMetadata(string exePath)
        {
            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(exePath);

                string? productName = versionInfo.ProductName;
                string? companyName = versionInfo.CompanyName;
                string? version = versionInfo.ProductVersion ?? versionInfo.FileVersion;

                Debug.WriteLine($"Extracted EXE metadata: {productName} v{version} by {companyName}");

                return (
                    CleanProductName(productName ?? ""),
                    companyName ?? "",
                    version ?? ""
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"EXE metadata extraction failed: {ex.Message}");
                return (string.Empty, string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// Cleans common installer suffixes from product names
        /// </summary>
        public static string CleanProductName(string productName)
        {
            if (string.IsNullOrWhiteSpace(productName)) return "";

            return productName
                .Replace(" Setup", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Installer", "", StringComparison.OrdinalIgnoreCase)
                .Replace(" Installation", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }

        /// <summary>
        /// Extracts application name from filename as fallback
        /// </summary>
        public static string ExtractNameFromFilename(string filePath)
        {
            string appName = Path.GetFileNameWithoutExtension(filePath);

            appName = appName
                .Replace("_setup", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Setup", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_installer", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Installer", "", StringComparison.OrdinalIgnoreCase)
                .Replace("_install", "", StringComparison.OrdinalIgnoreCase)
                .Replace("Install", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            Debug.WriteLine($"Extracted name from filename: {appName}");
            return appName;
        }

        /// <summary>
        /// Extracts metadata from PowerShell script (Deploy-Application.ps1)
        /// </summary>
        public static Dictionary<string, string> ExtractMetadataFromScript(string scriptPath)
        {
            var metadata = new Dictionary<string, string>();

            try
            {
                var scriptContent = File.ReadAllText(scriptPath);

                Debug.WriteLine($"Script content preview: {scriptContent.Substring(0, Math.Min(500, scriptContent.Length))}");

                // Extract metadata using helper method
                metadata["Vendor"] = ExtractPowerShellVariable(scriptContent, "appVendor") ?? "";
                metadata["AppName"] = ExtractPowerShellVariable(scriptContent, "appName") ?? "";
                metadata["Version"] = ExtractPowerShellVariable(scriptContent, "appVersion") ?? "";
                metadata["ScriptDate"] = ExtractPowerShellVariable(scriptContent, "appScriptDate") ?? "";
                metadata["ScriptAuthor"] = ExtractPowerShellVariable(scriptContent, "appScriptAuthor") ?? "";

                // Alternative: Try to find variables in comment blocks if not found
                if (string.IsNullOrEmpty(metadata["AppName"]))
                {
                    var altNameMatch = System.Text.RegularExpressions.Regex.Match(
                        scriptContent, @"#\s*App Name\s*:\s*(.+)$",
                        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (altNameMatch.Success)
                    {
                        metadata["AppName"] = altNameMatch.Groups[1].Value.Trim();
                        Debug.WriteLine($"Found name in comments: {altNameMatch.Groups[1].Value}");
                    }
                }

                // Log summary of what was extracted
                Debug.WriteLine("=== Extraction Summary ===");
                foreach (var kvp in metadata)
                {
                    Debug.WriteLine($"{kvp.Key}: {kvp.Value}");
                }
                Debug.WriteLine("========================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting metadata: {ex.Message}");
            }

            return metadata;
        }

        /// <summary>
        /// Extracts PowerShell variable value from script content
        /// </summary>
        private static string? ExtractPowerShellVariable(string scriptContent, string variableName)
        {
            // Try standard pattern first
            var match = System.Text.RegularExpressions.Regex.Match(
                scriptContent, $@"\${variableName}\s*=\s*['""]([^'""]*)['""]",
                System.Text.RegularExpressions.RegexOptions.Multiline);

            if (!match.Success)
            {
                // Try pattern with leading whitespace
                match = System.Text.RegularExpressions.Regex.Match(
                    scriptContent, $@"^\s*\${variableName}\s*=\s*['""]([^'""]*)['""]",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
            }

            if (match.Success)
            {
                Debug.WriteLine($"Found {variableName}: {match.Groups[1].Value}");
                return match.Groups[1].Value;
            }

            Debug.WriteLine($"{variableName} not found");
            return null;
        }
    }
}
