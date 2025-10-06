using IntunePackagingTool.Models;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using IntunePackagingTool.Configuration;


namespace IntunePackagingTool.Services
{
    public interface IUploadProgress
    {
        void UpdateProgress(int percentage, string message);
    }

    public class IntuneUploadService : IDisposable
    {
        private HttpClient? sharedHttpClient;
        private readonly IntuneService _intuneService;
        private string _currentAppId = "";
        private string _currentContentVersionId = "";
        private string _currentFileId = "";
        public string ConverterPath => Paths.IntuneWinAppUtil;

        public IntuneUploadService(IntuneService intuneService)
        {
            _intuneService = intuneService;
        }

        private void EnsureHttpClient()
        {
            if (sharedHttpClient == null)
            {
                sharedHttpClient = new HttpClient();
                sharedHttpClient.Timeout = TimeSpan.FromMinutes(30);
            }
        }

        public async Task<string> UploadWin32ApplicationAsync(
            ApplicationInfo appInfo,
            string packagePath,
            List<DetectionRule> detectionRules,
            string installCommand,
            string uninstallCommand,
            string description,
            string installContext,
            string? iconPath = null,
            IUploadProgress? progress = null)
        {
            try
            {
                progress?.UpdateProgress(5, "Authenticating with Microsoft Graph...");
                var token = await _intuneService.GetAccessTokenAsync();
                EnsureHttpClient();
                sharedHttpClient!.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // Step 0 - Sign files before conversion
                progress?.UpdateProgress(10, "Signing application files...");
                await SignApplicationFilesAsync(packagePath, progress);

                // Step 1: Create the .intunewin file
                progress?.UpdateProgress(20, "Converting package to .intunewin format...");
                await CreateIntuneWinFileAsync(packagePath);

                // Step 2: Find the created .intunewin file
                progress?.UpdateProgress(25, "Locating .intunewin file...");
                var intuneFolder = Path.Combine(packagePath, "Intune");
                var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
                if (intuneWinFiles.Length == 0)
                {
                    throw new Exception("No .intunewin file found after conversion.");
                }

                var intuneWinFile = intuneWinFiles[0];
                Debug.WriteLine($"✓ Found .intunewin file: {Path.GetFileName(intuneWinFile)}");

                // Step 3: Extract .intunewin metadata
                progress?.UpdateProgress(30, "Extracting .intunewin metadata...");
                var intuneWinInfo = ExtractIntuneWinInfo(intuneWinFile);

                // Step 4: Create Win32LobApp
                progress?.UpdateProgress(35, "Creating application in Intune...");
                var appId = await CreateWin32LobAppAsync(appInfo, installCommand, uninstallCommand, description, detectionRules, installContext, intuneWinInfo, iconPath);

                // Step 5: Create content version
                progress?.UpdateProgress(45, "Creating content version...");
                var contentVersionId = await CreateContentVersionAsync(appId);
                _currentAppId = appId;
                _currentContentVersionId = contentVersionId;

                // Step 6: Create file entry
                progress?.UpdateProgress(55, "Creating file entry...");
                var fileId = await CreateFileEntryAsync(appId, contentVersionId, intuneWinInfo);
                _currentFileId = fileId;

                // Step 7: Wait for Azure Storage URI
                progress?.UpdateProgress(65, "Getting Azure Storage URI...");
                var azureStorageInfo = await WaitForAzureStorageUriAsync(appId, contentVersionId, fileId);

                // Step 8: Upload file to Azure Storage
                progress?.UpdateProgress(75, "Uploading file to Azure Storage...");
                await UploadFileToAzureStorageAsync(azureStorageInfo.SasUri, intuneWinInfo.EncryptedFilePath, progress);

                // Step 9: Commit the file
                progress?.UpdateProgress(85, "Committing file...");
                await CommitFileAsync(appId, contentVersionId, fileId, intuneWinInfo.EncryptionInfo);

                // Step 10: Wait for file processing
                progress?.UpdateProgress(90, "Waiting for file processing...");
                await WaitForFileProcessingAsync(appId, contentVersionId, fileId, "CommitFile");

                // Step 11: Commit the app
                progress?.UpdateProgress(95, "Finalizing application...");
                await CommitAppAsync(appId, contentVersionId);

                // Step 12: Cleanup temp files
                CleanupTempFiles(intuneWinInfo);

                progress?.UpdateProgress(100, "Application uploaded successfully!");
                return appId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload application to Intune: {ex.Message}", ex);
            }
        }

        private async Task SignApplicationFilesAsync(string packagePath, IUploadProgress? progress)
        {
            try
            {
                var signer = new BatchFileSigner();

                // Check certificate availability
                if (!signer.ValidateCertificateAvailability())
                {
                    progress?.UpdateProgress(10, "Certificate not available - skipping file signing");
                    Debug.WriteLine("⚠️ Certificate not available for signing");
                    return;
                }

                progress?.UpdateProgress(10, "Certificate validated - starting file signing...");

                // Create progress handler for signing
                var signingProgress = new Progress<BatchFileSigner.SigningProgress>(signingUpdate =>
                {
                    if (signingUpdate.TotalFiles > 0)
                    {
                        var percentage = (int)((signingUpdate.ProcessedFiles * 100.0) / signingUpdate.TotalFiles);
                        var currentFile = Path.GetFileName(signingUpdate.CurrentFile);
                        progress?.UpdateProgress(10 + (percentage / 10), // Scale to 10-15% range
                            $"Signing files: {signingUpdate.ProcessedFiles}/{signingUpdate.TotalFiles} - {currentFile}");
                    }
                });

                // Sign all files in Application folder
                var result = await signer.SignApplicationFolderAsync(packagePath, signingProgress);

                var successful = result.Results.Count(r => r.Success);
                var failed = result.Results.Count(r => !r.Success);

                if (failed > 0)
                {
                    Debug.WriteLine($"⚠️ Signing completed with {failed} failures out of {result.Results.Count} files");
                    // Log failed files for debugging
                    foreach (var failedResult in result.Results.Where(r => !r.Success))
                    {
                        Debug.WriteLine($"  Failed: {Path.GetFileName(failedResult.FilePath)} - {failedResult.ErrorMessage}");
                    }
                }

                progress?.UpdateProgress(15, $"File signing complete: {successful} files signed successfully");
                Debug.WriteLine($"✓ File signing completed: {successful} successful, {failed} failed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠️ File signing failed: {ex.Message}");
                progress?.UpdateProgress(15, "File signing failed - continuing with upload");
                // Don't throw - signing is optional, continue with upload
            }
        }

