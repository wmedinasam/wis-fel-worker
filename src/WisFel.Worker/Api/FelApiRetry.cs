using System.Net;
using System.Net.Http.Headers;

namespace WisFel.Worker.Api;

public static class FelApiRetry
{
    /// <summary>
    /// Delay before the next attempt, or null when the response must not be retried.
    /// Pass a null status for a network failure.
    /// </summary>
    public static TimeSpan? NextDelay(
        HttpStatusCode? status,
        RetryConditionHeaderValue? retryAfter,
        int attempt,
        TimeSpan baseDelay,
        DateTimeOffset utcNow)
    {
        if (status is not null && !IsTransient(status.Value))
        {
            return null;
        }

        if (retryAfter is not null)
        {
            var requested = retryAfter.Delta ?? (retryAfter.Date is { } date ? date - utcNow : null);
            if (requested is { } delay)
            {
                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        var exponential = baseDelay * Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = Random.Shared.NextDouble() * 0.25 * exponential.TotalMilliseconds;
        return exponential + TimeSpan.FromMilliseconds(jitter);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
        (int)status >= 500;
}
