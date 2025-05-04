using System.Diagnostics;
using LiteRetry.Core.Retrying.Application.Enums;
using LiteRetry.Core.Retrying.Domain;

namespace LiteRetry.Core.Retrying.Application;

public static class RetryExecutor
{
    #region Properties

    private static readonly Random _jitterer = new();

    #endregion Properties

    #region Public

    public static async Task<RetryResult<T>> ExecuteAsync<T>
    (
        Func<CancellationToken, Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? baseDelay = null,
        DelayStrategy delayStrategy = DelayStrategy.Fixed,
        Func<Exception, bool>? shouldRetry = null,
        Func<RetryContext, Task>? onRetryAsync = null,
        CancellationToken cancellationToken = default
    )
    {
        if (maxAttempts < 1)
            maxAttempts = 1;

        TimeSpan effectiveBaseDelay = baseDelay ?? TimeSpan.FromMilliseconds(200);
        if (baseDelay < TimeSpan.Zero)
            effectiveBaseDelay = TimeSpan.FromMilliseconds(200);

        int attempt = 0;
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        DateTimeOffset operationStartTime = DateTimeOffset.UtcNow;
        Exception? lastException = null;

        while (attempt < maxAttempts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            Stopwatch attemptStopwatch = Stopwatch.StartNew();

            try
            {
                T result = await operation(cancellationToken).ConfigureAwait(false);
                attemptStopwatch.Stop();
                totalStopwatch.Stop();

                return new
                (
                    value: result,
                    succeeded: true,
                    attempts: attempt,
                    elapsedTime: totalStopwatch.Elapsed,
                    lastAttemptDuration: attemptStopwatch.Elapsed
                );
            }
            catch (Exception ex)
            {
                attemptStopwatch.Stop();
                lastException = ex;

                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    totalStopwatch.Stop();
                    throw;
                }

                if (attempt >= maxAttempts || !(shouldRetry?.Invoke(ex) ?? true))
                {
                    totalStopwatch.Stop();
                    RetryFailedException maxAttemptsException = new
                    (
                        message: $"Operation failed after {attempt} attempt(s). See inner exception for details.",
                        attempts: attempt,
                        elapsedTime: totalStopwatch.Elapsed,
                        innerException: lastException
                    );

                    return new RetryResult<T>
                    (
                        value: default,
                        succeeded: false,
                        finalException: maxAttemptsException, 
                        attempts: attempt,
                        elapsedTime: totalStopwatch.Elapsed,
                        lastAttemptDuration: attemptStopwatch.Elapsed
                    );
                }

                TimeSpan currentDelay = CalculateDelay(attempt, effectiveBaseDelay, delayStrategy);

                if (onRetryAsync is not null)
                {
                    try
                    {
                        RetryContext context = new(attempt, ex, currentDelay, operationStartTime);
                        await onRetryAsync(context).ConfigureAwait(false);
                    }
                    catch (Exception hookEx)
                    {
                        Debug.WriteLine($"[LiteRetry] onRetryAsync failed: {hookEx.Message}");
                    }
                }

                if (currentDelay > TimeSpan.Zero)
                {
                    await Task.Delay(currentDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        totalStopwatch.Stop();

        RetryFailedException finalException = new
        (
            message: $"Operation failed after {attempt} attempt(s). Retry logic ended unexpectedly.",
            attempts: attempt,
            elapsedTime: totalStopwatch.Elapsed,
            innerException: lastException
        );

        return new RetryResult<T>
        (
            value: default,
            succeeded: false,
            finalException: finalException,
            attempts: attempt,
            elapsedTime: totalStopwatch.Elapsed,
            lastAttemptDuration: totalStopwatch.Elapsed
        );
    }

    public static async Task ExecuteAsync
    (
        Func<CancellationToken, Task> operation,
        int maxAttempts = 4,
        TimeSpan? baseDelay = null,
        DelayStrategy delayStrategy = DelayStrategy.Fixed,
        Func<Exception, bool>? shouldRetry = null,
        Func<RetryContext, Task>? onRetryAsync = null,
        CancellationToken cancellationToken = default
    )
    {
        Func<CancellationToken, Task<bool>> operationWithDummyResult = async (ct) =>
        {
            await operation(ct).ConfigureAwait(false);
            return true;
        };

        await ExecuteAsync
        (
            operation: operationWithDummyResult,
            maxAttempts: maxAttempts,
            baseDelay: baseDelay,
            delayStrategy: delayStrategy,
            shouldRetry: shouldRetry,
            onRetryAsync: onRetryAsync,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
    }

    #endregion Public

    #region Private

    private static TimeSpan CalculateDelay(int attempt, TimeSpan baseDelay, DelayStrategy strategy)
    {
        switch (strategy)
        {
            case DelayStrategy.Fixed:
                return baseDelay;

            case DelayStrategy.Exponential:
                int exponent = attempt - 1;
                return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, exponent));

            case DelayStrategy.ExponentialWithJitter:
                int power = attempt - 1;
                double exponentialDelayMs = baseDelay.TotalMilliseconds * Math.Pow(2, power - 1);
                double jitterMs = exponentialDelayMs * 0.4 * (_jitterer.NextDouble() - 0.5);
                int finalDelayMs = (int)(exponentialDelayMs + jitterMs);
                return TimeSpan.FromMilliseconds(Math.Max(1, finalDelayMs));

            default:
                return baseDelay;
        }
    }

    #endregion Private
}