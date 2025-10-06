using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IntunePackagingTool.Utilities
{
    /// <summary>
    /// Provides retry logic with exponential backoff for transient failures
    /// </summary>
    public static class RetryHelper
    {
        private const int DEFAULT_MAX_RETRIES = 3;
        private const int DEFAULT_INITIAL_DELAY_MS = 1000; // 1 second

        /// <summary>
        /// Execute an async function with retry logic
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            int maxRetries = DEFAULT_MAX_RETRIES,
            int initialDelayMs = DEFAULT_INITIAL_DELAY_MS,
            CancellationToken cancellationToken = default)
        {
            var attempt = 0;
            var delay = initialDelayMs;

            while (true)
            {
                attempt++;

                try
                {
                    return await operation();
                }
                catch (Exception ex) when (ShouldRetry(ex, attempt, maxRetries))
                {
                    Debug.WriteLine($"⚠️ Attempt {attempt}/{maxRetries} failed: {ex.Message}. Retrying in {delay}ms...");

                    await Task.Delay(delay, cancellationToken);

                    // Exponential backoff: double the delay each time (1s, 2s, 4s)
                    delay *= 2;
                }
            }
        }

        /// <summary>
        /// Execute an async action with retry logic
        /// </summary>
        public static async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            int maxRetries = DEFAULT_MAX_RETRIES,
            int initialDelayMs = DEFAULT_INITIAL_DELAY_MS,
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return 0; // Dummy return value
            }, maxRetries, initialDelayMs, cancellationToken);
        }

        /// <summary>
        /// Determine if an exception should trigger a retry
        /// </summary>
        private static bool ShouldRetry(Exception ex, int attempt, int maxRetries)
        {
            if (attempt >= maxRetries)
            {
                Debug.WriteLine($"❌ Max retries ({maxRetries}) reached. Giving up.");
                return false;
            }

            // Retry on transient network errors
            return ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is TimeoutException
                || (ex is Exception && ex.Message.Contains("429")) // Too Many Requests
                || (ex is Exception && ex.Message.Contains("503")) // Service Unavailable
                || (ex is Exception && ex.Message.Contains("504")); // Gateway Timeout
        }
    }
}
