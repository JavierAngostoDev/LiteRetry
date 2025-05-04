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

    /// <summary>
    /// Executes an asynchronous operation that returns a result, with retry logic based on the specified parameters.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The asynchronous function to execute. It receives a CancellationToken.</param>
    /// <param name="maxAttempts">The maximum number of attempts (including the initial one). Must be at least 1. Defaults to 3.</param>
    /// <param name="baseDelay">The base delay duration for waits between attempts. Defaults to 200ms if null or negative. Used differently depending on the strategy.</param>
    /// <param name="delayStrategy">The strategy for calculating delays between attempts (Fixed, Exponential, ExponentialWithJitter). Defaults to Fixed.</param>
    /// <param name="shouldRetry">An optional predicate function that determines if a retry should occur based on the caught exception. If null, retries on any exception. Return true to retry, false to fail immediately.</param>
    /// <param name="onRetryAsync">An optional asynchronous action to execute before each retry attempt. Receives context about the current attempt.</param>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the operation and delays.</param>
    /// <returns>
    /// A Task representing the asynchronous retry operation, yielding a <see cref="RetryResult{T}"/>
    /// which contains the execution result (if successful), status, attempt count, timing information, and the final exception (if failed).
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the <paramref name="cancellationToken"/> is canceled during the operation or delay.</exception>
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

    /// <summary>
    /// Executes an asynchronous operation that does not return a result, with retry logic based on the specified parameters.
    /// This is a convenience overload that calls the generic ExecuteAsync&lt;T&gt; method.
    /// </summary>
    /// <param name="operation">The asynchronous action to execute. It receives a CancellationToken.</param>
    /// <param name="maxAttempts">The maximum number of attempts (including the initial one). Must be at least 1. Defaults to 3.</param>
    /// <param name="baseDelay">The base delay duration for waits between attempts. Defaults to 200ms if null or negative. Used differently depending on the strategy.</param>
    /// <param name="delayStrategy">The strategy for calculating delays between attempts (Fixed, Exponential, ExponentialWithJitter). Defaults to Fixed.</param>
    /// <param name="shouldRetry">An optional predicate function that determines if a retry should occur based on the caught exception. If null, retries on any exception. Return true to retry, false to fail immediately.</param>
    /// <param name="onRetryAsync">An optional asynchronous action to execute before each retry attempt. Receives context about the current attempt.</param>
    /// <param name="cancellationToken">A CancellationToken to observe while waiting for the operation and delays.</param>
    /// <returns>
    /// A Task representing the asynchronous retry operation.
    /// If the operation fails after all attempts, the Task will eventually complete,
    /// but the underlying <see cref="RetryResult{T}"/> (where T is a dummy type) will indicate failure
    /// and contain the <see cref="RetryFailedException"/>. Consider checking the result of the underlying generic method call if detailed failure information is needed beyond catching exceptions.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown if the <paramref name="cancellationToken"/> is canceled during the operation or delay.</exception>
    /// <remarks>
    /// This method wraps the provided operation and calls the generic <c>ExecuteAsync&lt;bool&gt;</c> overload.
    /// While it returns a Task, you might need to inspect the returned task's status or handle exceptions propagated from the underlying call for robust error handling,
    /// although the primary mechanism for failure reporting in LiteRetry is the RetryResult object returned by the generic overload.
    /// </remarks>
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

    /// <summary>
    /// Calculates the delay duration before the next retry attempt.
    /// </summary>
    /// <param name="attempt">The current attempt number (starting from 1).</param>
    /// <param name="baseDelay">The configured base delay.</param>
    /// <param name="strategy">The chosen delay strategy.</param>
    /// <returns>The calculated TimeSpan delay.</returns>
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
                double exponentialDelayMs = baseDelay.TotalMilliseconds * Math.Pow(2, power);
                double jitterMs = exponentialDelayMs * 0.4 * (_jitterer.NextDouble() - 0.5);
                int finalDelayMs = (int)(exponentialDelayMs + jitterMs);
                return TimeSpan.FromMilliseconds(Math.Max(1, finalDelayMs));

            default:
                return baseDelay;
        }
    }

    #endregion Private
}