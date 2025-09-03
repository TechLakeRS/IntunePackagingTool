using IntunePackagingTool.Dialogs;
using IntunePackagingTool.Models; 
using IntunePackagingTool.WizardSteps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IntunePackagingTool.Services

{
    public class IntuneService : IDisposable
    {
        private static readonly HttpClient _sharedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        private readonly CacheService _cache = new CacheService();

        private string? _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private bool _disposed = false;

        private readonly string _clientId = "b47987a1-70b4-415a-9a4e-9775473e382b";
        private readonly string _tenantId = "43f10d24-b9bf-46da-a9c8-15c1b0990ce7";
        private readonly string _certificateThumbprint = "CF6DCE7DF3377CA65D9B40F06BF8C2228AC7821F";

        public async Task<string> GetAccessTokenAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry.AddMinutes(-5))
            {
                
                return _accessToken;
            }

           
            var certificate = LoadCertificate();
            var assertion = CreateJwtAssertion(certificate);

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_assertion_type", "urn:ietf:params:oauth:client-assertion-type:jwt-bearer"),
                new KeyValuePair<string, string>("client_assertion", assertion),
                new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default"),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            var tokenUrl = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";
            var response = await _sharedHttpClient!.PostAsync(tokenUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                
                throw new Exception($"Failed to get access token. Status: {response.StatusCode}, Response: {responseText}");
            }

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseText);
            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
               
                throw new Exception("Access token is missing in response");
            }

            _accessToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _sharedHttpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);

            return _accessToken;
        }

        private X509Certificate2 LoadCertificate()
        {
            var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
            if (certificates.Count == 0)
            {
               
                throw new Exception("Certificate not found");
            }
            return certificates[0];
        }

        private string CreateJwtAssertion(X509Certificate2 certificate)
        {
            var now = DateTimeOffset.UtcNow;
            var x5t = Base64UrlEncode(certificate.GetCertHash());

            var header = new
            {
                alg = "RS256",
                typ = "JWT",
                x5t = x5t
            };

            var payload = new
            {
                aud = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token",
                exp = now.AddMinutes(10).ToUnixTimeSeconds(),
                iss = _clientId,
                jti = Guid.NewGuid().ToString(),
                nbf = now.ToUnixTimeSeconds(),
                sub = _clientId
            };

            var headerJson = JsonSerializer.Serialize(header);
            var payloadJson = JsonSerializer.Serialize(payload);

            var headerEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadEncoded = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            var message = $"{headerEncoded}.{payloadEncoded}";
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var rsa = certificate.GetRSAPrivateKey();
            if (rsa == null)
            {
                throw new InvalidOperationException(
                    "Certificate does not contain a private key. " +
                    "Please ensure you're using a certificate with a private key for authentication.");
            }
            var signature = rsa.SignData(messageBytes, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var signatureEncoded = Base64UrlEncode(signature);

            return $"{message}.{signatureEncoded}";
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        public async Task<List<IntuneApplication>> GetApplicationsAsync(bool forceRefresh = false)
        {
            if (forceRefresh)
            {
                _cache.Clear("apps_list");
            }

            return await _cache.GetOrAddAsync("apps_list",
                async () => await GetApplicationsFromGraphAsync(),
                TimeSpan.FromMinutes(10));
        }

        public async Task<List<IntuneApplication>> GetApplicationsFromGraphAsync()
        {
            try
            {
                Debug.WriteLine("=== GRAPH API CALL ===");
                Debug.WriteLine("Starting GetApplicationsAsync...");
                
                var token = await GetAccessTokenAsync();
                Debug.WriteLine($"✓ Token obtained for Graph API call, length: {token.Length}");

                // Try with smaller batch and expand categories - if it fails, we'll fallback
                var requestUrl = "https://graph.microsoft.com/beta/deviceAppManagement/mobileApps?$filter=isof('microsoft.graph.win32LobApp')&$expand=categories&$top=100";
                Debug.WriteLine($"Making Graph API request to: {requestUrl}");

                var response = await _sharedHttpClient.GetAsync(requestUrl);
                var responseText = await response.Content.ReadAsStringAsync();

                Debug.WriteLine($"Graph API response status: {response.StatusCode}");
                Debug.WriteLine($"Graph API response length: {responseText.Length}");

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"✗ Graph API error with categories expand, trying without expand...");
                    
                    // Fallback: try without expand if categories expand fails
                    requestUrl = "https://graph.microsoft.com/beta/deviceAppManagement/mobileApps?$filter=isof('microsoft.graph.win32LobApp')&$top=100";
                    Debug.WriteLine($"Fallback request to: {requestUrl}");
                    
                    response = await _sharedHttpClient.GetAsync(requestUrl);
                    responseText = await response.Content.ReadAsStringAsync();
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        Debug.WriteLine($"✗ Graph API error response: {responseText}");
                        
                        // Try to parse Graph API error
                        try
                        {
                            var errorResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
                            if (errorResponse.TryGetProperty("error", out var errorObj))
                            {
                                var code = errorObj.TryGetProperty("code", out var codeProp) ? codeProp.GetString() : "Unknown";
                                var message = errorObj.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Unknown";
                                
                                Debug.WriteLine($"Graph API Error - Code: {code}, Message: {message}");
                                throw new Exception($"Graph API Error: {code} - {message}");
                            }
                        }
                        catch (JsonException)
                        {
                            Debug.WriteLine("Could not parse Graph API error response");
                        }
                        
                        throw new Exception($"Graph API request failed. Status: {response.StatusCode}, Response: {responseText}");
                    }
                }

                Debug.WriteLine("✓ Graph API request successful, parsing response...");

                var graphResponse = JsonSerializer.Deserialize<GraphResponse<JsonElement>>(responseText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var apps = new List<IntuneApplication>();

                if (graphResponse?.Value != null)
                {
                    Debug.WriteLine($"✓ Found {graphResponse.Value.Count} applications in Intune");
                    
                    foreach (var app in graphResponse.Value)
                    {
                        var id = app.GetProperty("id").GetString() ?? "";
                        var displayName = app.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "Unknown" : "Unknown";
                        var version = app.TryGetProperty("displayVersion", out var verProp) ? verProp.GetString() ?? "1.0.0" : "1.0.0";
                        var publisher = app.TryGetProperty("publisher", out var pubProp) ? pubProp.GetString() ?? "Unknown" : "Unknown";

                        // Try to get real categories from Intune if they were expanded
                        var category = "Uncategorized"; // Default
                        if (app.TryGetProperty("categories", out var categoriesProp) && categoriesProp.ValueKind == JsonValueKind.Array)
                        {
                            var categoryNames = new List<string>();
                            foreach (var cat in categoriesProp.EnumerateArray())
                            {
                                if (cat.TryGetProperty("displayName", out var catName))
                                {
                                    var catNameStr = catName.GetString();
                                    if (!string.IsNullOrWhiteSpace(catNameStr))
                                        categoryNames.Add(catNameStr);
                                }
                            }
                            if (categoryNames.Count > 0)
                                category = string.Join(", ", categoryNames);
                        }

                        Debug.WriteLine($"  App: {displayName} v{version} by {publisher} - Category: {category}");

                        apps.Add(new IntuneApplication
                        {
                            Id = id,
                            DisplayName = displayName,
                            Version = version,
                            Publisher = publisher,
                            Category = category,
                            LastModified = DateTime.Now
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("⚠ No applications found in response");
                }

                // Handle pagination if there are more results (but limit to avoid timeouts)
                var hasNextPage = graphResponse?.ODataNextLink != null;
                var pageCount = 1;
                
                while (hasNextPage && pageCount < 20) // Allow more pages since we're using smaller batches
                {
                    Debug.WriteLine($"Getting page {pageCount + 1}...");
                    
                    var nextResponse = await _sharedHttpClient.GetAsync(graphResponse!.ODataNextLink);
                    if (!nextResponse.IsSuccessStatusCode) break;
                    
                    var nextResponseText = await nextResponse.Content.ReadAsStringAsync();
                    var nextGraphResponse = JsonSerializer.Deserialize<GraphResponse<JsonElement>>(nextResponseText, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    
                    if (nextGraphResponse?.Value != null)
                    {
                        Debug.WriteLine($"✓ Found {nextGraphResponse.Value.Count} more applications");
                        
                        foreach (var app in nextGraphResponse.Value)
                        {
                            var id = app.GetProperty("id").GetString() ?? "";
                            var displayName = app.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "Unknown" : "Unknown";
                            var version = app.TryGetProperty("displayVersion", out var verProp) ? verProp.GetString() ?? "1.0.0" : "1.0.0";
                            var publisher = app.TryGetProperty("publisher", out var pubProp) ? pubProp.GetString() ?? "Unknown" : "Unknown";

                            // Try to get real categories
                            var category = "Uncategorized"; // Default
                            if (app.TryGetProperty("categories", out var categoriesProp) && categoriesProp.ValueKind == JsonValueKind.Array)
                            {
                                var categoryNames = new List<string>();
                                foreach (var cat in categoriesProp.EnumerateArray())
                                {
                                    if (cat.TryGetProperty("displayName", out var catName))
                                    {
                                        var catNameStr = catName.GetString();
                                        if (!string.IsNullOrWhiteSpace(catNameStr))
                                            categoryNames.Add(catNameStr);
                                    }
                                }
                                if (categoryNames.Count > 0)
                                    category = string.Join(", ", categoryNames);
                            }

                            apps.Add(new IntuneApplication
                            {
                                Id = id,
                                DisplayName = displayName,
                                Version = version,
                                Publisher = publisher,
                                Category = category,
                                LastModified = DateTime.Now
                            });
                        }
                    }
                    
                    graphResponse = nextGraphResponse;
                    hasNextPage = graphResponse?.ODataNextLink != null;
                    pageCount++;
                }

                Debug.WriteLine($"✓ Successfully processed {apps.Count} applications across {pageCount} pages");
                return apps.OrderBy(a => a.DisplayName).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ Error in GetApplicationsAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                MessageBox.Show(
                    $"Failed to retrieve applications from Intune:\n\n{ex.Message}\n\nCheck the Debug Output window for detailed logs.",
                    "Graph API Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
                
                throw;
            }
        }
        public async Task<ApplicationDetail> GetApplicationDetailAsync(string intuneAppId, bool forceRefresh = false)
        {
            var cacheKey = $"app_detail_{intuneAppId}";

            if (forceRefresh)
            {
                _cache.Clear(cacheKey);
            }

            return await _cache.GetOrAddAsync(cacheKey,
                async () => await GetApplicationDetailFromGraphAsync(intuneAppId),
                TimeSpan.FromMinutes(15));
        }
        public async Task<ApplicationDetail> GetApplicationDetailFromGraphAsync(string intuneAppId)
         {
            try
            {
            Debug.WriteLine($"=== FETCHING APP DETAILS FOR: {intuneAppId} ===");

            var token = await GetAccessTokenAsync();
            
            

            // Get the complete application details including detection rules in single call
            var appUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{intuneAppId}";
            Debug.WriteLine($"Getting complete app details from: {appUrl}");

            var appResponse = await _sharedHttpClient.GetAsync(appUrl);
            var appResponseText = await appResponse.Content.ReadAsStringAsync();

            if (!appResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to get app details: {appResponse.StatusCode} - {appResponseText}");
            }

            var appJson = JsonSerializer.Deserialize<JsonElement>(appResponseText);

        
            ApplicationDetail appDetail;
            try 
            {
                // Extract complete app information from Graph API response
                appDetail = new ApplicationDetail
                {
                // Basic Properties
                Id = appJson.GetProperty("id").GetString() ?? "",
                DisplayName = appJson.TryGetProperty("displayName", out var displayNameProp) ? displayNameProp.GetString() ?? "Unknown" : "Unknown",
                Version = appJson.TryGetProperty("displayVersion", out var versionProp) ? versionProp.GetString() ?? "1.0.0" : "1.0.0",
                Publisher = appJson.TryGetProperty("publisher", out var publisherProp) ? publisherProp.GetString() ?? "Unknown" : "Unknown",
                Description = appJson.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
                InstallCommand = appJson.TryGetProperty("installCommandLine", out var installProp) ? installProp.GetString() ?? "Deploy-Application.exe Install" : "Deploy-Application.exe Install",
                UninstallCommand = appJson.TryGetProperty("uninstallCommandLine", out var uninstallProp) ? uninstallProp.GetString() ?? "Deploy-Application.exe Uninstall" : "Deploy-Application.exe Uninstall",
                InstallContext = appJson.TryGetProperty("installContext", out var contextProp) ? contextProp.GetString() ?? "System" : "System",

                // Extended Properties
                Owner = appJson.TryGetProperty("owner", out var ownerProp) ? ownerProp.GetString() ?? "" : "",
                Developer = appJson.TryGetProperty("developer", out var devProp) ? devProp.GetString() ?? "" : "",
                Notes = appJson.TryGetProperty("notes", out var notesProp) ? notesProp.GetString() ?? "" : "",
                FileName = appJson.TryGetProperty("fileName", out var fileNameProp) ? fileNameProp.GetString() ?? "" : "",
                
                // ✅ FIXED: Safe numeric property parsing
                Size = GetSafeLong(appJson, "size"),
                MinimumFreeDiskSpaceInMB = GetSafeInt(appJson, "minimumFreeDiskSpaceInMB"),
                MinimumMemoryInMB = GetSafeInt(appJson, "minimumMemoryInMB"),
                MinimumNumberOfProcessors = GetSafeInt(appJson, "minimumNumberOfProcessors"),
                MinimumCpuSpeedInMHz = GetSafeInt(appJson, "minimumCpuSpeedInMHz"),
                
                // Boolean properties (these are usually safe)
                IsFeatured = appJson.TryGetProperty("isFeatured", out var featuredProp) && featuredProp.GetBoolean(),
                IsAssigned = appJson.TryGetProperty("isAssigned", out var assignedProp) && assignedProp.GetBoolean(),
                AllowAvailableUninstall = appJson.TryGetProperty("allowAvailableUninstall", out var uninstallAvailProp) && uninstallAvailProp.GetBoolean(),
                
                // String properties
                PrivacyInformationUrl = appJson.TryGetProperty("privacyInformationUrl", out var privacyProp) ? privacyProp.GetString() ?? "" : "",
                InformationUrl = appJson.TryGetProperty("informationUrl", out var infoProp) ? infoProp.GetString() ?? "" : "",
                UploadState = appJson.TryGetProperty("uploadState", out var uploadProp)
                ? (uploadProp.ValueKind == JsonValueKind.Number
                ? uploadProp.GetInt32().ToString()
                 : uploadProp.GetString() ?? "")
                 : "",
                PublishingState = appJson.TryGetProperty("publishingState", out var publishProp) ? publishProp.GetString() ?? "" : "",
                ApplicableArchitectures = appJson.TryGetProperty("applicableArchitectures", out var appArchProp) ? appArchProp.GetString() ?? "" : "",
                AllowedArchitectures = appJson.TryGetProperty("allowedArchitectures", out var allowArchProp) ? allowArchProp.GetString() ?? "" : "",
                SetupFilePath = appJson.TryGetProperty("setupFilePath", out var setupProp) ? setupProp.GetString() ?? "" : "",
                MinimumSupportedWindowsRelease = appJson.TryGetProperty("minimumSupportedWindowsRelease", out var winProp) ? winProp.GetString() ?? "" : "",

                // Dates (these need special handling too)
                CreatedDateTime = GetSafeDateTime(appJson, "createdDateTime"),
                LastModifiedDateTime = GetSafeDateTime(appJson, "lastModifiedDateTime"),

                Category = "Loading..." // Will be updated later
            };
                }
                catch (InvalidOperationException ex)
                {
                    Debug.WriteLine($"❌ JSON parsing failed for app ID: {intuneAppId}");
                    Debug.WriteLine($"❌ Error: {ex.Message}");
                    Debug.WriteLine($"❌ Stack trace: {ex.StackTrace}");
            
                    // Log the raw JSON to see which property is null
                    var debugFileName = $"debug_app_{intuneAppId}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                    File.WriteAllText(debugFileName, appJson.GetRawText());
                    Debug.WriteLine($"🔍 Raw JSON saved to: {debugFileName}");
            
                    throw;
                }

                // Parse large icon
                if (appJson.TryGetProperty("largeIcon", out var iconProp) && iconProp.ValueKind != JsonValueKind.Null)
                {
                    var iconType = iconProp.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "" : "";
                    var iconValue = iconProp.TryGetProperty("value", out var valueProp) ? valueProp.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(iconValue))
                    {
                        try
                        {
                            appDetail.IconType = iconType;
                            // This will automatically trigger CreateIconImage() via the property setter
                            appDetail.IconData = Convert.FromBase64String(iconValue);
                            Debug.WriteLine($"Icon data set: {appDetail.IconData.Length} bytes");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to parse icon: {ex.Message}");
                        }
                    }
                }

                // Parse role scope tags
                if (appJson.TryGetProperty("roleScopeTagIds", out var roleTagsProp) && roleTagsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var tag in roleTagsProp.EnumerateArray())
            {
                var tagValue = tag.GetString();
                if (!string.IsNullOrEmpty(tagValue))
                    appDetail.RoleScopeTagIds.Add(tagValue);
            }
        }

        // Parse return codes - this could also cause issues
        if (appJson.TryGetProperty("returnCodes", out var returnCodesProp) && returnCodesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var returnCode in returnCodesProp.EnumerateArray())
            {
                // ✅ FIXED: Safe return code parsing
                var code = GetSafeInt(returnCode, "returnCode");
                var type = returnCode.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "Unknown" : "Unknown";

                appDetail.ReturnCodes.Add(new ReturnCode
                {
                    Code = code,
                    Type = type
                });
            }
        }

                    Debug.WriteLine($"✓ Basic app info loaded: {appDetail.DisplayName}");

                    // Parse detection rules from main response
                    appDetail.DetectionRules = ParseDetectionRulesFromResponse(appJson);

                    // Fetch assignments and categories separately 
                    var assignmentsTask = GetAssignedGroupsAsync(intuneAppId);
                    var categoriesTask = GetAppCategoriesAsync(intuneAppId);

                    await Task.WhenAll(assignmentsTask, categoriesTask);

                    appDetail.AssignedGroups = await assignmentsTask;
                    appDetail.Category = await categoriesTask;

                    Debug.WriteLine($"✓ Complete app details loaded for: {appDetail.DisplayName}");
                    return appDetail;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"✗ Error getting app details: {ex.Message}");
                    throw new Exception($"Failed to get application details from Intune: {ex.Message}", ex);
                }
            }

                
                private static long GetSafeLong(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Number)
                        {
                            return prop.GetInt64();
                        }
                        else if (prop.ValueKind == JsonValueKind.String)
                        {
                            if (long.TryParse(prop.GetString(), out var longValue))
                                return longValue;
                        }
                        // If it's null or any other type, return 0
                        Debug.WriteLine($"⚠️ Property '{propertyName}' has unexpected type: {prop.ValueKind}");
                    }
                    return 0;
                }

                private static int GetSafeInt(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Number)
                        {
                            return prop.GetInt32();
                        }
                        else if (prop.ValueKind == JsonValueKind.String)
                        {
                            if (int.TryParse(prop.GetString(), out var intValue))
                                return intValue;
                        }
                        // If it's null or any other type, return 0
                        Debug.WriteLine($"⚠️ Property '{propertyName}' has unexpected type: {prop.ValueKind}");
                    }
                    return 0;
                }

                private static DateTime GetSafeDateTime(JsonElement element, string propertyName)
                {
                    if (element.TryGetProperty(propertyName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.String)
                        {
                            var dateString = prop.GetString();
                            if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out var dateValue))
                                return dateValue;
                        }
                        Debug.WriteLine($"⚠️ Property '{propertyName}' has unexpected type: {prop.ValueKind}");
                    }
                    return DateTime.MinValue;
                }

        private List<DetectionRule> ParseDetectionRulesFromResponse(JsonElement appJson)
        {
            var rules = new List<DetectionRule>();

            try
            {
                if (appJson.TryGetProperty("detectionRules", out var rulesArray) && rulesArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var rule in rulesArray.EnumerateArray())
                    {
                        var ruleType = rule.TryGetProperty("@odata.type", out var typeProp) ? typeProp.GetString() : "";

                        switch (ruleType)
                        {
                            case "#microsoft.graph.win32LobAppFileSystemDetection":
                                var filePath = rule.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                                var fileName = rule.TryGetProperty("fileOrFolderName", out var fileProp) ? fileProp.GetString() ?? "" : "";
                                var checkVersion = rule.TryGetProperty("check32BitOn64System", out var checkProp) && checkProp.GetBoolean();

                                rules.Add(new DetectionRule
                                {
                                    Type = DetectionRuleType.File,
                                    Path = filePath,
                                    FileOrFolderName = fileName,
                                    CheckVersion = checkVersion
                                });
                                break;

                            case "#microsoft.graph.win32LobAppRegistryDetection":
                                var keyPath = rule.TryGetProperty("keyPath", out var keyProp) ? keyProp.GetString() ?? "" : "";
                                var valueName = rule.TryGetProperty("valueName", out var valueProp) ? valueProp.GetString() ?? "" : "";

                                rules.Add(new DetectionRule
                                {
                                    Type = DetectionRuleType.Registry,
                                    Path = keyPath,
                                    FileOrFolderName = valueName
                                });
                                break;

                            case "#microsoft.graph.win32LobAppProductCodeDetection":  // ← ADD THIS CASE
                                var productCode = rule.TryGetProperty("productCode", out var codeProp) ? codeProp.GetString() ?? "" : "";
                                var productVersion = rule.TryGetProperty("productVersion", out var versionProp) ? versionProp.GetString() ?? "" : "";
                                var productVersionOperator = rule.TryGetProperty("productVersionOperator", out var operatorProp) ? operatorProp.GetString() ?? "" : "";

                                rules.Add(new DetectionRule
                                {
                                    Type = DetectionRuleType.MSI,
                                    Path = productCode,
                                    FileOrFolderName = productVersion,
                                    CheckVersion = !string.IsNullOrEmpty(productVersion) && productVersionOperator != "notConfigured"
                                });
                                break;

                            case "#microsoft.graph.win32LobAppMsiInformation":  // Keep this for backward compatibility
                                var msiCode = rule.TryGetProperty("productCode", out var msiCodeProp) ? msiCodeProp.GetString() ?? "" : "";
                                var msiVersion = rule.TryGetProperty("productVersion", out var msiVersionProp) ? msiVersionProp.GetString() ?? "" : "";

                                rules.Add(new DetectionRule
                                {
                                    Type = DetectionRuleType.MSI,
                                    Path = msiCode,
                                    FileOrFolderName = msiVersion,
                                    CheckVersion = !string.IsNullOrEmpty(msiVersion)
                                });
                                break;

                            default:
                                // Create a generic rule for unknown types
                                rules.Add(new DetectionRule
                                {
                                    Type = DetectionRuleType.File, // Default to file type
                                    Path = "Unknown detection rule type",
                                    FileOrFolderName = ruleType?.Replace("#microsoft.graph.", "") ?? "Unknown"
                                });
                                break;
                        }
                    }
                }

                // Also check for MSI information at root level
                if (appJson.TryGetProperty("msiInformation", out var msiInfo) && msiInfo.ValueKind != JsonValueKind.Null)
                {
                    var productCode = msiInfo.TryGetProperty("productCode", out var msiCodeProp) ? msiCodeProp.GetString() ?? "" : "";
                    var productVersion = msiInfo.TryGetProperty("productVersion", out var msiVersionProp) ? msiVersionProp.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(productCode))
                    {
                        rules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.MSI,
                            Path = productCode,
                            FileOrFolderName = productVersion,
                            CheckVersion = !string.IsNullOrEmpty(productVersion)
                        });
                    }
                }

                if (rules.Count == 0)
                {
                    rules.Add(new DetectionRule
                    {
                        Type = DetectionRuleType.File,
                        Path = "No detection rules configured",
                        FileOrFolderName = "for this application"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing detection rules: {ex.Message}");
                rules.Add(new DetectionRule
                {
                    Type = DetectionRuleType.File,
                    Path = "Error loading detection rules",
                    FileOrFolderName = ex.Message
                });
            }

            return rules;
        }

        public async Task<string> CreateOrGetGroupAsync(string displayName, string description)
        {
            try
            {
                var token = await GetAccessTokenAsync();
               
               

                // First check if group exists
                var checkUrl = $"https://graph.microsoft.com/v1.0/groups?$filter=displayName eq '{Uri.EscapeDataString(displayName)}'";
                var checkResponse = await _sharedHttpClient.GetAsync(checkUrl);

                if (checkResponse.IsSuccessStatusCode)
                {
                    var checkContent = await checkResponse.Content.ReadAsStringAsync();
                    var checkJson = JsonSerializer.Deserialize<JsonElement>(checkContent);

                    if (checkJson.TryGetProperty("value", out var groups) &&
                        groups.GetArrayLength() > 0)
                    {
                        // Group exists, return its ID
                        var existingGroupId = groups[0].GetProperty("id").GetString();
                        Debug.WriteLine($"Group '{displayName}' already exists with ID: {existingGroupId}");
                        return existingGroupId;
                    }
                }

                // Create new group
                var groupPayload = new
                {
                    displayName = displayName,
                    description = description,
                    mailEnabled = false,
                    mailNickname = displayName.Replace("_", "-").ToLower(),
                    securityEnabled = true
                };

                var createUrl = "https://graph.microsoft.com/v1.0/groups";
                var content = new StringContent(JsonSerializer.Serialize(groupPayload),
                    System.Text.Encoding.UTF8, "application/json");

                var createResponse = await _sharedHttpClient.PostAsync(createUrl, content);
                var responseContent = await createResponse.Content.ReadAsStringAsync();

                if (!createResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to create group: {createResponse.StatusCode} - {responseContent}");
                }

                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var groupId = responseJson.GetProperty("id").GetString();

                Debug.WriteLine($"Created group '{displayName}' with ID: {groupId}");
                return groupId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating/getting group: {ex.Message}");
                throw;
            }
        }

        public async Task AssignGroupsToApplicationAsync(string appId, GroupAssignmentIds groupIds)
        {
            try
            {
                var token = await GetAccessTokenAsync();
               
                

                // Create install assignments
                if (!string.IsNullOrEmpty(groupIds.SystemInstallId))
                {
                    await CreateAssignment(appId, groupIds.SystemInstallId, "required");
                }

                if (!string.IsNullOrEmpty(groupIds.UserInstallId))
                {
                    await CreateAssignment(appId, groupIds.UserInstallId, "required");
                }

                // Create uninstall assignments
                if (!string.IsNullOrEmpty(groupIds.SystemUninstallId))
                {
                    await CreateAssignment(appId, groupIds.SystemUninstallId, "uninstall");
                }

                if (!string.IsNullOrEmpty(groupIds.UserUninstallId))
                {
                    await CreateAssignment(appId, groupIds.UserUninstallId, "uninstall");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error assigning groups: {ex.Message}");
                throw;
            }
        }

        private async Task CreateAssignment(string appId, string groupId, string intent)
        {
            var assignmentPayload = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.mobileAppAssignment",
                ["intent"] = intent,
                ["target"] = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.groupAssignmentTarget",
                    ["groupId"] = groupId
                },
                ["settings"] = null
            };

            var url = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/assignments";
            var content = new StringContent(JsonSerializer.Serialize(assignmentPayload),
                System.Text.Encoding.UTF8, "application/json");

            var response = await _sharedHttpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Warning: Failed to create {intent} assignment for group {groupId}: {error}");
                // Don't throw - continue with other assignments
            }
            else
            {
                Debug.WriteLine($"Created {intent} assignment for group {groupId}");
            }
        }

        
        

        private async Task<List<AssignedGroup>> GetAssignedGroupsAsync(string appId)
        {
            var groups = new List<AssignedGroup>();

            try
            {
                var assignmentsUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/assignments";
                var response = await _sharedHttpClient!.GetAsync(assignmentsUrl);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var json = JsonSerializer.Deserialize<JsonElement>(responseText);

                    if (json.TryGetProperty("value", out var assignmentsArray) && assignmentsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var assignment in assignmentsArray.EnumerateArray())
                        {
                            var intent = assignment.TryGetProperty("intent", out var intentProp) ? intentProp.GetString() : "Unknown";
                            var groupName = "Unknown Group";

                            if (assignment.TryGetProperty("target", out var targetObj))
                            {
                                var targetType = targetObj.TryGetProperty("@odata.type", out var targetTypeProp) ? targetTypeProp.GetString() : "";

                                switch (targetType)
                                {
                                    case "#microsoft.graph.groupAssignmentTarget":
                                        if (targetObj.TryGetProperty("groupId", out var groupIdProp))
                                        {
                                            var groupId = groupIdProp.GetString();
                                            groupName = await GetGroupNameAsync(groupId) ?? $"Group ID: {groupId}";
                                        }
                                        break;

                                    case "#microsoft.graph.allLicensedUsersAssignmentTarget":
                                        groupName = "All Licensed Users";
                                        break;

                                    case "#microsoft.graph.allDevicesAssignmentTarget":
                                        groupName = "All Devices";
                                        break;

                                    default:
                                        groupName = targetType?.Replace("#microsoft.graph.", "") ?? "Unknown Target";
                                        break;
                                }
                            }

                            groups.Add(new AssignedGroup
                            {
                                GroupName = groupName,
                                AssignmentType = intent
                            });
                        }
                    }
                }

                if (groups.Count == 0)
                {
                    groups.Add(new AssignedGroup
                    {
                        GroupName = "No Assignments",
                        AssignmentType = "Not assigned to any groups"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting assigned groups: {ex.Message}");
                groups.Add(new AssignedGroup { GroupName = "Error Loading Groups", AssignmentType = "Could not load assignments" });
            }

            return groups;
        }

        private async Task<string> GetGroupNameAsync(string groupId)
        {
            try
            {
                var groupUrl = $"https://graph.microsoft.com/beta/groups/{groupId}?$select=displayName";
                var response = await _sharedHttpClient!.GetAsync(groupUrl);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var json = JsonSerializer.Deserialize<JsonElement>(responseText);
                    return json.TryGetProperty("displayName", out var nameProp) ? nameProp.GetString() : null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting group name for {groupId}: {ex.Message}");
            }

            return null;
        }

        private async Task<string> GetAppCategoriesAsync(string appId)
        {
            try
            {
                var categoriesUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/categories";
                var response = await _sharedHttpClient!.GetAsync(categoriesUrl);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var json = JsonSerializer.Deserialize<JsonElement>(responseText);

                    if (json.TryGetProperty("value", out var categoriesArray) && categoriesArray.ValueKind == JsonValueKind.Array)
                    {
                        var categoryNames = new List<string>();

                        foreach (var category in categoriesArray.EnumerateArray())
                        {
                            if (category.TryGetProperty("displayName", out var catNameProp))
                            {
                                var catName = catNameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(catName))
                                    categoryNames.Add(catName);
                            }
                        }

                        return categoryNames.Count > 0 ? string.Join(", ", categoryNames) : "Uncategorized";
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting categories: {ex.Message}");
            }

            return "Uncategorized";
        }

        public async Task<List<string>> GetApplicationCategoriesAsync()
        {
            try
            {
                var token = await GetAccessTokenAsync();
                var url = "https://graph.microsoft.com/beta/deviceAppManagement/mobileAppCategories";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    var categories = new List<string>();
                    if (doc.RootElement.TryGetProperty("value", out var value))
                    {
                        foreach (var cat in value.EnumerateArray())
                        {
                            if (cat.TryGetProperty("displayName", out var name))
                            {
                                var categoryName = name.GetString();
                                if (!string.IsNullOrWhiteSpace(categoryName))
                                    categories.Add(categoryName);
                            }
                        }
                    }

                    // Sort alphabetically for easier selection
                    categories.Sort();
                    return categories;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching categories: {ex.Message}");
            }

            // Return empty list if fetch fails (will fall back to hardcoded)
            return new List<string>();
        }

        public async Task <string>UploadApplicationAsync(ApplicationInfo appInfo, List<DetectionRule> detectionRules = null)
        {
            try
            {
                var token = await GetAccessTokenAsync();
                await Task.Delay(1000); // Simulate upload delay

                MessageBox.Show(
                    $"Win32 app upload via API requires complex file handling.\n\n" +
                    $"Package: {appInfo.Manufacturer}_{appInfo.Name}_{appInfo.Version}\n\n" +
                    $"Package is ready at the network location.\n",
                    "Upload Status", MessageBoxButton.OK, MessageBoxImage.Information);

                string appId = "generated-app-id"; // Get this from the actual upload response
                return appId;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to upload application to Intune: {ex.Message}", ex);
            }
        }

        public async Task<bool> UpdateApplicationAsync(
       string appId,
       string displayName,
       string description,
       string category,
       List<DetectionRule> detectionRules,
       string packagePath = null,
       string iconPath = null)
        {
            try
            {
                var token = await GetAccessTokenAsync();
                var updateUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}";

                // Build update body
                var updateBody = new Dictionary<string, object>
                {
                    ["@odata.type"] = "#microsoft.graph.win32LobApp",
                    ["displayName"] = displayName,
                    ["description"] = description,
                    ["categories"] = new[] { category }
                };

                // Add detection rules if provided
                if (detectionRules?.Count > 0)
                {
                    var formattedRules = new List<object>();

                    foreach (var rule in detectionRules)
                    {
                        object formattedRule = rule.Type switch
                        {
                            DetectionRuleType.File => new
                            {
                                odataType = "#microsoft.graph.win32LobAppFileSystemDetection",
                                path = rule.Path,
                                fileOrFolderName = rule.FileOrFolderName,
                                check32BitOn64System = false,
                                detectionType = rule.CheckVersion ? "version" : "exists"
                            },
                            DetectionRuleType.Registry => new
                            {
                                odataType = "#microsoft.graph.win32LobAppRegistryDetection",
                                check32BitOn64System = false,
                                keyPath = rule.Path,
                                valueName = rule.FileOrFolderName,
                                detectionType = string.IsNullOrEmpty(rule.FileOrFolderName) ? "exists" : "string"
                            },
                            DetectionRuleType.MSI => new
                            {
                                odataType = "#microsoft.graph.win32LobAppProductCodeDetection",
                                productCode = rule.Path,
                                productVersionOperator = rule.Operator,
                                productVersion = rule.FileOrFolderName
                            },
                            _ => null
                        };

                        if (formattedRule != null)
                        {
                            formattedRules.Add(formattedRule);
                        }
                    }

                    updateBody["detectionRules"] = formattedRules;
                }

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                var json = JsonSerializer.Serialize(updateBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var patchMethod = new HttpMethod("PATCH");
                var request = new HttpRequestMessage(patchMethod, updateUrl)
                {
                    Content = content
                };

                var response = await httpClient.SendAsync(request);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating app: {ex.Message}");
                return false;
            }
        }

        // Dispose method to clean up HttpClient
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Don't dispose the static HttpClient
                    // Clear sensitive data
                    _accessToken = null;

                    // Clear any other managed resources if needed
                }
                _disposed = true;
            }
        }

        ~IntuneService()
        {
            Dispose(false);
        }
    }

    public class GraphResponse<T>
    {
        [JsonPropertyName("@odata.context")]
        public string? ODataContext { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? ODataNextLink { get; set; }

        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    public class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = "";
    }

}