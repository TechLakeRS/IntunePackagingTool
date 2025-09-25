// Services/WingetRestApiService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class WingetRestApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0";
        private readonly string _searchUrl = "https://storeedgefd.dsx.mp.microsoft.com/v9.0/manifestSearch";

        public event EventHandler<string> ProgressChanged;

        public WingetRestApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "winget-cli");
        }

        public async Task<List<WingetPackage>> SearchPackagesAsync(string searchTerm = "", int maxResults = 50)
        {
            var packages = new List<WingetPackage>();

            try
            {
                OnProgressChanged($"Searching for: {searchTerm}");

                // Build the search request
                var searchRequest = new
                {
                    Query = new
                    {
                        KeyWord = string.IsNullOrWhiteSpace(searchTerm) ? "Microsoft" : searchTerm,
                        MatchType = "Substring"
                    },
                    MaximumResults = maxResults,
                    Filters = new[]
                    {
                        new
                        {
                            PackageMatchField = "Market",
                            RequestMatch = new
                            {
                                KeyWord = "US",
                                MatchType = "Exact"
                            }
                        }
                    },
                    IncludeUnknown = false
                };

                var json = JsonSerializer.Serialize(searchRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Debug.WriteLine($"Sending request to: {_searchUrl}");
                Debug.WriteLine($"Request body: {json}");

                var response = await _httpClient.PostAsync(_searchUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Response received, length: {responseJson.Length}");

                    var searchResponse = JsonSerializer.Deserialize<WingetSearchResponse>(responseJson);

                    if (searchResponse?.Data != null)
                    {
                        foreach (var item in searchResponse.Data)
                        {
                            var package = ConvertToWingetPackage(item);
                            if (package != null)
                            {
                                packages.Add(package);
                            }
                        }
                    }

                    OnProgressChanged($"Found {packages.Count} packages");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"API Error: {response.StatusCode} - {error}");
                    OnProgressChanged($"Search failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SearchPackagesAsync: {ex}");
                OnProgressChanged($"Error: {ex.Message}");
            }

            return packages;
        }

        public async Task<WingetPackageDetails> GetPackageDetailsAsync(string packageId)
        {
            try
            {
                // For details, we need to get the manifest
                var manifestRequest = new
                {
                    PackageIdentifier = packageId,
                    MaximumResults = 1
                };

                var json = JsonSerializer.Serialize(manifestRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(_searchUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var searchResponse = JsonSerializer.Deserialize<WingetSearchResponse>(responseJson);

                    if (searchResponse?.Data?.Count > 0)
                    {
                        var item = searchResponse.Data[0];
                        return new WingetPackageDetails
                        {
                            PackageId = item.PackageIdentifier,
                            PackageName = item.PackageName,
                            Publisher = item.Publisher,
                            LatestVersion = item.Versions?.FirstOrDefault()?.PackageVersion ?? "Unknown",
                            Description = item.Versions?.FirstOrDefault()?.DefaultLocale?.ShortDescription ?? "",
                            Homepage = item.Versions?.FirstOrDefault()?.DefaultLocale?.PackageUrl ?? "",
                            License = item.Versions?.FirstOrDefault()?.DefaultLocale?.License ?? "",
                            InstallerUrl = GetInstallerUrl(item),
                            InstallerType = GetInstallerType(item)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting package details: {ex}");
            }

            return null;
        }

        private WingetPackage ConvertToWingetPackage(WingetSearchItem item)
        {
            try
            {
                var latestVersion = item.Versions?.FirstOrDefault();

                return new WingetPackage
                {
                    Id = item.PackageIdentifier ?? "",
                    Name = item.PackageName ?? "",
                    Publisher = item.Publisher ?? "",
                    Version = latestVersion?.PackageVersion ?? "Unknown",
                    Description = latestVersion?.DefaultLocale?.ShortDescription ?? "",
                    Homepage = latestVersion?.DefaultLocale?.PackageUrl ?? "",
                    License = latestVersion?.DefaultLocale?.License ?? "",
                    InstallerType = GetInstallerType(item),
                    Source = "Winget",
                    IsInstalled = false
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting package: {ex}");
                return null;
            }
        }

        private string GetInstallerType(WingetSearchItem item)
        {
            try
            {
                var installer = item.Versions?.FirstOrDefault()?.Installers?.FirstOrDefault();
                return installer?.InstallerType ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private string GetInstallerUrl(WingetSearchItem item)
        {
            try
            {
                var installer = item.Versions?.FirstOrDefault()?.Installers?.FirstOrDefault();
                return installer?.InstallerUrl ?? "";
            }
            catch
            {
                return "";
            }
        }

        public async Task<bool> DownloadPackageAsync(string packageId, string targetPath)
        {
            try
            {
                var details = await GetPackageDetailsAsync(packageId);
                if (details != null && !string.IsNullOrEmpty(details.InstallerUrl))
                {
                    var fileName = System.IO.Path.GetFileName(new Uri(details.InstallerUrl).LocalPath);
                    var outputPath = System.IO.Path.Combine(targetPath, fileName);

                    using (var response = await _httpClient.GetAsync(details.InstallerUrl))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Create))
                        {
                            await response.Content.CopyToAsync(fs);
                        }
                    }

                    OnProgressChanged($"Downloaded {fileName} successfully");
                    return true;
                }
            }
            catch (Exception ex)
            {
                OnProgressChanged($"Download failed: {ex.Message}");
            }

            return false;
        }

        private void OnProgressChanged(string message)
        {
            Debug.WriteLine($"WingetRestApiService: {message}");
            ProgressChanged?.Invoke(this, message);
        }
    }

    // Response models for the API
    public class WingetSearchResponse
    {
        public List<WingetSearchItem> Data { get; set; }
    }

    public class WingetSearchItem
    {
        public string PackageIdentifier { get; set; }
        public string PackageName { get; set; }
        public string Publisher { get; set; }
        public List<WingetVersionInfo> Versions { get; set; }
    }

    public class WingetVersionInfo
    {
        public string PackageVersion { get; set; }
        public WingetLocaleInfo DefaultLocale { get; set; }
        public List<WingetInstallerInfo> Installers { get; set; }
    }

    public class WingetLocaleInfo
    {
        public string PackageLocale { get; set; }
        public string Publisher { get; set; }
        public string PackageName { get; set; }
        public string License { get; set; }
        public string ShortDescription { get; set; }
        public string Description { get; set; }
        public string PackageUrl { get; set; }
    }

    public class WingetInstallerInfo
    {
        public string Architecture { get; set; }
        public string InstallerType { get; set; }
        public string InstallerUrl { get; set; }
        public string InstallerSha256 { get; set; }
        public string Scope { get; set; }
    }

    public class WingetPackageDetails
    {
        public string PackageId { get; set; }
        public string PackageName { get; set; }
        public string Publisher { get; set; }
        public string LatestVersion { get; set; }
        public string Description { get; set; }
        public string Homepage { get; set; }
        public string License { get; set; }
        public string InstallerUrl { get; set; }
        public string InstallerType { get; set; }
    }
}