using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IntunePackagingTool.Services
{
    public class CacheService
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache = new();

        // Add Keys property to expose cache keys
        public ICollection<string> Keys => _cache.Keys;

        private class CacheItem
        {
            public object? Value { get; set; }
            public DateTime Expiry { get; set; }
        }

        public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            if (_cache.TryGetValue(key, out var item) &&
                item.Expiry > DateTime.UtcNow &&
                item.Value is T typedValue)
            {
                return typedValue;
            }

            var result = await factory();
            var expiry = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromMinutes(5));
            _cache[key] = new CacheItem { Value = result, Expiry = expiry };
            return result;
        }

        public void Clear(string? key = null)
        {
            if (key != null)
                _cache.TryRemove(key, out _);
            else
                _cache.Clear();
        }

        public void ClearPattern(string pattern)
        {
            var keysToRemove = _cache.Keys
                .Where(k => k.Contains(pattern))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _); 
            }
        }
    }
}