using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace IntunePackagingTool.Services
{
    /// <summary>
    /// Disk-based cache for application icons to reduce API calls and improve performance
    /// </summary>
    public class IconCacheService
    {
        private readonly string _cacheDir;
        private const long MAX_CACHE_SIZE_BYTES = 100 * 1024 * 1024; // 100MB
        private const int MAX_CACHE_DAYS = 30; // Clean icons older than 30 days

        public IconCacheService()
        {
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntunePackagingTool",
                "IconCache");

            // Create cache directory if it doesn't exist
            Directory.CreateDirectory(_cacheDir);

            // Clean old cache on startup (async fire-and-forget)
            _ = CleanOldCacheAsync();
        }

        /// <summary>
        /// Get icon from cache or create from byte array and cache it
        /// </summary>
        public async Task<BitmapImage?> GetOrCacheIconAsync(string appId, byte[]? iconData)
        {
            if (string.IsNullOrEmpty(appId))
                return null;

            var cachePath = GetCachePath(appId);

            // Try to load from cache first
            if (File.Exists(cachePath))
            {
                try
                {
                    return await LoadIconFromFileAsync(cachePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load cached icon for {appId}: {ex.Message}");
                    // Delete corrupt cache file
                    try { File.Delete(cachePath); } catch { }
                }
            }

            // If not in cache and we have data, cache it
            if (iconData != null && iconData.Length > 0)
            {
                try
                {
                    await CacheIconAsync(cachePath, iconData);
                    return await LoadIconFromFileAsync(cachePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to cache icon for {appId}: {ex.Message}");
                    // Fall back to creating from memory
                    return CreateIconFromBytes(iconData);
                }
            }

            return null;
        }

        /// <summary>
        /// Get cache file path for an app ID
        /// </summary>
        private string GetCachePath(string appId)
        {
            // Sanitize app ID for filename
            var sanitized = appId.Replace("{", "").Replace("}", "").Replace("-", "");
            return Path.Combine(_cacheDir, $"{sanitized}.png");
        }

        /// <summary>
        /// Load icon from file asynchronously
        /// </summary>
        private async Task<BitmapImage> LoadIconFromFileAsync(string path)
        {
            return await Task.Run(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load immediately and close file
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // Make thread-safe
                return bitmap;
            });
        }

        /// <summary>
        /// Cache icon data to disk
        /// </summary>
        private async Task CacheIconAsync(string cachePath, byte[] iconData)
        {
            await File.WriteAllBytesAsync(cachePath, iconData);
            Debug.WriteLine($"✓ Cached icon to disk: {Path.GetFileName(cachePath)} ({iconData.Length} bytes)");
        }

        /// <summary>
        /// Create BitmapImage from byte array (fallback for cache failures)
        /// </summary>
        private BitmapImage? CreateIconFromBytes(byte[] iconData)
        {
            try
            {
                using var ms = new MemoryStream(iconData);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create icon from bytes: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clean cache files older than MAX_CACHE_DAYS
        /// </summary>
        private async Task CleanOldCacheAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(_cacheDir, "*.png");
                    var cutoffDate = DateTime.Now.AddDays(-MAX_CACHE_DAYS);
                    var deletedCount = 0;
                    long freedBytes = 0;

                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastAccessTime < cutoffDate)
                        {
                            freedBytes += fileInfo.Length;
                            File.Delete(file);
                            deletedCount++;
                        }
                    }

                    if (deletedCount > 0)
                    {
                        Debug.WriteLine($"🧹 Cleaned {deletedCount} old icons ({freedBytes / 1024}KB freed)");
                    }

                    // Check total cache size
                    CheckCacheSizeLimit();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error cleaning icon cache: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Enforce cache size limit by deleting oldest files
        /// </summary>
        private void CheckCacheSizeLimit()
        {
            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.png");
                var totalSize = 0L;

                foreach (var file in files)
                {
                    totalSize += new FileInfo(file).Length;
                }

                if (totalSize > MAX_CACHE_SIZE_BYTES)
                {
                    Debug.WriteLine($"⚠️ Cache size ({totalSize / 1024 / 1024}MB) exceeds limit, cleaning...");

                    // Sort by last access time and delete oldest
                    var filesByAge = files
                        .Select(f => new FileInfo(f))
                        .OrderBy(f => f.LastAccessTime)
                        .ToList();

                    var bytesToDelete = totalSize - (MAX_CACHE_SIZE_BYTES * 3 / 4); // Delete to 75% capacity
                    var deletedBytes = 0L;
                    var deletedCount = 0;

                    foreach (var file in filesByAge)
                    {
                        if (deletedBytes >= bytesToDelete)
                            break;

                        deletedBytes += file.Length;
                        file.Delete();
                        deletedCount++;
                    }

                    Debug.WriteLine($"🧹 Deleted {deletedCount} oldest icons ({deletedBytes / 1024 / 1024}MB freed)");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking cache size: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all cached icons
        /// </summary>
        public void ClearCache()
        {
            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.png");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                Debug.WriteLine($"🧹 Cleared {files.Length} cached icons");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing icon cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public (int FileCount, long TotalBytes) GetCacheStats()
        {
            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.png");
                var totalBytes = files.Sum(f => new FileInfo(f).Length);
                return (files.Length, totalBytes);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