        private async Task CreateIntuneWinFileAsync(string packagePath)
        {
            var converterPath = @"\\nbb.local\sys\SCCMData\TOOLS\IntunePackagingTool\IntuneWinAppUtil.exe";

            if (!File.Exists(converterPath))
            {
                throw new FileNotFoundException($"IntuneWinAppUtil.exe not found at: {converterPath}");
            }

            var applicationFolder = Path.Combine(packagePath, "Application");
            var setupFile = Path.Combine(applicationFolder, "Deploy-Application.exe");
            var outputFolder = Path.Combine(packagePath, "Intune");

            if (!Directory.Exists(applicationFolder))
            {
                throw new DirectoryNotFoundException($"Application folder not found: {applicationFolder}");
            }

            if (!File.Exists(setupFile))
            {
                throw new FileNotFoundException($"Deploy-Application.exe not found: {setupFile}");
            }

            Directory.CreateDirectory(outputFolder);

            var arguments = $"-c \"{applicationFolder}\" -s \"{setupFile}\" -o \"{outputFolder}\" -q";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = converterPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var errorMessage = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new Exception($"IntuneWinAppUtil failed with exit code {process.ExitCode}: {errorMessage}");
            }

            Debug.WriteLine($"✓ Created .intunewin file using: {converterPath} {arguments}");
        }

