using System.Diagnostics;
using FluentAssertions;
using LiteRetry.Core.Retrying.Application;
using LiteRetry.Core.Retrying.Application.Enums;
using LiteRetry.Core.Retrying.Domain;

namespace LiteRetry.Tests;

public class RetryExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CancellationRequestedBeforeFirstAttempt_ReturnsFailureWithRetryFailedException()
    {
        // Arrange
        Func<CancellationToken, Task<string>> operation = ct => Task.FromResult("Should not run");
        using CancellationTokenSource cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        RetryResult<string> result = await RetryExecutor.ExecuteAsync(operation, cancellationToken: cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(0);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequestedDuringDelay_ThrowsOperationCanceledException()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        int attemptsMade = 0;

        Func<CancellationToken, Task<string>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade == 1)
            {
                cts.Cancel();
            }
            throw new InvalidOperationException("Temporary failure");
        };

        Func<Task> act = () => RetryExecutor.ExecuteAsync(operation, maxAttempts: 3, baseDelay: TimeSpan.FromSeconds(1), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attemptsMade.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ExecuteAsync_MaxAttemptsLessThanOne_DefaultsToOneAttempt(int invalidMaxAttempts)
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> failingOperation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            throw new Exception("Failure");
        };

        RetryResult<string> result = await RetryExecutor.ExecuteAsync(failingOperation, maxAttempts: invalidMaxAttempts);

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(1);
        attemptsMade.Should().Be(1);
        result.FinalException.Should().BeOfType<RetryFailedException>();
    }

    [Fact]
    public async Task ExecuteAsync_NegativeBaseDelay_UsesDefaultDelay()
    {
        int attemptsMade = 0;
        TimeSpan measuredDelay = TimeSpan.Zero;
        Func<CancellationToken, Task<int>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 2) throw new Exception("Fail once");
            return 1;
        };

        Func<RetryContext, Task> onRetry = (ctx) =>
        {
            measuredDelay = ctx.Delay;
            return Task.CompletedTask;
        };
        TimeSpan negativeDelay = TimeSpan.FromMilliseconds(-100);
        TimeSpan defaultDelay = TimeSpan.FromMilliseconds(200);

        RetryResult<int> result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 3,
            baseDelay: negativeDelay,
            delayStrategy: DelayStrategy.Fixed,
            onRetryAsync: onRetry
        );

        result.Succeeded.Should().BeTrue();
        result.Attempts.Should().Be(2);
        measuredDelay.Should().Be(defaultDelay);
    }

    [Fact]
    public async Task ExecuteAsync_OnSuccessAsyncIsInvokedOnce_WhenOperationSucceeds()
    {
        // Arrange
        int onSuccessCalls = 0;
        RetryContext? receivedContext = null;

        Func<CancellationToken, Task<string>> operation = async ct =>
        {
            await Task.Delay(1, ct);
            return "OK";
        };

        Func<RetryContext, Task> onSuccess = ctx =>
        {
            onSuccessCalls++;
            receivedContext = ctx;
            return Task.CompletedTask;
        };

        // Act
        var result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(5),
            onSuccessAsync: onSuccess
        );

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("OK");
        onSuccessCalls.Should().Be(1);
        receivedContext.Should().NotBeNull();
        receivedContext!.Attempt.Should().Be(1);
        receivedContext.LastException.Should().BeNull(); // Confirm success context
    }

    [Fact]
    public async Task ExecuteAsync_OperationFailsAllAttempts_ReturnsFailureResult()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<bool>> failingOperation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            throw new TimeoutException("Operation timed out");
        };

        int maxAttempts = 3;
        int onRetryCalls = 0;
        Func<RetryContext, Task> onRetry = (ctx) =>
        {
            onRetryCalls++;
            return Task.CompletedTask;
        };

        RetryResult<bool> result = await RetryExecutor.ExecuteAsync(
            failingOperation,
            maxAttempts: maxAttempts,
            baseDelay: TimeSpan.FromMilliseconds(5),
            onRetryAsync: onRetry
        );

        result.Succeeded.Should().BeFalse();
        result.Value.Should().Be(default);
        result.Attempts.Should().Be(maxAttempts);
        attemptsMade.Should().Be(maxAttempts);
        onRetryCalls.Should().Be(maxAttempts - 1);
        result.FinalException.Should().NotBeNull();
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.InnerException.Should().BeOfType<TimeoutException>();
        result.FinalException.Message.Should().Contain($"Operation failed after {maxAttempts} attempt(s)");
        ((RetryFailedException)result.FinalException).Attempts.Should().Be(maxAttempts);
    }

    [Fact]
    public async Task ExecuteAsync_OperationFailsThenSucceeds_ReturnsSuccessResultAfterRetry()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 3)
            {
                throw new InvalidOperationException("Temporary failure");
            }
            return 123;
        };

        int onRetryCalls = 0;
        Func<RetryContext, Task> onRetry = (ctx) =>
        {
            onRetryCalls++;
            ctx.Attempt.Should().Be(onRetryCalls);
            ctx.LastException.Should().BeOfType<InvalidOperationException>();
            return Task.CompletedTask;
        };

        RetryResult<int> result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 5,
            baseDelay: TimeSpan.FromMilliseconds(5),
            onRetryAsync: onRetry
        );

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be(123);
        result.Attempts.Should().Be(3);
        attemptsMade.Should().Be(3);
        onRetryCalls.Should().Be(2);
        result.FinalException.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_OperationItselfThrowsOperationCanceledException_ReturnsFailureWithRetryFailedException()
    {
        using CancellationTokenSource cts = new CancellationTokenSource();
        int attemptsMade = 0;

        Func<CancellationToken, Task<string>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(5, ct);
            if (attemptsMade == 2)
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
            }
            throw new InvalidOperationException("Temporary failure before cancellation");
        };

        RetryResult<string> result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(5),
            cancellationToken: cts.Token
        );

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(2);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task ExecuteAsync_OperationSucceedsFirstTry_ReturnsSuccessResult()
    {
        Func<CancellationToken, Task<string>> successfulOperation = async (ct) =>
        {
            await Task.Delay(1, ct);
            return "Success";
        };

        RetryResult<string> result = await RetryExecutor.ExecuteAsync(successfulOperation);

        result.Should().NotBeNull();
        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("Success");
        result.Attempts.Should().Be(1);
        result.FinalException.Should().BeNull();
        result.ElapsedTime.Should().BeGreaterThan(TimeSpan.Zero);
        result.LastAttemptDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRetryReturnsFalse_StopsRetryingAndReturnsFailure()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            throw new ArgumentException("Bad argument, don't retry");
        };

        Func<Exception, bool> shouldRetry = (ex) => !(ex is ArgumentException);

        int onRetryCalls = 0;
        Func<RetryContext, Task> onRetry = (ctx) =>
        {
            onRetryCalls++;
            return Task.CompletedTask;
        };

        RetryResult<int> result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 5,
            baseDelay: TimeSpan.FromMilliseconds(5),
            shouldRetry: shouldRetry,
            onRetryAsync: onRetry
        );

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(1);
        attemptsMade.Should().Be(1);
        onRetryCalls.Should().Be(0);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutExceededBeforeCompletion_ReturnsFailureImmediately()
    {
        // Arrange
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(300, ct);
            throw new InvalidOperationException("Simulated failure");
        };

        TimeSpan totalTimeout = TimeSpan.FromMilliseconds(500);

        Stopwatch sw = Stopwatch.StartNew();

        // Act
        RetryResult<string> result = await RetryExecutor.ExecuteAsync
        (
            operation,
            maxAttempts: 5,
            baseDelay: TimeSpan.FromMilliseconds(100),
            delayStrategy: DelayStrategy.Fixed,
            totalTimeout: totalTimeout
        );

        sw.Stop();

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().BeLessThan(5);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.Message.Should().Contain("timeout");
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(totalTimeout);
        attemptsMade.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(DelayStrategy.Fixed)]
    [InlineData(DelayStrategy.Exponential)]
    [InlineData(DelayStrategy.ExponentialWithJitter)]
    public async Task ExecuteAsync_WithDifferentDelayStrategies_CompletesSuccessfullyAfterRetries(DelayStrategy strategy)
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async (ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 2) throw new Exception("Fail once");
            return 1;
        };
        int onRetryCalls = 0;
        Func<RetryContext, Task> onRetry = (ctx) => { onRetryCalls++; return Task.CompletedTask; };

        RetryResult<int> result = await RetryExecutor.ExecuteAsync(
            operation,
            maxAttempts: 3,
            baseDelay: TimeSpan.FromMilliseconds(5),
            delayStrategy: strategy,
            onRetryAsync: onRetry
        );

        result.Succeeded.Should().BeTrue();
        result.Attempts.Should().Be(2);
        onRetryCalls.Should().Be(1);
    }
}