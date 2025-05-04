using LiteRetry.Core.Retrying.Application;
using LiteRetry.Core.Retrying.Application.Enums;
using LiteRetry.Core.Retrying.Domain;

namespace LiteRetry.Core.Retrying.Fluent;

public sealed class RetryBuilder
{
    private TimeSpan? _baseDelay = null;
    private int _maxAttempts = 3;
    private Func<RetryContext, Task>? _onRetryAsync = null;
    private Func<Exception, bool>? _shouldRetry = null;
    private DelayStrategy _strategy = DelayStrategy.Fixed;

    private RetryBuilder()
    { }

    public static RetryBuilder Configure() => new();

    public static RetryBuilder For<T>() => new();

    public RetryBuilder OnRetryAsync(Func<RetryContext, Task> hook)
    {
        _onRetryAsync = hook;
        return this;
    }

    public async Task<RetryResult<T>> RunAsync<T>
    (
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await RetryExecutor.ExecuteAsync
        (
            operation: operation,
            maxAttempts: _maxAttempts,
            baseDelay: _baseDelay,
            delayStrategy: _strategy,
            shouldRetry: _shouldRetry,
            onRetryAsync: _onRetryAsync,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
    }

    public async Task RunAsync
    (
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(operation);
        await RetryExecutor.ExecuteAsync
        (
            operation: operation,
            maxAttempts: _maxAttempts,
            baseDelay: _baseDelay,
            delayStrategy: _strategy,
            shouldRetry: _shouldRetry,
            onRetryAsync: _onRetryAsync,
            cancellationToken: cancellationToken
        ).ConfigureAwait(false);
    }

    public RetryBuilder WithBaseDelay(TimeSpan delay)
    {
        _baseDelay = delay;
        return this;
    }

    public RetryBuilder WithFilterByPredicate(Func<Exception, bool> filter)
    {
        if (filter is null)
        {
            throw new RetryFailedException
            (
                message: "Exception filter predicate cannot be null.",
                attempts: 0,
                elapsedTime: TimeSpan.Zero
            );
        }
        _shouldRetry = filter;
        return this;
    }

    public RetryBuilder WithFilterByType<TException>() where TException : Exception
    {
        _shouldRetry = ex => ex is TException;
        return this;
    }

    public RetryBuilder WithMaxAttempts(int attempts)
    {
        _maxAttempts = attempts;
        return this;
    }

    public RetryBuilder WithStrategy(DelayStrategy strategy)
    {
        _strategy = strategy;
        return this;
    }
}