        private IntuneWinInfo ExtractIntuneWinInfo(string intuneWinFilePath)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                using (var archive = ZipFile.OpenRead(intuneWinFilePath))
                {

                    foreach (var entry in archive.Entries)
                    {
                        Debug.WriteLine($"  {entry.Name} ({entry.Length:N0} bytes)");
                    }

                    // Step 1: Find and extract detection.xml
                    var detectionEntry = archive.Entries.FirstOrDefault(e =>
                        e.Name.Equals("detection.xml", StringComparison.OrdinalIgnoreCase));

                    if (detectionEntry == null)
                    {
                        var availableFiles = string.Join(", ", archive.Entries.Select(e => e.Name));
                        throw new Exception($"detection.xml not found. Available files: {availableFiles}");
                    }

                    var detectionXmlPath = Path.Combine(tempDir, "detection.xml");
                    detectionEntry.ExtractToFile(detectionXmlPath);


                    // Step 2: Parse detection.xml
                    var xmlContent = File.ReadAllText(detectionXmlPath);


                    var detectionXml = XDocument.Load(detectionXmlPath);
                    var appInfo = detectionXml.Root;

                    if (appInfo == null || !appInfo.Name.LocalName.Equals("ApplicationInfo", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception($"Root element is not ApplicationInfo. Found: {appInfo?.Name}");
                    }

                    var encryptionInfo = appInfo.Elements().FirstOrDefault(e => e.Name.LocalName == "EncryptionInfo");
                    if (encryptionInfo == null)
                    {
                        var availableElements = string.Join(", ", appInfo.Elements().Select(e => e.Name.LocalName));
                        throw new Exception($"EncryptionInfo not found. Available elements: {availableElements}");
                    }

                    Debug.WriteLine("✓ Found ApplicationInfo and EncryptionInfo");

                    // Step 3: Find the encrypted content file using multiple strategies
                    ZipArchiveEntry? contentEntry = null;
                    string contentStrategy = "";

                    // Strategy 1: Look for common .dat file names
                    var commonDatNames = new[] { "Contents.dat", "IntunePackage.dat", "contents.dat", "intunepackage.dat" };
                    foreach (var datName in commonDatNames)
                    {
                        contentEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals(datName, StringComparison.OrdinalIgnoreCase));
                        if (contentEntry != null)
                        {
                            contentStrategy = $"Found by common .dat name: {datName}";
                            break;
                        }
                    }

                    // Strategy 2: Look for .intunewin files inside the archive (newer format)
                    if (contentEntry == null)
                    {
                        contentEntry = archive.Entries.FirstOrDefault(e =>
                            e.Name.EndsWith(".intunewin", StringComparison.OrdinalIgnoreCase) &&
                            e.Length > 1000); // Must be substantial size (not just metadata)

                        if (contentEntry != null)
                        {
                            contentStrategy = $"Found .intunewin content file: {contentEntry.Name}";
                        }
                    }

                    // Strategy 3: Look for any .dat file
                    if (contentEntry == null)
                    {
                        contentEntry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));
                        if (contentEntry != null)
                        {
                            contentStrategy = $"Found .dat file: {contentEntry.Name}";
                        }
                    }

                    // Strategy 4: Look for the largest non-XML file (likely the encrypted content)
                    if (contentEntry == null)
                    {
                        contentEntry = archive.Entries
                            .Where(e => !e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(e => e.Length)
                            .FirstOrDefault();

                        if (contentEntry != null)
                        {
                            contentStrategy = $"Found largest non-XML file: {contentEntry.Name}";
                        }
                    }

                    // Strategy 5: Look for file matching the FileName from XML (without .intunewin extension)
                    if (contentEntry == null)
                    {
                        var fileNameFromXml = GetElementValue(appInfo, "FileName");
                        if (!string.IsNullOrEmpty(fileNameFromXml))
                        {
                            // Try the filename as-is, and also try replacing .intunewin with other extensions
                            var possibleNames = new[]
                            {
                        fileNameFromXml,
                        Path.GetFileNameWithoutExtension(fileNameFromXml) + ".dat",
                        Path.GetFileNameWithoutExtension(fileNameFromXml),
                    };

                            foreach (var possibleName in possibleNames)
                            {
                                contentEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals(possibleName, StringComparison.OrdinalIgnoreCase));
                                if (contentEntry != null)
                                {
                                    contentStrategy = $"Found by FileName reference: {possibleName}";
                                    break;
                                }
                            }
                        }
                    }

                    if (contentEntry == null)
                    {
                        var fileList = string.Join("\n  ", archive.Entries.Select(e => $"{e.Name} ({e.Length:N0} bytes)"));
                        throw new Exception($"Could not find encrypted content file in archive.\n\nAvailable files:\n  {fileList}");
                    }

                    Debug.WriteLine($"✓ {contentStrategy}");

                    // Step 4: Extract the content file
                    var encryptedFilePath = Path.Combine(tempDir, contentEntry.Name);
                    contentEntry.ExtractToFile(encryptedFilePath);
                    Debug.WriteLine($"✓ Extracted content file: {contentEntry.Name} ({contentEntry.Length:N0} bytes)");

                    // Step 5: Extract metadata using helper function
                    string GetElementValue(XElement parent, string localName)
                    {
                        var element = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
                        var value = element?.Value?.Trim();

                        if (string.IsNullOrWhiteSpace(value))
                        {
                            Debug.WriteLine($"⚠ Element '{localName}' is empty or missing");
                            return "";
                        }

                        Debug.WriteLine($"✓ Element '{localName}': {value}");
                        return value;
                    }

                    var fileName = GetElementValue(appInfo, "FileName");
                    if (string.IsNullOrEmpty(fileName))
                    {
                        fileName = Path.GetFileName(intuneWinFilePath);
                        Debug.WriteLine($"Using fallback filename: {fileName}");
                    }

                    var unencryptedSizeStr = GetElementValue(appInfo, "UnencryptedContentSize");
                    if (!long.TryParse(unencryptedSizeStr, out var unencryptedSize))
                    {
                        Debug.WriteLine($"⚠ Could not parse UnencryptedContentSize: '{unencryptedSizeStr}', using actual content file size");
                        unencryptedSize = contentEntry.Length;
                    }

                    var result = new IntuneWinInfo
                    {
                        FileName = fileName,
                        UnencryptedContentSize = unencryptedSize,
                        EncryptedFilePath = encryptedFilePath,
                        TempDirectory = tempDir,
                        EncryptionInfo = new EncryptionInfo
                        {
                            EncryptionKey = GetElementValue(encryptionInfo, "EncryptionKey"),
                            MacKey = GetElementValue(encryptionInfo, "MacKey") ?? GetElementValue(encryptionInfo, "macKey"),
                            InitializationVector = GetElementValue(encryptionInfo, "InitializationVector") ?? GetElementValue(encryptionInfo, "initializationVector"),
                            Mac = GetElementValue(encryptionInfo, "Mac") ?? GetElementValue(encryptionInfo, "mac"),
                            ProfileIdentifier = GetElementValue(encryptionInfo, "ProfileIdentifier") ?? "ProfileVersion1",
                            FileDigest = GetElementValue(encryptionInfo, "FileDigest") ?? GetElementValue(encryptionInfo, "fileDigest"),
                            FileDigestAlgorithm = GetElementValue(encryptionInfo, "FileDigestAlgorithm") ?? GetElementValue(encryptionInfo, "fileDigestAlgorithm") ?? "SHA256"
                        }
                    };

                    // Step 6: Validate essential data
                    if (string.IsNullOrEmpty(result.EncryptionInfo.EncryptionKey))
                    {
                        throw new Exception("EncryptionKey is missing from detection.xml");
                    }

                    if (!File.Exists(result.EncryptedFilePath))
                    {
                        throw new Exception($"Encrypted content file was not extracted properly: {result.EncryptedFilePath}");
                    }

                    return result;
                }
            }
            catch
            {
                // Clean up temp directory if extraction fails
                if (Directory.Exists(tempDir))
                {
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
                throw;
            }
        }

        private async Task<string> CreateWin32LobAppAsync(
            ApplicationInfo appInfo,
            string installCommand,
            string uninstallCommand,
            string description,
            List<DetectionRule> detectionRules,
            string installContext,
            IntuneWinInfo intuneWinInfo,
            string? iconPath = null)
        {
            var formattedDetectionRules = new List<Dictionary<string, object>>();

            foreach (var rule in detectionRules)
            {
                var formattedRule = ConvertDetectionRuleForBetaAPI(rule);
                if (formattedRule != null)
                {
                    formattedDetectionRules.Add(formattedRule);
                }
            }

            if (formattedDetectionRules.Count == 0)
            {
                // Add default detection rule if none provided
                formattedDetectionRules.Add(new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.win32LobAppFileSystemDetection",
                    ["path"] = "%ProgramFiles%",
                    ["fileOrFolderName"] = $"{appInfo.Name}.exe",
                    ["check32BitOn64System"] = false,
                    ["detectionType"] = "exists"
                });
            }

            var createAppPayload = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.win32LobApp",
                ["displayName"] = $"{appInfo.Manufacturer} {appInfo.Name} {appInfo.Version}",
                ["description"] = description,
                ["publisher"] = appInfo.Manufacturer,
                ["displayVersion"] = appInfo.Version,
                ["installCommandLine"] = installCommand,
                ["uninstallCommandLine"] = uninstallCommand,
                ["applicableArchitectures"] = "x64",
                ["fileName"] = intuneWinInfo.FileName,
                ["setupFilePath"] = "Deploy-Application.exe",
                ["informationUrl"] = "https://servicemanagement.nbb.be/nbb_portal",
                ["privacyInformationUrl"] = "https://servicemanagement.nbb.be/nbb_portal",
                ["installExperience"] = new Dictionary<string, object>
                {
                    ["runAsAccount"] = installContext,
                    ["deviceRestartBehavior"] = "allow"
                },


                ["detectionRules"] = formattedDetectionRules.ToArray(),
                ["returnCodes"] = new[]
                {
                    new Dictionary<string, object> { ["returnCode"] = 0, ["type"] = "success" },
                    new Dictionary<string, object> { ["returnCode"] = 3010, ["type"] = "softReboot" },
                    new Dictionary<string, object> { ["returnCode"] = 1641, ["type"] = "hardReboot" },
                    new Dictionary<string, object> { ["returnCode"] = 1618, ["type"] = "retry" }
                }
            };

            // In CreateWin32LobAppAsync method, enhance the icon handling section:




            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    var fileInfo = new FileInfo(iconPath);
                    Debug.WriteLine($"Icon file exists: true");
                    Debug.WriteLine($"Icon file size: {fileInfo.Length} bytes");

                    var iconData = ConvertIconToBase64(iconPath);
                    if (iconData != null)
                    {
                        createAppPayload["largeIcon"] = iconData;
                        Debug.WriteLine($"✓ Added icon to payload");

                        // Verify the structure
                        var iconJson = JsonSerializer.Serialize(iconData);
                        Debug.WriteLine($"Icon structure: {iconJson}");
                    }
                    else
                    {
                        Debug.WriteLine("❌ ConvertIconToBase64 returned null");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"⚠ Failed to add icon: {ex.Message}");
                    // Continue without icon - not a critical failure
                }
            }
            else
            {
                if (string.IsNullOrEmpty(iconPath))
                {
                    Debug.WriteLine("⚠ No icon path provided");
                }
                else
                {
                    Debug.WriteLine($"❌ Icon file not found: {iconPath}");
                }
            }

            // Log the final JSON to verify icon is included
            var json = JsonSerializer.Serialize(createAppPayload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            });


            if (json.Contains("\"largeIcon\""))
            {
                // Extract just the largeIcon part for verification (first 100 chars of value)
                var iconIndex = json.IndexOf("\"largeIcon\"");
                if (iconIndex > 0)
                {
                    var iconSection = json.Substring(iconIndex, Math.Min(500, json.Length - iconIndex));

                }
            }
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.PostAsync("https://graph.microsoft.com/beta/deviceAppManagement/mobileApps", content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create Win32 app. Status: {response.StatusCode}, Response: {responseText}");
            }

            var createdApp = JsonSerializer.Deserialize<JsonElement>(responseText);
            var appId = createdApp.GetProperty("id").GetString();


            try
            {
                await _intuneService.AssignTestCategoryToAppAsync(appId!);

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed to assign Test category: {ex.Message}");
                // Don't fail the entire upload if category assignment fails
            }

            return appId ?? throw new Exception("App ID not returned from creation");
        }

        private Dictionary<string, object>? ConvertIconToBase64(string iconPath)
        {
            try
            {
                Debug.WriteLine($"=== CONVERTING ICON ===");
                Debug.WriteLine($"Icon path: {iconPath}");

                // Read the icon file
                var iconBytes = File.ReadAllBytes(iconPath);
                Debug.WriteLine($"Icon size: {iconBytes.Length} bytes");

                // If icon is too large (> 500KB), you might need to resize it
                if (iconBytes.Length > 500 * 1024)
                {
                    Debug.WriteLine($"⚠ Icon is large ({iconBytes.Length} bytes), may fail to upload");
                }

                // Convert to base64
                var base64String = Convert.ToBase64String(iconBytes);
                Debug.WriteLine($"Base64 length: {base64String.Length} characters");

                // Determine MIME type based on file extension
                var extension = Path.GetExtension(iconPath).ToLower();
                var mimeType = extension switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".ico" => "image/x-icon",
                    _ => "image/png" // Default to PNG
                };

                Debug.WriteLine($"MIME type: {mimeType}");



                var iconData = new Dictionary<string, object>
                {
                    ["type"] = mimeType,
                    ["value"] = base64String
                };

                Debug.WriteLine("✓ Icon converted successfully");
                return iconData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error converting icon to base64: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private async Task<string> CreateContentVersionAsync(string appId)
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions";
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create content version. Status: {response.StatusCode}, Response: {responseText}");
            }

            var contentVersion = JsonSerializer.Deserialize<JsonElement>(responseText);
            var contentVersionId = contentVersion.GetProperty("id").GetString();

            Debug.WriteLine($"✓ Created content version. ID: {contentVersionId}");
            return contentVersionId ?? throw new Exception("Content version ID not returned");
        }

        private async Task<string> CreateFileEntryAsync(string appId, string contentVersionId, IntuneWinInfo intuneWinInfo)
        {
            var encryptedSize = new FileInfo(intuneWinInfo.EncryptedFilePath).Length;



            var fileBody = new Dictionary<string, object?>
            {
                ["@odata.type"] = "#microsoft.graph.mobileAppContentFile",
                ["name"] = intuneWinInfo.FileName,
                ["size"] = intuneWinInfo.UnencryptedContentSize,
                ["sizeEncrypted"] = encryptedSize,
                ["manifest"] = (object?)null,
                ["isDependency"] = false
            };

            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files";
            var json = JsonSerializer.Serialize(fileBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });



            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create file entry. Status: {response.StatusCode}, Response: {responseText}");
            }

            var fileEntry = JsonSerializer.Deserialize<JsonElement>(responseText);
            var fileId = fileEntry.GetProperty("id").GetString();


            return fileId ?? throw new Exception("File ID not returned");
        }

        private async Task<AzureStorageInfo> WaitForAzureStorageUriAsync(string appId, string contentVersionId, string fileId)
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";



            for (int attempts = 0; attempts < 120; attempts++) // 20 minutes total
            {
                try
                {
                    var response = await sharedHttpClient!.GetAsync(url);
                    var responseText = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"❌ HTTP Error {response.StatusCode}: {responseText}");
                        throw new Exception($"Failed to get file info. Status: {response.StatusCode}, Response: {responseText}");
                    }

                    var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

                    // Log the full response for debugging
                    Debug.WriteLine($"Attempt {attempts + 1}: Response = {responseText}");

                    if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
                    {
                        Debug.WriteLine($"⚠ No uploadState property found in response");
                        await Task.Delay(10000); // Wait 10 seconds
                        continue;
                    }

                    var uploadState = uploadStateProp.GetString() ?? "";
                    Debug.WriteLine($"Attempt {attempts + 1}: Upload state = '{uploadState}'");


                    if (uploadState.Equals("AzureStorageUriRequestSuccess", StringComparison.OrdinalIgnoreCase) ||
                        uploadState.Equals("azureStorageUriRequestSuccess", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
                        {
                            var azureStorageUri = azureStorageUriProp.GetString();
                            Debug.WriteLine($"✅ Got Azure Storage URI after {attempts + 1} attempts");
                            Debug.WriteLine($"URI length: {azureStorageUri?.Length ?? 0}");
                            return new AzureStorageInfo { SasUri = azureStorageUri ?? throw new Exception("Azure Storage URI is null") };
                        }
                        else
                        {
                            Debug.WriteLine($"❌ Success state but no azureStorageUri property found");
                            throw new Exception("Upload state is success but azureStorageUri is missing");
                        }
                    }


                    if (uploadState.Equals("AzureStorageUriRequestPending", StringComparison.OrdinalIgnoreCase) ||
                        uploadState.Equals("azureStorageUriRequestPending", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"⏳ Still pending... (attempt {attempts + 1}/120) - waiting 10 seconds");
                        await Task.Delay(10000);
                        continue;
                    }

                    // Handle failure states
                    if (uploadState.Equals("AzureStorageUriRequestFailed", StringComparison.OrdinalIgnoreCase) ||
                        uploadState.Equals("azureStorageUriRequestFailed", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"❌ Azure Storage URI request failed");
                        throw new Exception("Azure Storage URI request failed");
                    }

                    if (uploadState.Equals("AzureStorageUriRequestTimedOut", StringComparison.OrdinalIgnoreCase) ||
                        uploadState.Equals("azureStorageUriRequestTimedOut", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"❌ Azure Storage URI request timed out");
                        throw new Exception("Azure Storage URI request timed out");
                    }


                    Debug.WriteLine($"❓ Unknown upload state: '{uploadState}' - will wait and retry");


                    if (attempts < 115) // 
                    {
                        await Task.Delay(15000);
                        continue;
                    }
                    else
                    {
                        // After many attempts with unknown state, fail
                        Debug.WriteLine($"❌ Giving up after {attempts + 1} attempts with unknown state: '{uploadState}'");
                        throw new Exception($"Unknown upload state after many attempts: '{uploadState}'. Check Intune admin center for app status.");
                    }
                }
                catch (Exception ex) when (!(ex.Message.Contains("upload state") || ex.Message.Contains("Failed to get file info")))
                {
                    Debug.WriteLine($"⚠ Network exception on attempt {attempts + 1}: {ex.Message}");

                    // For network errors, retry a few times
                    if (attempts < 115)
                    {
                        await Task.Delay(10000);
                        continue;
                    }
                    throw;
                }
            }

            Debug.WriteLine($"❌ Timeout after 120 attempts (20 minutes)");
            throw new Exception("Timeout waiting for Azure Storage URI after 20 minutes. The application was created in Intune but file upload preparation timed out. Check the Intune admin center - the app may still be processing.");
        }

        private async Task UploadFileToAzureStorageAsync(string sasUri, string filePath, IUploadProgress? progress = null)
        {
            // Determine optimal chunk size based on file size
            var fileInfo = new FileInfo(filePath);
            var totalSize = fileInfo.Length;

            // Use smaller chunks for very large files to avoid timeouts
            int chunkSize;
            if (totalSize > 5L * 1024 * 1024 * 1024) // > 5GB
                chunkSize = 4 * 1024 * 1024; // 4MB chunks
            else
                chunkSize = 6 * 1024 * 1024; // 6MB chunks (default)

            var totalChunks = (int)Math.Ceiling((double)totalSize / chunkSize);

            Debug.WriteLine($"=== AZURE STORAGE UPLOAD ===");
            Debug.WriteLine($"File: {Path.GetFileName(filePath)} ({FormatBytes(totalSize)})");
            Debug.WriteLine($"Using chunk size: {FormatBytes(chunkSize)}");
            Debug.WriteLine($"Total chunks: {totalChunks}");

            progress?.UpdateProgress(65, $"Starting upload: {Path.GetFileName(filePath)} ({FormatBytes(totalSize)})");

            using var azureHttpClient = new HttpClient();
            azureHttpClient.Timeout = TimeSpan.FromMinutes(10);

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var blockIds = new List<string>();
            var sasRenewalTimer = System.Diagnostics.Stopwatch.StartNew();
            var currentSasUri = sasUri;

            for (int chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++)
            {
                var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(chunkIndex.ToString("0000")));
                blockIds.Add(blockId);

                // FIXED: Calculate the actual bytes to read for this chunk
                long startPosition = (long)chunkIndex * chunkSize;
                int bytesToRead = (int)Math.Min(chunkSize, totalSize - startPosition);

                var buffer = new byte[bytesToRead];
                var totalBytesRead = 0;

                // Ensure we're at the correct position in the file
                fileStream.Position = startPosition;

                while (totalBytesRead < buffer.Length)
                {
                    var bytesRead = await fileStream.ReadAsync(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        // This should only happen if we've calculated wrong
                        Debug.WriteLine($"WARNING: End of file at chunk {chunkIndex + 1}, read {totalBytesRead} of {buffer.Length} bytes");
                        break;
                    }
                    totalBytesRead += bytesRead;
                }

                // Verify we read the expected amount
                if (totalBytesRead != buffer.Length)
                {
                    Debug.WriteLine($"❌ Chunk {chunkIndex + 1}: Expected {buffer.Length} bytes, got {totalBytesRead} bytes");
                    // Resize buffer to actual bytes read
                    Array.Resize(ref buffer, totalBytesRead);
                }

                // Update progress
                var percentComplete = (int)(((long)chunkIndex * 100) / totalChunks);
                var progressPercentage = 65 + (int)((chunkIndex + 1.0) / totalChunks * 15);
                progress?.UpdateProgress(progressPercentage,
                    $"Uploading chunk {chunkIndex + 1}/{totalChunks} ({percentComplete}%)");

                // SAS renewal every 7 minutes
                if (chunkIndex < totalChunks - 1 && sasRenewalTimer.ElapsedMilliseconds >= 420000)
                {
                    progress?.UpdateProgress(progressPercentage, "Renewing SAS token...");
                    try
                    {
                        currentSasUri = await RenewSasUriAsync();
                        sasRenewalTimer.Restart();
                        Debug.WriteLine("✅ SAS token renewed successfully");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"⚠️ SAS renewal failed, continuing with current token: {ex.Message}");
                    }
                }

                // Upload chunk with retry logic
                var chunkUri = $"{currentSasUri}&comp=block&blockid={blockId}";
                await UploadChunkWithRetryAsync(azureHttpClient, chunkUri, buffer, chunkIndex, totalChunks);

                // Log successful chunk upload
                Debug.WriteLine($"✅ Uploaded chunk {chunkIndex + 1}/{totalChunks}");
            }

            // Commit blocks with retry
            progress?.UpdateProgress(82, "Committing blocks to Azure Storage...");
            await CommitBlockListWithRetryAsync(azureHttpClient, currentSasUri, blockIds);

            progress?.UpdateProgress(84, "File uploaded successfully to Azure Storage");
            Debug.WriteLine($"✅ Successfully uploaded file to Azure Storage");
        }

        // Updated helper method with better error handling
        private async Task UploadChunkWithRetryAsync(HttpClient client, string chunkUri, byte[] buffer,
            int chunkIndex, int totalChunks)
        {
            const int maxRetries = 5;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // Create new request for each attempt
                    using var request = new HttpRequestMessage(HttpMethod.Put, chunkUri);
                    request.Content = new ByteArrayContent(buffer);
                    request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain")
                    {
                        CharSet = "iso-8859-1"
                    };
                    request.Headers.Add("x-ms-blob-type", "BlockBlob");

                    // Use longer timeout for last chunk and larger chunks
                    var timeout = chunkIndex == totalChunks - 1 ?
                        TimeSpan.FromMinutes(15) :
                        TimeSpan.FromMinutes(5 + attempt); // Increase timeout with each retry

                    using var cts = new CancellationTokenSource(timeout);

                    var response = await client.SendAsync(request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        if (attempt > 1)
                            Debug.WriteLine($"✓ Chunk {chunkIndex + 1} succeeded on attempt {attempt}");
                        return;
                    }

                    var errorText = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"⚠ Chunk {chunkIndex + 1} failed (attempt {attempt}/{maxRetries}): {response.StatusCode}");
                    Debug.WriteLine($"  Error details: {errorText}");

                    // Don't retry client errors (4xx) except for 408 (timeout) and 429 (throttled)
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                        && response.StatusCode != System.Net.HttpStatusCode.RequestTimeout
                        && (int)response.StatusCode != 429)
                    {
                        throw new Exception($"Client error: {response.StatusCode} - {errorText}");
                    }
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"⚠ Chunk {chunkIndex + 1} timed out (attempt {attempt}/{maxRetries})");
                }
                catch (HttpRequestException ex)
                {
                    Debug.WriteLine($"⚠ Network error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Unexpected error on chunk {chunkIndex + 1} (attempt {attempt}/{maxRetries}): {ex.Message}");
                    if (attempt == maxRetries)
                        throw;
                }

                if (attempt < maxRetries)
                {
                    // Exponential backoff with jitter: 2-4s, 4-8s, 8-16s, 16-32s
                    var baseDelay = Math.Pow(2, attempt);
                    var jitter = new Random().NextDouble(); // 0.0 to 1.0
                    var delay = TimeSpan.FromSeconds(baseDelay + baseDelay * jitter);

                    Debug.WriteLine($"⏳ Waiting {delay.TotalSeconds:F1}s before retry...");
                    await Task.Delay(delay);
                }
                else
                {
                    throw new Exception($"Failed to upload chunk {chunkIndex + 1} after {maxRetries} attempts");
                }
            }
        }

        // Block list commit stays the same
        private async Task CommitBlockListWithRetryAsync(HttpClient client, string sasUri, List<string> blockIds)
        {
            var blockListXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><BlockList>";
            foreach (var blockId in blockIds)
            {
                blockListXml += $"<Latest>{blockId}</Latest>";
            }
            blockListXml += "</BlockList>";

            const int maxRetries = 5;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var finalizeUri = $"{sasUri}&comp=blocklist";
                    using var request = new HttpRequestMessage(HttpMethod.Put, finalizeUri);
                    request.Content = new StringContent(blockListXml, Encoding.UTF8);
                    request.Content.Headers.ContentType = null; // Important: no Content-Type for block list

                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
                    var response = await client.SendAsync(request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"✅ Block list committed successfully (attempt {attempt})");
                        return;
                    }

                    var errorText = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"⚠ Block list commit failed (attempt {attempt}/{maxRetries}): {response.StatusCode}");
                    Debug.WriteLine($"  Error: {errorText}");
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine($"⚠ Block list commit timed out (attempt {attempt}/{maxRetries})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"❌ Error committing block list (attempt {attempt}/{maxRetries}): {ex.Message}");
                }

                if (attempt < maxRetries)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
                    Debug.WriteLine($"⏳ Waiting {delay.TotalSeconds}s before retry...");
                    await Task.Delay(delay);
                }
                else
                {
                    throw new Exception($"Failed to commit block list after {maxRetries} attempts");
                }
            }
        }
        private string FormatBytes(long bytes)
        {
            if (bytes >= 1073741824)
                return $"{bytes / 1073741824.0:F2} GB";
            if (bytes >= 1048576)
                return $"{bytes / 1048576.0:F2} MB";
            if (bytes >= 1024)
                return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} bytes";
        }

        private async Task CommitFileAsync(string appId, string contentVersionId, string fileId, EncryptionInfo encryptionInfo)
        {


            // Check for empty/null values
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(encryptionInfo.EncryptionKey)) issues.Add("EncryptionKey is empty");
            if (string.IsNullOrWhiteSpace(encryptionInfo.MacKey)) issues.Add("MacKey is empty");
            if (string.IsNullOrWhiteSpace(encryptionInfo.InitializationVector)) issues.Add("InitializationVector is empty");
            if (string.IsNullOrWhiteSpace(encryptionInfo.Mac)) issues.Add("Mac is empty");
            if (string.IsNullOrWhiteSpace(encryptionInfo.FileDigest)) issues.Add("FileDigest is empty");

            if (issues.Any())
            {
                Console.WriteLine("❌ ENCRYPTION ISSUES FOUND:");
                foreach (var issue in issues)
                {
                    Console.WriteLine($"   - {issue}");
                }
            }
            else
            {
                Console.WriteLine("✅ All encryption fields appear populated");
            }

            var commitBody = new Dictionary<string, object>
            {
                ["fileEncryptionInfo"] = new Dictionary<string, object>
                {
                    ["encryptionKey"] = encryptionInfo.EncryptionKey ?? "",
                    ["macKey"] = encryptionInfo.MacKey ?? "",
                    ["initializationVector"] = encryptionInfo.InitializationVector ?? "",
                    ["mac"] = encryptionInfo.Mac ?? "",
                    ["profileIdentifier"] = encryptionInfo.ProfileIdentifier ?? "ProfileVersion1",
                    ["fileDigest"] = encryptionInfo.FileDigest ?? "",
                    ["fileDigestAlgorithm"] = encryptionInfo.FileDigestAlgorithm ?? "SHA256"
                }
            };

            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}/commit";
            var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true  // Make it readable
            });


            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await sharedHttpClient!.PostAsync(url, content);
                var responseText = await response.Content.ReadAsStringAsync();



                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to commit file. Status: {response.StatusCode}, Response: {responseText}");
                }

                Console.WriteLine($"✅ File committed successfully");
            }
            catch (Exception ex)
            {

                throw new Exception($"Failed to upload application to Intune: {ex.Message}", ex);
            }
        }

        private async Task WaitForFileProcessingAsync(string appId, string contentVersionId, string fileId, string stage)
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/microsoft.graph.win32LobApp/contentVersions/{contentVersionId}/files/{fileId}";
            var successState = $"{stage}Success";
            var pendingState = $"{stage}Pending";

            Debug.WriteLine($"=== WAITING FOR FILE PROCESSING: {stage} ===");
            Debug.WriteLine($"Expected success state: {successState}");
            Debug.WriteLine($"Expected pending state: {pendingState}");

            for (int attempts = 0; attempts < 120; attempts++)
            {
                var response = await sharedHttpClient!.GetAsync(url);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to get file processing status. Status: {response.StatusCode}, Response: {responseText}");
                }

                var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

                if (!fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
                {
                    Debug.WriteLine($"⚠ No uploadState property found, attempt {attempts + 1}");
                    await Task.Delay(5000);
                    continue;
                }

                var uploadState = uploadStateProp.GetString() ?? "";
                Debug.WriteLine($"Attempt {attempts + 1}: Upload state = '{uploadState}'");


                if (uploadState.Equals(successState, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"✓ File processing completed for stage: {stage}");
                    return;
                }

                if (uploadState.Equals(pendingState, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"⏳ Still processing... (attempt {attempts + 1}/120) - waiting 5 seconds");
                    await Task.Delay(5000); // Wait 5 seconds
                    continue;
                }

                // Handle failure states with case-insensitive checks
                if (uploadState.Equals($"{stage}Failed", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"❌ File processing failed for stage: {stage}");
                    throw new Exception($"File processing failed for stage: {stage}. State: {uploadState}");
                }

                if (uploadState.Equals($"{stage}TimedOut", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"❌ File processing timed out for stage: {stage}");
                    throw new Exception($"File processing timed out for stage: {stage}. State: {uploadState}");
                }

                // For unknown states, wait a bit and try again
                Debug.WriteLine($"❓ Unknown upload state: '{uploadState}' - will wait and retry");

                if (attempts < 115) // Give more chances for unknown states
                {
                    await Task.Delay(10000); // Wait 10 seconds for unknown states
                    continue;
                }
                else
                {
                    Debug.WriteLine($"❌ Giving up after {attempts + 1} attempts with unknown state: '{uploadState}'");
                    throw new Exception($"Unknown file processing state after many attempts: '{uploadState}'. Check Intune admin center.");
                }
            }

            throw new Exception($"Timeout waiting for file processing stage: {stage} after 10 minutes");
        }
        private async Task CommitAppAsync(string appId, string contentVersionId)
        {
            var commitBody = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.win32LobApp",
                ["committedContentVersion"] = contentVersionId
            };

            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
            var json = JsonSerializer.Serialize(commitBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.PatchAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to commit app. Status: {response.StatusCode}, Response: {responseText}");
            }

            Debug.WriteLine($"✓ App committed successfully");
        }

        private void CleanupTempFiles(IntuneWinInfo intuneWinInfo)
        {
            try
            {
                if (Directory.Exists(intuneWinInfo.TempDirectory))
                {
                    Directory.Delete(intuneWinInfo.TempDirectory, true);
                    Debug.WriteLine($"✓ Cleaned up temp files: {intuneWinInfo.TempDirectory}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Failed to cleanup temp files: {ex.Message}");
            }
        }

        private Dictionary<string, object>? ConvertDetectionRuleForBetaAPI(DetectionRule rule)
        {
            switch (rule.Type)
            {
                case DetectionRuleType.File:
                    return ConvertFileDetectionForBetaAPI(rule);

                case DetectionRuleType.Registry:
                    return ConvertRegistryDetectionForBetaAPI(rule);

                case DetectionRuleType.MSI:
                    return ConvertMsiDetectionForBetaAPI(rule);

                default:
                    Debug.WriteLine($"⚠ Cannot convert {rule.Type} detection rule for beta API yet");
                    return null;
            }
        }

        private Dictionary<string, object> ConvertFileDetectionForBetaAPI(DetectionRule rule)
        {
            var fileRule = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.win32LobAppFileSystemDetection",
                ["path"] = rule.Path.Trim(),
                ["fileOrFolderName"] = rule.FileOrFolderName.Trim(),
                ["check32BitOn64System"] = false
            };

            if (rule.CheckVersion)
            {
                fileRule["detectionType"] = "version";
                fileRule["operator"] = "greaterThanOrEqual"; // Default operator
                fileRule["detectionValue"] = "1.0.0"; // Default version - you can enhance this later
            }
            else
            {
                fileRule["detectionType"] = "exists";
            }

            return fileRule;
        }

        private Dictionary<string, object> ConvertRegistryDetectionForBetaAPI(DetectionRule rule)
        {
            var registryRule = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.win32LobAppRegistryDetection",
                ["keyPath"] = rule.Path.Trim(), // Use Path property for registry key
                ["check32BitOn64System"] = false
            };

            // If FileOrFolderName is set, it's the registry value name
            if (!string.IsNullOrEmpty(rule.FileOrFolderName))
            {
                registryRule["valueName"] = rule.FileOrFolderName.Trim();
                registryRule["detectionType"] = "exists"; // Default to exists check
            }
            else
            {
                registryRule["detectionType"] = "exists"; // Key exists check
            }

            return registryRule;
        }

        private Dictionary<string, object> ConvertMsiDetectionForBetaAPI(DetectionRule rule)
        {
            var msiRule = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.win32LobAppProductCodeDetection",
                ["productCode"] = rule.Path.Trim(), // MSI product code stored in Path
                ["productVersionOperator"] = "notConfigured"
            };

            // If CheckVersion is true and FileOrFolderName contains version info
            if (rule.CheckVersion && !string.IsNullOrEmpty(rule.FileOrFolderName))
            {
                // Parse the version info stored as "operator:version"
                var versionParts = rule.FileOrFolderName.Split(':');
                if (versionParts.Length == 2)
                {
                    var operatorText = versionParts[0].Trim();
                    var versionValue = versionParts[1].Trim();

                    // Convert operator text to API format
                    var apiOperator = operatorText switch
                    {
                        "Greater than or equal to" => "greaterThanOrEqual",
                        "Equal to" => "equal",
                        "Greater than" => "greaterThan",
                        "Less than" => "lessThan",
                        "Less than or equal to" => "lessThanOrEqual",
                        _ => "greaterThanOrEqual"
                    };

                    msiRule["productVersionOperator"] = apiOperator;
                    msiRule["productVersion"] = versionValue;
                }
            }

            return msiRule;
        }

        private async Task<string> RenewSasUriAsync()
        {
            var renewUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}/renewUpload";

            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await sharedHttpClient!.PostAsync(renewUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var responseText = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to renew SAS URI. Status: {response.StatusCode}, Response: {responseText}");
            }

            // Wait for the renewal to complete and get new URI
            return await WaitForNewSasUriAfterRenewal();
        }

        private async Task<string> WaitForNewSasUriAfterRenewal()
        {
            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{_currentAppId}/microsoft.graph.win32LobApp/contentVersions/{_currentContentVersionId}/files/{_currentFileId}";

            for (int attempts = 0; attempts < 30; attempts++) // 5 minutes max
            {
                var response = await sharedHttpClient!.GetAsync(url);
                var responseText = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to get renewed SAS URI. Status: {response.StatusCode}, Response: {responseText}");
                }

                var fileInfo = JsonSerializer.Deserialize<JsonElement>(responseText);

                if (fileInfo.TryGetProperty("uploadState", out var uploadStateProp))
                {
                    var uploadState = uploadStateProp.GetString() ?? "";

                    if (uploadState.Equals("AzureStorageUriRenewalSuccess", StringComparison.OrdinalIgnoreCase))
                    {
                        if (fileInfo.TryGetProperty("azureStorageUri", out var azureStorageUriProp))
                        {
                            var newSasUri = azureStorageUriProp.GetString();
                            if (!string.IsNullOrEmpty(newSasUri))
                            {
                                Debug.WriteLine("✓ Successfully renewed SAS URI");
                                return newSasUri;
                            }
                        }
                    }
                }

                await Task.Delay(10000); // Wait 10 seconds
            }

            throw new Exception("Timeout waiting for SAS URI renewal");
        }
        private async Task UpdateCommittedContentVersionAsync(string appId, string contentVersionId)
        {
            try
            {
                // Step 1: Get the existing app to preserve important fields like the icon
                Debug.WriteLine($"Fetching existing app metadata for {appId}...");
                var getUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
                var getResponse = await sharedHttpClient!.GetAsync(getUrl);

                if (!getResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to fetch existing app: {getResponse.StatusCode}");
                }

                var existingAppJson = await getResponse.Content.ReadAsStringAsync();
                var existingApp = JsonSerializer.Deserialize<JsonElement>(existingAppJson);

                // Step 2: Build update with preserved fields
                var updateBody = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.win32LobApp",
                    ["committedContentVersion"] = contentVersionId
                };

                // ✅ Preserve the icon if it exists
                if (existingApp.TryGetProperty("largeIcon", out var iconProp) &&
                    iconProp.ValueKind != JsonValueKind.Null)
                {
                    // Convert the icon object to a dictionary
                    var iconDict = new Dictionary<string, object>();

                    if (iconProp.TryGetProperty("type", out var typeProp))
                        iconDict["type"] = typeProp.GetString() ?? "";

                    if (iconProp.TryGetProperty("value", out var valueProp))
                        iconDict["value"] = valueProp.GetString() ?? "";

                    updateBody["largeIcon"] = iconDict;
                    Debug.WriteLine("✅ Preserved existing icon in update");
                }
                else
                {
                    Debug.WriteLine("⚠️ No icon found in existing app");
                }

                // Step 3: Send the update
                var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";
                var json = JsonSerializer.Serialize(updateBody, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                Debug.WriteLine($"Updating app with preserved metadata...");

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await sharedHttpClient!.PatchAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"❌ Update failed: {responseText}");
                    throw new Exception($"Failed to update content version. Status: {response.StatusCode}, Response: {responseText}");
                }

                Debug.WriteLine($"✅ Successfully updated committedContentVersion to {contentVersionId} with preserved icon");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error in UpdateCommittedContentVersionAsync: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> UpdateExistingApplicationAsync(
           string existingAppId,
           string packagePath,
           IUploadProgress? progress = null)
        {
            try
            {
                progress?.UpdateProgress(5, "Authenticating with Microsoft Graph...");
                var token = await _intuneService.GetAccessTokenAsync();
                EnsureHttpClient();
                sharedHttpClient!.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // Step 1: Create the .intunewin file
                progress?.UpdateProgress(10, "Converting package to .intunewin format...");
                await CreateIntuneWinFileAsync(packagePath);

                // Step 2: Find the created .intunewin file
                progress?.UpdateProgress(15, "Locating .intunewin file...");
                var intuneFolder = Path.Combine(packagePath, "Intune");
                var intuneWinFiles = Directory.GetFiles(intuneFolder, "*.intunewin");
                if (intuneWinFiles.Length == 0)
                {
                    throw new Exception("No .intunewin file found after conversion.");
                }

                var intuneWinFile = intuneWinFiles[0];
                Debug.WriteLine($"✓ Found .intunewin file: {Path.GetFileName(intuneWinFile)}");

                // Step 3: Extract .intunewin metadata
                progress?.UpdateProgress(20, "Extracting .intunewin metadata...");
                var intuneWinInfo = ExtractIntuneWinInfo(intuneWinFile);

                // Step 4: Create NEW content version for EXISTING app 
                progress?.UpdateProgress(25, "Creating new content version for existing app...");
                var contentVersionId = await CreateContentVersionAsync(existingAppId);
                _currentAppId = existingAppId;  // Use existing app ID
                _currentContentVersionId = contentVersionId;

                // Step 5: Create file entry
                progress?.UpdateProgress(35, "Creating file entry...");
                var fileId = await CreateFileEntryAsync(existingAppId, contentVersionId, intuneWinInfo);
                _currentFileId = fileId;

                // Step 6: Wait for Azure Storage URI
                progress?.UpdateProgress(45, "Getting Azure Storage URI...");
                var azureStorageInfo = await WaitForAzureStorageUriAsync(existingAppId, contentVersionId, fileId);

                // Step 7: Upload file to Azure Storage
                progress?.UpdateProgress(55, "Uploading file to Azure Storage...");
                await UploadFileToAzureStorageAsync(azureStorageInfo.SasUri, intuneWinInfo.EncryptedFilePath, progress);

                // Step 8: Commit the file
                progress?.UpdateProgress(85, "Committing file...");
                await CommitFileAsync(existingAppId, contentVersionId, fileId, intuneWinInfo.EncryptionInfo);

                // Step 9: Wait for file processing
                progress?.UpdateProgress(90, "Waiting for file processing...");
                await WaitForFileProcessingAsync(existingAppId, contentVersionId, fileId, "CommitFile");

                // Step 10: Commit the app with new content version
                progress?.UpdateProgress(95, "Finalizing application update...");
                await UpdateCommittedContentVersionAsync(existingAppId, contentVersionId);

                // Step 11: Cleanup temp files
                CleanupTempFiles(intuneWinInfo);

                progress?.UpdateProgress(100, "Application updated successfully!");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Failed to update application: {ex.Message}");
                progress?.UpdateProgress(0, $"Update failed: {ex.Message}");
                throw new Exception($"Failed to update application in Intune: {ex.Message}", ex);
            }
        }
        public void Dispose()
        {
            sharedHttpClient?.Dispose();
        }
    }

    // Supporting classes
    public class IntuneWinInfo
    {
        public string FileName { get; set; } = "";
        public long UnencryptedContentSize { get; set; }
        public string EncryptedFilePath { get; set; } = "";
        public string TempDirectory { get; set; } = "";
        public EncryptionInfo EncryptionInfo { get; set; } = new();
    }

    public class EncryptionInfo
    {
        public string EncryptionKey { get; set; } = "";
        public string MacKey { get; set; } = "";
        public string InitializationVector { get; set; } = "";
        public string Mac { get; set; } = "";
        public string ProfileIdentifier { get; set; } = "";
        public string FileDigest { get; set; } = "";
        public string FileDigestAlgorithm { get; set; } = "";
    }

    public class AzureStorageInfo
    {
        public string SasUri { get; set; } = "";
    }
}