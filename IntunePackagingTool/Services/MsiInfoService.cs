using System;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace IntunePackagingTool.Services
{
    /// <summary>
    /// Service for extracting metadata from MSI files without installation
    /// </summary>
    public class MsiInfoService
    {
        public class MsiInfo
        {
            public string ProductCode { get; set; } = "";
            public string ProductName { get; set; } = "";
            public string ProductVersion { get; set; } = "";
            public string Manufacturer { get; set; } = "";
            public string UpgradeCode { get; set; } = "";
            public bool IsValid => !string.IsNullOrEmpty(ProductCode);
        }

        /// <summary>
        /// Extracts MSI information from an MSI file
        /// </summary>
        public static MsiInfo ExtractMsiInfo(string msiPath)
        {
            var info = new MsiInfo();

            try
            {
                Type? installerType = Type.GetTypeFromProgID("WindowsInstaller.Installer");
                if (installerType == null)
                {
                    Debug.WriteLine("Windows Installer COM object not available");
                    return info;
                }

                dynamic? installer = Activator.CreateInstance(installerType);
                if (installer == null)
                {
                    Debug.WriteLine("Failed to create Windows Installer instance");
                    return info;
                }

                // Open the MSI database
                dynamic database = installer.OpenDatabase(msiPath, 0); // 0 = read-only

                // Extract properties
                info.ProductCode = GetMsiProperty(database, "ProductCode");
                info.ProductName = GetMsiProperty(database, "ProductName");
                info.ProductVersion = GetMsiProperty(database, "ProductVersion");
                info.Manufacturer = GetMsiProperty(database, "Manufacturer");
                info.UpgradeCode = GetMsiProperty(database, "UpgradeCode");

                // Log extracted information
                Debug.WriteLine("=== MSI Information Extracted ===");
                Debug.WriteLine($"Product Code: {info.ProductCode}");
                Debug.WriteLine($"Product Name: {info.ProductName}");
                Debug.WriteLine($"Product Version: {info.ProductVersion}");
                Debug.WriteLine($"Manufacturer: {info.Manufacturer}");
                Debug.WriteLine($"Upgrade Code: {info.UpgradeCode}");
                Debug.WriteLine("================================");

                // Validate Product Code format
                if (!string.IsNullOrEmpty(info.ProductCode) && !IsValidGuid(info.ProductCode))
                {
                    Debug.WriteLine($"Warning: Product Code '{info.ProductCode}' is not a valid GUID format");
                }

                // Release COM objects
                if (database != null)
                {
                    Marshal.ReleaseComObject(database);
                }
                if (installer != null)
                {
                    Marshal.ReleaseComObject(installer);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting MSI info: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return info;
        }

        /// <summary>
        /// Gets a property value from an MSI database
        /// </summary>
        private static string GetMsiProperty(dynamic database, string propertyName)
        {
            try
            {
                // Create SQL query to get the property
                string sql = $"SELECT `Value` FROM `Property` WHERE `Property` = '{propertyName}'";
                dynamic view = database.OpenView(sql);
                view.Execute();

                dynamic record = view.Fetch();
                if (record != null)
                {
                    string value = record.StringData[1];
                    Marshal.ReleaseComObject(record);
                    Marshal.ReleaseComObject(view);
                    return value ?? "";
                }

                Marshal.ReleaseComObject(view);
                return "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting MSI property '{propertyName}': {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Validates if a string is a valid GUID format
        /// </summary>
        private static bool IsValidGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;

            // MSI GUIDs should be in format {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}
            if (!guid.StartsWith("{") || !guid.EndsWith("}"))
                return false;

            // Try to parse the GUID (remove the braces first)
            string cleanGuid = guid.Trim('{', '}');
            return Guid.TryParse(cleanGuid, out _);
        }

        /// <summary>
        /// Alternative method using msiexec to extract info (fallback)
        /// </summary>
        public static MsiInfo ExtractMsiInfoUsingMsiExec(string msiPath)
        {
            var info = new MsiInfo();

            try
            {
                // Use msiexec with logging to extract info
                string tempLogFile = System.IO.Path.GetTempFileName();

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = $"/a \"{msiPath}\" /qn TARGETDIR=\"%TEMP%\\MsiExtract\" /l*v \"{tempLogFile}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                process.Start();
                process.WaitForExit(5000); // Wait max 5 seconds

                // Parse the log file for product code
                if (System.IO.File.Exists(tempLogFile))
                {
                    string[] lines = System.IO.File.ReadAllLines(tempLogFile);
                    foreach (string line in lines)
                    {
                        if (line.Contains("ProductCode"))
                        {
                            // Extract product code from log
                            int startIndex = line.IndexOf("{");
                            int endIndex = line.IndexOf("}", startIndex);
                            if (startIndex >= 0 && endIndex > startIndex)
                            {
                                info.ProductCode = line.Substring(startIndex, endIndex - startIndex + 1);
                                break;
                            }
                        }
                    }

                    // Clean up
                    System.IO.File.Delete(tempLogFile);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error using msiexec fallback: {ex.Message}");
            }

            return info;
        }
    }
}