using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using IntunePackagingTool.Models;

namespace IntunePackagingTool.Services
{
    public class ApplicationCacheService
    {
        private static ApplicationCacheService? _instance;
        public static ApplicationCacheService Instance => _instance ??= new ApplicationCacheService();

        private List<IntuneApplication>? _cachedApplications;
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _backgroundRefreshInterval = TimeSpan.FromMinutes(10);
        private DispatcherTimer? _backgroundRefreshTimer;
        private bool _isRefreshing;

        public event EventHandler<List<IntuneApplication>>? ApplicationsUpdated;
        public event EventHandler<bool>? RefreshingChanged;

        private ApplicationCacheService()
        {
            InitializeBackgroundRefresh();
        }

        public bool HasValidCache =>
            _cachedApplications != null &&
            DateTime.Now - _lastRefresh < _cacheExpiration;

        public List<IntuneApplication>? CachedApplications => _cachedApplications;

        public async Task<List<IntuneApplication>> GetApplicationsAsync(
            IntuneService intuneService,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            // Return cached data if valid and not forcing refresh
            if (!forceRefresh && HasValidCache && _cachedApplications != null)
            {
                return new List<IntuneApplication>(_cachedApplications);
            }

            // If already refreshing, wait for current refresh to complete
            if (_isRefreshing && !forceRefresh && _cachedApplications != null)
            {
                return new List<IntuneApplication>(_cachedApplications);
            }

            try
            {
                _isRefreshing = true;
                RefreshingChanged?.Invoke(this, true);

                // Fetch fresh data
                var applications = await intuneService.GetApplicationsAsync(forceRefresh, cancellationToken);

                // Update cache
                _cachedApplications = applications;
                _lastRefresh = DateTime.Now;

                // Notify listeners
                ApplicationsUpdated?.Invoke(this, applications);

                return applications;
            }
            finally
            {
                _isRefreshing = false;
                RefreshingChanged?.Invoke(this, false);
            }
        }

        public async Task<List<IntuneApplication>> GetPagedApplicationsAsync(
            IntuneService intuneService,
            int page,
            int pageSize,
            string searchTerm = "",
            string categoryFilter = "",
            CancellationToken cancellationToken = default)
        {
            // Get all applications (from cache if available)
            var applications = await GetApplicationsAsync(intuneService, false, cancellationToken);

            // Apply filters
            var filtered = applications.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var lowerSearch = searchTerm.ToLower();
                filtered = filtered.Where(app =>
                    app.DisplayName?.ToLower().Contains(lowerSearch) == true ||
                    app.Publisher?.ToLower().Contains(lowerSearch) == true);
            }

            if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != "All Categories")
            {
                filtered = filtered.Where(app =>
                    app.Categories?.Contains(categoryFilter) == true);
            }

            // Apply pagination
            var paged = filtered
                .OrderBy(app => app.DisplayName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return paged;
        }

        public void InvalidateCache()
        {
            _cachedApplications = null;
            _lastRefresh = DateTime.MinValue;
        }

        private void InitializeBackgroundRefresh()
        {
            _backgroundRefreshTimer = new DispatcherTimer
            {
                Interval = _backgroundRefreshInterval
            };

            _backgroundRefreshTimer.Tick += async (s, e) =>
            {
                // Skip if currently refreshing or no cache exists
                if (_isRefreshing || _cachedApplications == null)
                    return;

                // Only refresh if cache is about to expire
                if (DateTime.Now - _lastRefresh > _cacheExpiration.Subtract(TimeSpan.FromMinutes(1)))
                {
                    // This will happen in background without forcing UI update
                    await RefreshInBackgroundAsync();
                }
            };

            _backgroundRefreshTimer.Start();
        }

        private async Task RefreshInBackgroundAsync()
        {
            try
            {
                // Get IntuneService instance if available
                var intuneService = ServiceLocator.GetService<IntuneService>();
                if (intuneService == null || !intuneService.IsAuthenticated)
                    return;

                // Silently refresh the cache
                await GetApplicationsAsync(intuneService, forceRefresh: true);
            }
            catch
            {
                // Silent failure for background refresh
            }
        }

        public void StartBackgroundRefresh()
        {
            _backgroundRefreshTimer?.Start();
        }

        public void StopBackgroundRefresh()
        {
            _backgroundRefreshTimer?.Stop();
        }

        public void Dispose()
        {
            StopBackgroundRefresh();
            _backgroundRefreshTimer = null;
            _cachedApplications = null;
        }
    }

    // Simple service locator for dependency injection
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> Services = new();

        public static void Register<T>(T service) where T : class
        {
            Services[typeof(T)] = service;
        }

        public static T? GetService<T>() where T : class
        {
            return Services.TryGetValue(typeof(T), out var service) ? service as T : null;
        }
    }
}