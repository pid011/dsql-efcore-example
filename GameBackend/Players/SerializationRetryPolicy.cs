using Polly;
using Polly.Retry;

namespace GameBackend.Players;

internal static class SerializationRetryPolicy
{
    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(PostgresErrorClassifier.IsSerializationFailure)
        .WaitAndRetryAsync(
            retryCount: PlayerApiConstants.MaxSerializationRetryCount - 1,
            sleepDurationProvider: retryAttempt =>
            {
                var baseDelayMs = 20 * (1 << (retryAttempt - 1));
                var jitterMs = Random.Shared.Next(0, 25);
                return TimeSpan.FromMilliseconds(baseDelayMs + jitterMs);
            });

    public static Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        return RetryPolicy.ExecuteAsync((_, token) => action(token), new Context(), cancellationToken);
    }

    public static Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        return RetryPolicy.ExecuteAsync((_, token) => action(token), new Context(), cancellationToken);
    }
}
