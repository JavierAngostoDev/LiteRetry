using LiteRetry.Core.Retrying.Application;
using LiteRetry.Core.Retrying.Application.Enums;
using LiteRetry.Core.Retrying.Domain;

namespace LiteRetry.Core.Retrying.Fluent;

/// <summary>
/// Provides a fluent interface for configuring and executing retry policies.
/// Use the static <see cref="Configure"/> method to start building a policy.
/// </summary>
public sealed class RetryBuilder
{
    private TimeSpan? _baseDelay = null;
    private int _maxAttempts = 3;
    private Func<RetryContext, Task>? _onRetryAsync = null;
    private Func<Exception, bool>? _shouldRetry = null;
    private DelayStrategy _strategy = DelayStrategy.Fixed;

    private RetryBuilder()
    { }

    /// <summary>
    /// Creates a new instance of <see cref="RetryBuilder"/> to start configuring a retry policy.
    /// </summary>
    /// <returns>A new <see cref="RetryBuilder"/> instance.</returns>
    public static RetryBuilder Configure() => new();

    /// <summary>
    /// Creates a new instance of <see cref="RetryBuilder"/>, potentially indicating intent for a specific result type <typeparamref name="T"/>.
    /// Note: Currently behaves identically to <see cref="Configure"/>. The type <typeparamref name="T"/> is primarily determined by the <see cref="RunAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/> method used later.
    /// </summary>
    /// <typeparam name="T">The intended result type for the operation to be executed (for clarity).</typeparam>
    /// <returns>A new <see cref="RetryBuilder"/> instance.</returns>
    public static RetryBuilder For<T>() => new();

    /// <summary>
    /// Specifies an asynchronous action to be executed before each retry attempt.
    /// </summary>
    /// <param name="hook">The asynchronous action to execute, receiving context about the retry attempt.</param>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>
    public RetryBuilder OnRetryAsync(Func<RetryContext, Task> hook)
    {
        _onRetryAsync = hook;
        return this;
    }

    /// <summary>
    /// Executes the configured retry policy for the specified asynchronous operation that returns a result.
    /// </summary>
    /// <typeparam name="T">The type of the result returned by the operation.</typeparam>
    /// <param name="operation">The asynchronous function to execute.</param>
    /// <param name="cancellationToken">An optional CancellationToken to observe.</param>
    /// <returns>A Task representing the asynchronous retry operation, yielding a <see cref="RetryResult{T}"/> with execution details.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="operation"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the <paramref name="cancellationToken"/> is canceled during execution.</exception>
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

    /// <summary>
    /// Executes the configured retry policy for the specified asynchronous operation that does not return a result.
    /// </summary>
    /// <param name="operation">The asynchronous action to execute.</param>
    /// <param name="cancellationToken">An optional CancellationToken to observe.</param>
    /// <returns>A Task representing the completion of the asynchronous retry operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the <paramref name="operation"/> is null.</exception>
    /// <exception cref="RetryFailedException">Thrown if the operation ultimately fails after all configured retry attempts.</exception>
    /// <exception cref="OperationCanceledException">Thrown if the <paramref name="cancellationToken"/> is canceled during execution.</exception>
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

    /// <summary>
    /// Sets the base delay duration used between retry attempts. The interpretation depends on the chosen <see cref="DelayStrategy"/>.
    /// </summary>
    /// <param name="delay">The base delay duration.</param>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>

    public RetryBuilder WithBaseDelay(TimeSpan delay)
    {
        _baseDelay = delay;
        return this;
    }

    /// <summary>
    /// Specifies a predicate function to determine whether a retry should be attempted based on the caught exception.
    /// </summary>
    /// <param name="filter">The predicate function. It receives the caught <see cref="Exception"/> and should return <c>true</c> to retry or <c>false</c> to fail immediately.</param>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>
    /// <exception cref="RetryFailedException">Thrown internally if the provided <paramref name="filter"/> predicate is null.</exception>
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

    /// <summary>
    /// Configures the retry policy to only attempt retries for exceptions of the specified type <typeparamref name="TException"/> or types derived from it.
    /// </summary>
    /// <typeparam name="TException">The type of exception to filter for retries.</typeparam>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>
    public RetryBuilder WithFilterByType<TException>() where TException : Exception
    {
        _shouldRetry = ex => ex is TException;
        return this;
    }

    /// <summary>
    /// Sets the maximum number of attempts for the operation (including the initial attempt).
    /// </summary>
    /// <param name="attempts">The maximum number of attempts. Must be 1 or greater.</param>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>
    public RetryBuilder WithMaxAttempts(int attempts)
    {
        _maxAttempts = attempts;
        return this;
    }

    /// <summary>
    /// Sets the delay strategy to use between retry attempts (e.g., Fixed, Exponential).
    /// </summary>
    /// <param name="strategy">The <see cref="DelayStrategy"/> to apply.</param>
    /// <returns>The current <see cref="RetryBuilder"/> instance for fluent chaining.</returns>
    public RetryBuilder WithStrategy(DelayStrategy strategy)
    {
        _strategy = strategy;
        return this;
    }
}