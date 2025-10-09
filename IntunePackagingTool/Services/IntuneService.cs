
using IntunePackagingTool.Models;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace IntunePackagingTool.Services

{
    public class IntuneService
    {

        private static readonly Lazy<HttpClient> _SharedHttpClient = new Lazy<HttpClient>(() =>
        new HttpClient { Timeout = TimeSpan.FromMinutes(2) });

        private static HttpClient _sharedHttpClient => _SharedHttpClient.Value;

        private readonly CacheService _cache = new CacheService();
        private string? _accessToken;
        private DateTime _tokenExpiry = DateTime.MinValue;
        private bool _disposed = false;

        // Authentication credentials
        private readonly string _clientId;
        private readonly string _tenantId;
        private readonly string _certificateThumbprint;

        public string ClientId => _clientId;
        public string TenantId => _tenantId;
        public string CertificateThumbprint => _certificateThumbprint;

        public IntuneService(string tenantId, string clientId, string certificateThumbprint)
        {
            _tenantId = tenantId;
            _clientId = clientId;
            // Remove spaces and ensure uppercase for thumbprint comparison
            _certificateThumbprint = certificateThumbprint?.Replace(" ", "").ToUpperInvariant() ?? "";

            Debug.WriteLine($"IntuneService initialized with:");
            Debug.WriteLine($"  TenantId: {_tenantId}");
            Debug.WriteLine($"  ClientId: {_clientId}");
            Debug.WriteLine($"  CertificateThumbprint: {_certificateThumbprint}");
        }

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

            // ✅ FIX: Don't mutate shared HttpClient headers - we'll add headers per-request instead
            // This prevents memory leaks and threading issues with the static HttpClient

            return _accessToken;
        }

        /// <summary>
        /// Creates an HttpRequestMessage with authentication headers
        /// This avoids mutating the shared HttpClient's DefaultRequestHeaders
        /// </summary>
        private async Task<HttpRequestMessage> CreateAuthenticatedRequestAsync(HttpMethod method, string url)
        {
            var token = await GetAccessTokenAsync();
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private X509Certificate2 LoadCertificate()
        {
            Debug.WriteLine($"Looking for certificate with thumbprint: {_certificateThumbprint}");

            X509Certificate2Collection certificates;

            // Try CurrentUser\My first
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                Debug.WriteLine($"Searching CurrentUser\\My store, found {store.Certificates.Count} total certificates");

                // Debug: List all certificate thumbprints in CurrentUser\My
                foreach (var storeCert in store.Certificates)
                {
                    Debug.WriteLine($"  - Certificate: Subject={storeCert.Subject}, Thumbprint={storeCert.Thumbprint}");
                }

                certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
                Debug.WriteLine($"Found {certificates.Count} matching certificates in CurrentUser\\My");
            } // Store automatically disposed here

            // If not found, try LocalMachine\My
            if (certificates.Count == 0)
            {
                using (var store = new X509Store(StoreName.My, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadOnly);
                    Debug.WriteLine($"Searching LocalMachine\\My store, found {store.Certificates.Count} total certificates");

                    // Debug: List all certificate thumbprints in LocalMachine\My
                    foreach (var storeCert in store.Certificates)
                    {
                        Debug.WriteLine($"  - Certificate: Subject={storeCert.Subject}, Thumbprint={storeCert.Thumbprint}");
                    }

                    certificates = store.Certificates.Find(X509FindType.FindByThumbprint, _certificateThumbprint, false);
                    Debug.WriteLine($"Found {certificates.Count} matching certificates in LocalMachine\\My");
                } // Store automatically disposed here
            }

            if (certificates.Count == 0)
            {
                throw new Exception($"Certificate with thumbprint {_certificateThumbprint} not found in CurrentUser\\My or LocalMachine\\My stores");
            }

            var cert = certificates[0];
            Debug.WriteLine($"Certificate found: Subject={cert.Subject}, HasPrivateKey={cert.HasPrivateKey}");

            if (!cert.HasPrivateKey)
            {
                throw new Exception($"Certificate {_certificateThumbprint} found but does not have a private key. Cannot authenticate.");
            }

            return cert;
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

        public async Task<List<IntuneApplication>> GetApplicationsAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (forceRefresh)
            {
                _cache.Clear("apps_list");
            }

            return await _cache.GetOrAddAsync("apps_list",
                async () => await GetAllApplicationsFromGraphAsync(cancellationToken),
                TimeSpan.FromMinutes(10));
        }

        private async Task<List<IntuneApplication>> GetAllApplicationsFromGraphAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine("=== FETCHING ALL APPLICATIONS FROM GRAPH ===");

                var token = await GetAccessTokenAsync();

                var apps = new List<IntuneApplication>();

                // Start with first page
                var requestUrl = "https://graph.microsoft.com/beta/deviceAppManagement/mobileApps" +
                                "?$filter=isof('microsoft.graph.win32LobApp')" +
                                "&$expand=categories" +
                                "&$top=100" +  // Get 100 at a time
                                "&$orderby=displayName";

                while (!string.IsNullOrEmpty(requestUrl))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Debug.WriteLine($"Fetching batch from: {requestUrl}");

                    // Use retry logic for Graph API calls (handles transient failures)
                    var (response, responseText) = await Utilities.RetryHelper.ExecuteWithRetryAsync(async () =>
                    {
                        using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                        var resp = await _sharedHttpClient.SendAsync(request, cancellationToken);
                        var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                        return (resp, text);
                    }, maxRetries: 3, initialDelayMs: 1000, cancellationToken);

                    if (!response.IsSuccessStatusCode)
                    {
                        // Try without categories expand if it fails
                        requestUrl = requestUrl.Replace("&$expand=categories", "");

                        var (retryResponse, retryResponseText) = await Utilities.RetryHelper.ExecuteWithRetryAsync(async () =>
                        {
                            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                            var resp = await _sharedHttpClient.SendAsync(request, cancellationToken);
                            var text = await resp.Content.ReadAsStringAsync(cancellationToken);
                            return (resp, text);
                        }, maxRetries: 3, initialDelayMs: 1000, cancellationToken);

                        response = retryResponse;
                        responseText = retryResponseText;

                        if (!response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"Failed to fetch applications: {response.StatusCode}");
                            throw new Exception($"Failed to fetch applications: {response.StatusCode}");
                        }
                    }

                    var graphResponse = JsonSerializer.Deserialize<GraphResponse<JsonElement>>(responseText,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                    if (graphResponse?.Value != null)
                    {
                        foreach (var app in graphResponse.Value)
                        {
                            apps.Add(ParseIntuneApplication(app));
                        }

                        Debug.WriteLine($"Fetched {graphResponse.Value.Count} apps, total so far: {apps.Count}");
                    }

                    // Get next page URL if exists
                    requestUrl = graphResponse?.ODataNextLink ?? "";
                }

                Debug.WriteLine($"✓ Successfully fetched {apps.Count} total applications");

                return apps.OrderBy(a => a.DisplayName).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching all applications: {ex.Message}");
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

                using var appRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Get, appUrl);
                var appResponse = await _sharedHttpClient.SendAsync(appRequest);
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
                        InstallContext = "System",

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
                    if (appJson.TryGetProperty("installExperience", out var installExpProp) && installExpProp.ValueKind != JsonValueKind.Null)
                    {
                        if (installExpProp.TryGetProperty("runAsAccount", out var runAsAccountProp))
                        {
                            var runAsAccount = runAsAccountProp.GetString();
                            // The API returns "system" or "user" - normalize the casing
                            appDetail.InstallContext = runAsAccount?.Equals("user", StringComparison.OrdinalIgnoreCase) == true ? "User" : "System";
                            Debug.WriteLine($"✓ Install context: {appDetail.InstallContext} (from runAsAccount: {runAsAccount})");
                        }

                        // While we're here, you could also extract other installExperience properties if needed:
                        if (installExpProp.TryGetProperty("deviceRestartBehavior", out var restartProp))
                        {
                            // You could add a DeviceRestartBehavior property to ApplicationDetail if needed
                            Debug.WriteLine($"Device restart behavior: {restartProp.GetString()}");
                        }
                    }
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

                await Task.WhenAll(assignmentsTask, categoriesTask).ConfigureAwait(false);

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
        
        private IntuneApplication ParseIntuneApplication(JsonElement app)
        {
            var id = app.GetProperty("id").GetString() ?? "";
            var displayName = app.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "Unknown" : "Unknown";
            var version = app.TryGetProperty("displayVersion", out var verProp) ? verProp.GetString() ?? "1.0.0" : "1.0.0";
            var publisher = app.TryGetProperty("publisher", out var pubProp) ? pubProp.GetString() ?? "Unknown" : "Unknown";

            // Category parsing logic
            var category = "Uncategorized";
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

            return new IntuneApplication
            {
                Id = id,
                DisplayName = displayName,
                Version = version,
                Publisher = publisher,
                Category = category,
                LastModified = DateTime.Now
            };
        }
        
        public void InvalidateApplicationCache(string appId)
        {
            _cache.Clear($"app_detail_{appId}");

            // Also clear any list caches that might contain this app
            _cache.ClearPattern("apps_list");
            _cache.ClearPattern("apps_paged");
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

        public async Task<bool> AssignTestCategoryToAppAsync(string appId)
        {
            try
            {
                var token = await GetAccessTokenAsync();

                // First, get the category ID for "Test"
                var categoriesUrl = "https://graph.microsoft.com/beta/deviceAppManagement/mobileAppCategories";
                using var getCatRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Get, categoriesUrl);
                var response = await _sharedHttpClient.SendAsync(getCatRequest);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("Failed to get categories");
                    return false;
                }

                var responseText = await response.Content.ReadAsStringAsync();
                var json = JsonSerializer.Deserialize<JsonElement>(responseText);

                string? testCategoryId = null;
                if (json.TryGetProperty("value", out var categories))
                {
                    foreach (var category in categories.EnumerateArray())
                    {
                        if (category.TryGetProperty("displayName", out var name) &&
                            name.GetString() == "Test")
                        {
                            testCategoryId = category.GetProperty("id").GetString();
                            Debug.WriteLine($"Found Test category with ID: {testCategoryId}");
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(testCategoryId))
                {
                    Debug.WriteLine("Test category not found in Intune");
                    return false;
                }

                // Assign the Test category to the app
                var assignUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/categories/$ref";
                var assignPayload = new Dictionary<string, object>
                {
                    ["@odata.id"] = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileAppCategories/{testCategoryId}"
                };

                var assignContent = new StringContent(
                    JsonSerializer.Serialize(assignPayload),
                    Encoding.UTF8,
                    "application/json");

                using var postRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Post, assignUrl);
                postRequest.Content = assignContent;
                var assignResponse = await _sharedHttpClient.SendAsync(postRequest);

                if (assignResponse.IsSuccessStatusCode)
                {
                    Debug.WriteLine("✓ Successfully assigned Test category to app");
                    return true;
                }
                else
                {
                    var errorText = await assignResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Failed to assign category: {errorText}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error assigning Test category: {ex.Message}");
                return false;
            }
        }

        public async Task<string> CreateOrGetGroupAsync(string displayName, string description, List<string> ownerObjectIds)
        {
            try
            {
                var token = await GetAccessTokenAsync();

                // First check if group exists
                var checkUrl = $"https://graph.microsoft.com/beta/groups?$filter=displayName eq '{Uri.EscapeDataString(displayName)}'";
                using var checkRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Get, checkUrl);
                var checkResponse = await _sharedHttpClient.SendAsync(checkRequest);

                if (checkResponse.IsSuccessStatusCode)
                {
                    var checkContent = await checkResponse.Content.ReadAsStringAsync();
                    var checkJson = JsonSerializer.Deserialize<JsonElement>(checkContent);

                    if (checkJson.TryGetProperty("value", out var groups) &&
                        groups.GetArrayLength() > 0)
                    {
                        // Group exists, return its ID
                        var existingGroupId = groups[0].GetProperty("id").GetString();
                        if (existingGroupId == null)
                        {
                            throw new InvalidOperationException($"Group '{displayName}' exists but has no ID");
                        }

                        return existingGroupId;
                    }
                }

                // Create new group with owners
                var ownerBindings = ownerObjectIds
                    .Select(id => $"https://graph.microsoft.com/v1.0/users/{id}")
                    .ToArray();

                var groupPayload = new Dictionary<string, object>
                {
                    ["displayName"] = displayName,
                    ["description"] = description,
                    ["mailEnabled"] = false,
                    ["mailNickname"] = Guid.NewGuid().ToString("N").Substring(0, 10),
                    ["securityEnabled"] = true,
                    ["groupTypes"] = new string[] { },
                    ["owners@odata.bind"] = ownerBindings
                };

                var createUrl = "https://graph.microsoft.com/beta/groups";
                var content = new StringContent(JsonSerializer.Serialize(groupPayload),
                    System.Text.Encoding.UTF8, "application/json");

                using var createRequest = await CreateAuthenticatedRequestAsync(HttpMethod.Post, createUrl);
                createRequest.Content = content;
                var createResponse = await _sharedHttpClient.SendAsync(createRequest);
                var responseContent = await createResponse.Content.ReadAsStringAsync();

                if (!createResponse.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to create group: {createResponse.StatusCode} - {responseContent}");
                }

                var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
                var groupId = responseJson.GetProperty("id").GetString();

                if (groupId == null)
                {
                    throw new InvalidOperationException($"Created group '{displayName}' but response contained no ID. Response: {responseContent}");
                }

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

                // Load owner IDs from settings
                var settingsService = new SettingsService();
                var ownerIds = settingsService.Settings.GroupAssignment.OwnerIds;

                // Create install assignments with owners
                if (!string.IsNullOrEmpty(groupIds.SystemInstallId))
                {
                    await CreateAssignment(appId, groupIds.SystemInstallId, "required");
                }
                else
                {
                    // Create group with owners
                    groupIds.SystemInstallId = await CreateOrGetGroupAsync(
                        "System Install Group",
                        "Group for system-level installations",
                        ownerIds
                    );
                    await CreateAssignment(appId, groupIds.SystemInstallId, "required");
                }

                if (!string.IsNullOrEmpty(groupIds.UserInstallId))
                {
                    await CreateAssignment(appId, groupIds.UserInstallId, "required");
                }
                else
                {
                    groupIds.UserInstallId = await CreateOrGetGroupAsync(
                        "User Install Group",
                        "Group for user-level installations",
                        ownerIds
                    );
                    await CreateAssignment(appId, groupIds.UserInstallId, "required");
                }

                // Create uninstall assignments with owners
                if (!string.IsNullOrEmpty(groupIds.SystemUninstallId))
                {
                    await CreateAssignment(appId, groupIds.SystemUninstallId, "uninstall");
                }
                else
                {
                    groupIds.SystemUninstallId = await CreateOrGetGroupAsync(
                        "System Uninstall Group",
                        "Group for system-level uninstallations",
                        ownerIds
                    );
                    await CreateAssignment(appId, groupIds.SystemUninstallId, "uninstall");
                }

                if (!string.IsNullOrEmpty(groupIds.UserUninstallId))
                {
                    await CreateAssignment(appId, groupIds.UserUninstallId, "uninstall");
                }
                else
                {
                    groupIds.UserUninstallId = await CreateOrGetGroupAsync(
                        "User Uninstall Group",
                        "Group for user-level uninstallations",
                        ownerIds
                    );
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
            var assignmentPayload = new Dictionary<string, object?>
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

            using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, url);
            request.Content = content;
            var response = await _sharedHttpClient.SendAsync(request);

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

        public async Task<GroupDevice> FindDeviceByNameAsync(string deviceName)
        {
            try
            {
                Debug.WriteLine($"[FindDeviceByNameAsync] Searching for device: {deviceName}");

                var requestUrl = $"https://graph.microsoft.com/v1.0/devices?$filter=displayName eq '{deviceName}'";
                Debug.WriteLine($"[FindDeviceByNameAsync] Request URL: {requestUrl}");

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                var response = await _sharedHttpClient.SendAsync(request);
                Debug.WriteLine($"[FindDeviceByNameAsync] Response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var jsonDoc = JsonDocument.Parse(responseText);

                    if (jsonDoc.RootElement.TryGetProperty("value", out var valueArray) &&
                        valueArray.GetArrayLength() > 0)
                    {
                        var deviceElement = valueArray[0];
                        var device = new GroupDevice
                        {
                            Id = GetStringValue(deviceElement, "id"),
                            DeviceName = GetStringValue(deviceElement, "displayName"),
                            OperatingSystem = GetStringValue(deviceElement, "operatingSystem"),
                            // Note: Azure AD devices don't have userPrincipalName, compliance, or lastSync
                        };
                        Debug.WriteLine($"[FindDeviceByNameAsync] Found device: {device.DeviceName} (ID: {device.Id})");
                        return device;
                    }
                }

                Debug.WriteLine("[FindDeviceByNameAsync] Device not found");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FindDeviceByNameAsync] EXCEPTION: {ex.Message}");
                throw;
            }
        }

        public async Task<bool> IsDeviceInGroupAsync(string deviceId, string groupId)
        {
            try
            {
                await GetAccessTokenAsync();

                var requestUrl = $"https://graph.microsoft.com/beta/groups/{groupId}/members/{deviceId}";

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                var response = await _sharedHttpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking device membership: {ex.Message}");
                return false;
            }
        }

        public async Task AddDeviceToGroupAsync(string deviceId, string groupId)
        {
            try
            {
                Debug.WriteLine($"[AddDeviceToGroupAsync] Starting - DeviceId: {deviceId}, GroupId: {groupId}");

                await GetAccessTokenAsync();
                Debug.WriteLine("[AddDeviceToGroupAsync] Access token obtained");

                var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members/$ref";
                Debug.WriteLine($"[AddDeviceToGroupAsync] Request URL: {requestUrl}");

                var odataId = $"https://graph.microsoft.com/v1.0/directoryObjects/{deviceId}";
                Debug.WriteLine($"[AddDeviceToGroupAsync] OData.id value: {odataId}");

                // Create JSON manually to handle @odata.id properly
                var jsonPayload = $"{{\"@odata.id\":\"{odataId}\"}}";
                Debug.WriteLine($"[AddDeviceToGroupAsync] JSON Payload: {jsonPayload}");

                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                Debug.WriteLine($"[AddDeviceToGroupAsync] Sending POST request...");
                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, requestUrl);
                request.Content = content;
                var response = await _sharedHttpClient.SendAsync(request);

                Debug.WriteLine($"[AddDeviceToGroupAsync] Response Status Code: {(int)response.StatusCode} ({response.StatusCode})");

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"[AddDeviceToGroupAsync] ERROR Response Body: {error}");
                    throw new Exception($"Failed to add device to group. Status: {response.StatusCode}, Error: {error}");
                }

                Debug.WriteLine("[AddDeviceToGroupAsync] Device successfully added to group");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AddDeviceToGroupAsync] EXCEPTION: {ex.GetType().Name}");
                Debug.WriteLine($"[AddDeviceToGroupAsync] Message: {ex.Message}");
                Debug.WriteLine($"[AddDeviceToGroupAsync] StackTrace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task RemoveDeviceFromGroupAsync(string groupId, string deviceId)
        {
            try
            {
                await GetAccessTokenAsync();

                var requestUrl = $"https://graph.microsoft.com/beta/groups/{groupId}/members/{deviceId}/$ref";

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Delete, requestUrl);
                var response = await _sharedHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to remove device from group: {error}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing device from group: {ex.Message}");
                throw;
            }
        }

        public async Task RemoveDevicesFromGroupAsync(string groupId, List<string> deviceIds)
        {
            var tasks = new List<Task>();
            const int batchSize = 5;

            for (int i = 0; i < deviceIds.Count; i += batchSize)
            {
                var batch = deviceIds.Skip(i).Take(batchSize);

                foreach (var deviceId in batch)
                {
                    tasks.Add(RemoveDeviceFromGroupAsync(groupId, deviceId));
                }

                await Task.WhenAll(tasks);
                tasks.Clear();

                if (i + batchSize < deviceIds.Count)
                {
                    await Task.Delay(500);
                }
            }
        }

        public async Task<List<GroupDevice>> GetGroupMembersAsync(string groupId)
        {
            try
            {
                await GetAccessTokenAsync();
                var members = new List<GroupDevice>();

                Debug.WriteLine($"Fetching members for group: {groupId}");

                var requestUrl = $"https://graph.microsoft.com/v1.0/groups/{groupId}/members";

                while (!string.IsNullOrEmpty(requestUrl))
                {
                    using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                    var response = await _sharedHttpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        Debug.WriteLine($"Members response: {responseText.Substring(0, Math.Min(500, responseText.Length))}");

                        var jsonDoc = JsonDocument.Parse(responseText);

                        if (jsonDoc.RootElement.TryGetProperty("value", out var valueArray))
                        {
                            foreach (var member in valueArray.EnumerateArray())
                            {
                                var memberType = GetStringValue(member, "@odata.type");

                                if (memberType == "#microsoft.graph.device")
                                {
                                    // Just parse the device information we have!
                                    var device = new GroupDevice
                                    {
                                        Id = GetStringValue(member, "id"),
                                        DeviceName = GetStringValue(member, "displayName"),
                                        // The deviceId field is the Azure AD device ID
                                        AzureDeviceId = GetStringValue(member, "deviceId"),
                                        OperatingSystem = GetStringValue(member, "operatingSystem"),
                                        IsCompliant = member.TryGetProperty("isCompliant", out var compliantProp)
                                            && compliantProp.GetBoolean(),
                                        LastSyncDateTime = GetSafeDateTime(member, "approximateLastSignInDateTime"),
                                        CreatedDateTime = GetSafeDateTime(member, "createdDateTime"),
                                        AccountEnabled = member.TryGetProperty("accountEnabled", out var enabledProp)
                                            && enabledProp.GetBoolean()
                                    };

                                    // Try to get OS version from deviceVersion if available
                                    if (member.TryGetProperty("deviceVersion", out var versionProp))
                                    {
                                        device.OSVersion = versionProp.ValueKind == JsonValueKind.Number
                                            ? versionProp.GetInt32().ToString()
                                            : GetStringValue(member, "deviceVersion");
                                    }

                                   

                                    Debug.WriteLine($"Added device: {device.DeviceName} (ID: {device.Id})");
                                    members.Add(device);
                                }
                            }
                        }

                        // Check for next page
                        if (jsonDoc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp))
                        {
                            requestUrl = nextLinkProp.GetString();
                        }
                        else
                        {
                            requestUrl = null;
                        }
                    }
                    else
                    {
                        throw new Exception($"Failed to get group members: {response.StatusCode}");
                    }
                }

                Debug.WriteLine($"Total devices found: {members.Count}");
                return members;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting group members: {ex.Message}");
                throw;
            }
        }

        public async Task<List<GroupDevice>> GetAllManagedDevicesAsync()
        {
            try
            {
                await GetAccessTokenAsync();

                var devices = new List<GroupDevice>();
                var requestUrl = "https://graph.microsoft.com/beta/deviceManagement/managedDevices";

                while (!string.IsNullOrEmpty(requestUrl))
                {
                    using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, requestUrl);
                    var response = await _sharedHttpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseText = await response.Content.ReadAsStringAsync();
                        var jsonDoc = JsonDocument.Parse(responseText);

                        if (jsonDoc.RootElement.TryGetProperty("value", out var valueArray))
                        {
                            foreach (var deviceElement in valueArray.EnumerateArray())
                            {
                                var device = new GroupDevice
                                {
                                    Id = GetStringValue(deviceElement, "id"),
                                    DeviceName = GetStringValue(deviceElement, "deviceName"),
                                    UserPrincipalName = GetStringValue(deviceElement, "userPrincipalName"),
                                    OperatingSystem = GetStringValue(deviceElement, "operatingSystem"),
                                    IsCompliant = deviceElement.TryGetProperty("complianceState", out var complianceProp)
                                        && complianceProp.GetString() == "compliant",
                                    LastSyncDateTime = GetSafeDateTime(deviceElement, "lastSyncDateTime")
                                };

                                devices.Add(device);
                            }
                        }

                        if (jsonDoc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkProp))
                        {
                            requestUrl = nextLinkProp.GetString();
                        }
                        else
                        {
                            requestUrl = null;
                        }
                    }
                    else
                    {
                        throw new Exception($"Failed to get managed devices: {response.StatusCode}");
                    }
                }

                return devices;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting all managed devices: {ex.Message}");
                throw;
            }
        }

        private string GetStringValue(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                return prop.GetString() ?? "";
            }
            return "";
        }
        private async Task<List<AssignedGroup>> GetAssignedGroupsAsync(string appId)
        {
            var groups = new List<AssignedGroup>();

            try
            {
                var assignmentsUrl = $"https://graph.microsoft.com/beta/deviceAppManagement/mobileApps/{appId}/assignments";
                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, assignmentsUrl);
                var response = await _sharedHttpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var json = JsonSerializer.Deserialize<JsonElement>(responseText);

                    if (json.TryGetProperty("value", out var assignmentsArray) && assignmentsArray.ValueKind == JsonValueKind.Array)
                    {
                        // First pass: collect all assignments with their group IDs
                        var groupsNeedingNames = new List<(AssignedGroup group, string groupId)>();

                        foreach (var assignment in assignmentsArray.EnumerateArray())
                        {
                            var intent = assignment.TryGetProperty("intent", out var intentProp) ? intentProp.GetString() : "Unknown";
                            var groupName = "Unknown Group";
                            var groupId = "";

                            if (assignment.TryGetProperty("target", out var targetObj))
                            {
                                var targetType = targetObj.TryGetProperty("@odata.type", out var targetTypeProp) ? targetTypeProp.GetString() : "";

                                switch (targetType)
                                {
                                    case "#microsoft.graph.groupAssignmentTarget":
                                        if (targetObj.TryGetProperty("groupId", out var groupIdProp))
                                        {
                                            groupId = groupIdProp.GetString();
                                            if (!string.IsNullOrEmpty(groupId))
                                            {
                                                // Create the group with temporary name
                                                var group = new AssignedGroup
                                                {
                                                    GroupId = groupId,
                                                    GroupName = $"Group ID: {groupId}", // Temporary name
                                                    AssignmentType = intent ?? "Unknown"
                                                };
                                                groups.Add(group);
                                                groupsNeedingNames.Add((group, groupId));
                                            }
                                        }
                                        break;

                                    case "#microsoft.graph.allLicensedUsersAssignmentTarget":
                                        groups.Add(new AssignedGroup
                                        {
                                            GroupId = "",
                                            GroupName = "All Licensed Users",
                                            AssignmentType = intent ?? "Unknown"
                                        });
                                        break;

                                    case "#microsoft.graph.allDevicesAssignmentTarget":
                                        groups.Add(new AssignedGroup
                                        {
                                            GroupId = "",
                                            GroupName = "All Devices",
                                            AssignmentType = intent ?? "Unknown"
                                        });
                                        break;

                                    default:
                                        groups.Add(new AssignedGroup
                                        {
                                            GroupId = "",
                                            GroupName = targetType?.Replace("#microsoft.graph.", "") ?? "Unknown Target",
                                            AssignmentType = intent ?? "Unknown"
                                        });
                                        break;
                                }
                            }
                        }

                        // Second pass: fetch all group names in parallel
                        if (groupsNeedingNames.Any())
                        {
                            var nameTasks = groupsNeedingNames.Select(async item =>
                            {
                                try
                                {
                                    var name = await GetGroupNameAsync(item.groupId).ConfigureAwait(false);
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        item.group.GroupName = name;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Failed to get name for group {item.groupId}: {ex.Message}");
                                    // Keep the temporary name if we can't get the real one
                                }
                            });

                            await Task.WhenAll(nameTasks).ConfigureAwait(false);
                        }
                    }
                }

                if (groups.Count == 0)
                {
                    groups.Add(new AssignedGroup
                    {
                        GroupId = "",
                        GroupName = "No Assignments",
                        AssignmentType = "Not assigned to any groups"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting assigned groups: {ex.Message}");
                groups.Add(new AssignedGroup
                {
                    GroupId = "",
                    GroupName = "Error Loading Groups",
                    AssignmentType = "Could not load assignments"
                });
            }

            return groups;
        }

        private async Task<string?> GetGroupNameAsync(string groupId)
        {
            try
            {
                var groupUrl = $"https://graph.microsoft.com/beta/groups/{groupId}?$select=displayName";
                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, groupUrl);
                var response = await _sharedHttpClient!.SendAsync(request);

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
                Debug.WriteLine($"Fetching categories from: {categoriesUrl}");

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Get, categoriesUrl);
                var response = await _sharedHttpClient!.SendAsync(request);

                Debug.WriteLine($"Category response status: {response.StatusCode}");

                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Category response: {responseText}");

                    var json = JsonSerializer.Deserialize<JsonElement>(responseText);

                    if (json.TryGetProperty("value", out var categoriesArray) && categoriesArray.ValueKind == JsonValueKind.Array)
                    {
                        var categoryNames = new List<string>();
                        var categoryCount = categoriesArray.GetArrayLength();
                        Debug.WriteLine($"Found {categoryCount} categories for app {appId}");

                        foreach (var category in categoriesArray.EnumerateArray())
                        {
                            if (category.TryGetProperty("displayName", out var catNameProp))
                            {
                                var catName = catNameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(catName))
                                {
                                    Debug.WriteLine($"  - Category: {catName}");
                                    categoryNames.Add(catName);
                                }
                            }
                        }

                        var result = categoryNames.Count > 0 ? string.Join(", ", categoryNames) : "Uncategorized";
                        Debug.WriteLine($"✓ Final category result: {result}");
                        return result;
                    }
                    else
                    {
                        Debug.WriteLine($"⚠️ No 'value' property or not an array in category response");
                    }
                }
                else
                {
                    var errorText = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"❌ Category fetch failed: {response.StatusCode} - {errorText}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error getting categories for {appId}: {ex.Message}");
                Debug.WriteLine($"   Stack trace: {ex.StackTrace}");
            }

            Debug.WriteLine($"Returning 'Uncategorized' for app {appId}");
            return "Uncategorized";
        }
        public async Task<InstallationStatistics> GetInstallationStatisticsAsync(string appId)
        {
            try
            {
                var reportUrl = "https://graph.microsoft.com/beta/deviceManagement/reports/getAppStatusOverviewReport";

                var requestBody = new
                {
                    filter = $"(ApplicationId eq '{appId}')"
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, reportUrl);
                request.Content = content;
                var response = await _sharedHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Failed to get report: {response.StatusCode}");
                    return new InstallationStatistics();
                }

                var reportContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Statistics Response: {reportContent}");

                var data = JsonDocument.Parse(reportContent);

                if (data.RootElement.TryGetProperty("Values", out var values))
                {
                    foreach (var row in values.EnumerateArray())
                    {
                        var rowData = row.EnumerateArray().ToList();

                        // Based on the actual schema from your response:
                        // [0] ApplicationId
                        // [1] FailedDeviceCount
                        // [2] PendingInstallDeviceCount  
                        // [3] InstalledDeviceCount
                        // [4] NotInstalledDeviceCount
                        // [5] NotApplicableDeviceCount

                        if (rowData.Count >= 6)
                        {
                            var stats = new InstallationStatistics
                            {
                                FailedInstalls = GetSafeInt(rowData[1]),      // FailedDeviceCount
                                PendingInstalls = GetSafeInt(rowData[2]),     // PendingInstallDeviceCount
                                SuccessfulInstalls = GetSafeInt(rowData[3]),  // InstalledDeviceCount
                                NotInstalled = GetSafeInt(rowData[4]),        // NotInstalledDeviceCount
                            };

                            var notApplicable = GetSafeInt(rowData[5]);       // NotApplicableDeviceCount

                            // Calculate total devices
                            stats.TotalDevices = stats.SuccessfulInstalls + stats.FailedInstalls +
                                                stats.PendingInstalls + stats.NotInstalled + notApplicable;

                            Debug.WriteLine($"Found stats for {appId}:");
                            Debug.WriteLine($"  Installed: {stats.SuccessfulInstalls}");
                            Debug.WriteLine($"  Failed: {stats.FailedInstalls}");
                            Debug.WriteLine($"  Pending: {stats.PendingInstalls}");
                            Debug.WriteLine($"  NotInstalled: {stats.NotInstalled}");
                            Debug.WriteLine($"  NotApplicable: {notApplicable}");
                            Debug.WriteLine($"  Total: {stats.TotalDevices}");

                            return stats;
                        }
                    }
                }

                Debug.WriteLine($"No statistics found for app {appId}");
                return new InstallationStatistics();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting statistics: {ex.Message}");
                return new InstallationStatistics();
            }
        }

        
        private static int GetSafeInt(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt32();
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                if (int.TryParse(element.GetString(), out var value))
                    return value;
            }
            return 0;
        }
        public async Task<List<DeviceInstallStatus>> GetApplicationInstallStatusAsync(string appId)
        {
            try
            {
                var allStatuses = new List<DeviceInstallStatus>();
                var reportUrl = "https://graph.microsoft.com/beta/deviceManagement/reports/retrieveDeviceAppInstallationStatusReport";

                var requestBody = new
                {
                    filter = $"(ApplicationId eq '{appId}')"  // Properly formatted filter
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                using var request = await CreateAuthenticatedRequestAsync(HttpMethod.Post, reportUrl);
                request.Content = content;
                var response = await _sharedHttpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Failed to get device report: {response.StatusCode}");
                    return allStatuses;
                }

                var reportContent = await response.Content.ReadAsStringAsync();
                var data = JsonDocument.Parse(reportContent);

                if (data.RootElement.TryGetProperty("Values", out var values))
                {
                    foreach (var row in values.EnumerateArray())
                    {
                        var rowData = row.EnumerateArray().ToList();

                        if (rowData.Count >= 18)
                        {
                            var status = new DeviceInstallStatus
                            {
                                DeviceId = GetStringValue(rowData[0]),
                                DeviceName = GetStringValue(rowData[3]),
                                UserPrincipalName = GetStringValue(rowData[4]),
                                UserName = GetStringValue(rowData[5]),
                                Platform = GetStringValue(rowData[6]),
                                ErrorCode = rowData[8].ValueKind == JsonValueKind.Number ? rowData[8].GetInt32() : null,
                                InstallState = GetInstallStateString(rowData[15]),  // ✅ FIXED: Use GetInstallStateString
                                InstallStateDetail = GetStringValue(rowData[16]),
                            };

                            // Add hex error code if there's an error
                            if (status.ErrorCode.HasValue && status.ErrorCode != 0)
                            {
                                status.HexErrorCode = $"0x{status.ErrorCode.Value:X8}";
                            }

                            if (rowData[11].ValueKind == JsonValueKind.String)
                            {
                                if (DateTime.TryParse(rowData[11].GetString(), out var dt))
                                {
                                    status.LastSyncDateTime = dt;
                                }
                            }

                            allStatuses.Add(status);
                        }
                    }
                }

                var deduplicatedStatuses = allStatuses
              .GroupBy(s => s.DeviceName)
              .Select(group => group
                  .OrderByDescending(s => s.LastSyncDateTime)  // Most recent first
                  .ThenBy(s => string.IsNullOrEmpty(s.UserPrincipalName) ? 0 : 1)  // Prefer user records over system
                  .First())
              .ToList();

                Debug.WriteLine($"Deduplicated from {allStatuses.Count} to {deduplicatedStatuses.Count} records");

                return deduplicatedStatuses;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching device status: {ex.Message}");
                return new List<DeviceInstallStatus>();
            }
        }

        private string GetStringValue(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? "" : "";
        }

        private string GetInstallStateString(JsonElement element)
        {
            
            if (element.ValueKind == JsonValueKind.String)
            {
                var stringValue = element.GetString()?.ToLower() ?? "";

               
                return stringValue switch
                {
                    "installed" => "Installed",
                    "failed" => "Failed",
                    "pending" => "Pending",
                    "notinstalled" => "NotInstalled",
                    "notapplicable" => "NotApplicable",
                    "uninstallfailed" => "UninstallFailed",
                    _ => stringValue // Return as-is if not recognized
                };
            }

            
            if (element.ValueKind == JsonValueKind.Number)
            {
                var stateValue = element.GetInt32();
                return stateValue switch
                {
                    0 => "NotApplicable",
                    1 => "Installed",
                    2 => "Failed",
                    3 => "Pending",
                    4 => "NotInstalled",
                    5 => "UninstallFailed",
                    _ => $"Unknown ({stateValue})"
                };
            }

            // Handle null or other types
            return "Unknown";
        }

        // Dispose method to clean up HttpClient
        

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