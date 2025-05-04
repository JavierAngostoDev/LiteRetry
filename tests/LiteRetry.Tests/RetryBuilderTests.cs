using FluentAssertions;
using LiteRetry.Core.Retrying.Application.Enums;
using LiteRetry.Core.Retrying.Domain;
using LiteRetry.Core.Retrying.Fluent;

namespace LiteRetry.Tests;

public class RetryBuilderTests
{
    [Fact]
    public void Configure_ReturnsNonNullBuilderInstance()
    {
        RetryBuilder builder = RetryBuilder.Configure();

        builder.Should().NotBeNull();
        builder.Should().BeOfType<RetryBuilder>();
    }

    [Fact]
    public void For_ReturnsNonNullBuilderInstance()
    {
        RetryBuilder builder = RetryBuilder.For<string>();

        builder.Should().NotBeNull();
        builder.Should().BeOfType<RetryBuilder>();
    }

    [Fact]
    public async Task RunAsync_T_NullOperation_ThrowsArgumentNullException()
    {
        RetryBuilder builder = RetryBuilder.Configure();
        Func<CancellationToken, Task<string>>? nullOperation = null;

        Func<Task> act = () => builder.RunAsync(nullOperation!);

        await act.Should().ThrowAsync<ArgumentNullException>()
                 .WithMessage("*operation*");
    }

    [Fact]
    public async Task RunAsync_T_WithCustomMaxAttempts_RespectsSetting()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<bool>> failingOperation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            throw new TimeoutException("Always fails");
        };

        int customMaxAttempts = 2;
        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithMaxAttempts(customMaxAttempts)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        RetryResult<bool> result = await builder.RunAsync(failingOperation);

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(customMaxAttempts);
        attemptsMade.Should().Be(customMaxAttempts);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.InnerException.Should().BeOfType<TimeoutException>();
    }

    [Fact]
    public async Task RunAsync_T_WithCustomStrategy_AppliesStrategy()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(5, ct);
            if (attemptsMade < 3) throw new Exception("Fail");
            return 42;
        };

        int onRetryCalls = 0;
        TimeSpan? firstDelay = null;
        TimeSpan? secondDelay = null;

        Func<RetryContext, Task> onRetry = (RetryContext ctx) =>
        {
            onRetryCalls++;
            if (onRetryCalls == 1) firstDelay = ctx.Delay;
            if (onRetryCalls == 2) secondDelay = ctx.Delay;
            return Task.CompletedTask;
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithStrategy(DelayStrategy.Exponential)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(10))
                                           .OnRetryAsync(onRetry);

        RetryResult<int> result = await builder.RunAsync(operation);

        result.Succeeded.Should().BeTrue();
        result.Attempts.Should().Be(3);
        onRetryCalls.Should().Be(2);
        firstDelay.Should().Be(TimeSpan.FromMilliseconds(10));
        secondDelay.Should().Be(TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task RunAsync_T_WithDefaultSettings_UsesDefaultsSuccessfully()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 2) throw new InvalidOperationException("Fail once");
            return "Success";
        };

        RetryBuilder builder = RetryBuilder.Configure();

        RetryResult<string> result = await builder.RunAsync(operation);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("Success");
        result.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_T_WithFilterByPredicate_RetriesOnlyOnMatch()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade == 1) throw new InvalidOperationException("Retry this");
            if (attemptsMade == 2) throw new ArgumentException("Do not retry this");
            return "Success";
        };

        Func<Exception, bool> filter = (Exception ex) => ex is InvalidOperationException;

        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithFilterByPredicate(filter)
                                           .WithMaxAttempts(5)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        RetryResult<string> result = await builder.RunAsync(operation);

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(2);
        attemptsMade.Should().Be(2);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_T_WithFilterByType_RetriesOnlyOnMatch()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade == 1) throw new TimeoutException("Retry this type");
            if (attemptsMade == 2) throw new FormatException("Do not retry this type");
            return "Success";
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithFilterByType<TimeoutException>()
                                           .WithMaxAttempts(5)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        RetryResult<string> result = await builder.RunAsync(operation);

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(2);
        attemptsMade.Should().Be(2);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.InnerException.Should().BeOfType<FormatException>();
    }

    [Fact]
    public async Task RunAsync_T_WithOnRetryHook_HookIsCalled()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 3) throw new Exception("Fail");
            return 100;
        };

        int onRetryCalls = 0;
        Exception? lastExceptionInHook = null;
        int attemptNumberInHook = 0;

        Func<RetryContext, Task> onRetry = (RetryContext ctx) =>
        {
            onRetryCalls++;
            lastExceptionInHook = ctx.LastException;
            attemptNumberInHook = ctx.Attempt;
            return Task.CompletedTask;
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .OnRetryAsync(onRetry)
                                           .WithMaxAttempts(4)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        RetryResult<int> result = await builder.RunAsync(operation);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be(100);
        result.Attempts.Should().Be(3);
        attemptsMade.Should().Be(3);
        onRetryCalls.Should().Be(2);
        lastExceptionInHook.Should().BeOfType<Exception>();
        attemptNumberInHook.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_T_WithOnSuccessHook_HookIsCalledOnceOnSuccess()
    {
        // Arrange
        int onSuccessCalls = 0;
        RetryContext? receivedContext = null;

        Func<CancellationToken, Task<string>> operation = async ct =>
        {
            await Task.Delay(1, ct);
            return "SUCCESS";
        };

        Func<RetryContext, Task> onSuccess = ctx =>
        {
            onSuccessCalls++;
            receivedContext = ctx;
            return Task.CompletedTask;
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .OnSuccessAsync(onSuccess)
                                           .WithMaxAttempts(3)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        // Act
        RetryResult<string> result = await builder.RunAsync(operation);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("SUCCESS");
        result.Attempts.Should().Be(1);
        onSuccessCalls.Should().Be(1);
        receivedContext.Should().NotBeNull();
        receivedContext!.Attempt.Should().Be(1);
        receivedContext.LastException.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_T_WithTotalTimeoutExceeded_StopsEarlyWithFailure()
    {
        // Arrange
        int attemptsMade = 0;
        Func<CancellationToken, Task<string>> operation = async ct =>
        {
            attemptsMade++;
            await Task.Delay(300, ct);
            throw new InvalidOperationException("Fail");
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithMaxAttempts(5)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(100))
                                           .WithTimeout(TimeSpan.FromMilliseconds(500));

        // Act
        RetryResult<string> result = await builder.RunAsync(operation);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().BeLessThan(5);
        attemptsMade.Should().BeGreaterThan(0);
        result.FinalException.Should().BeOfType<RetryFailedException>();
        result.FinalException!.Message.Should().Contain("timeout");
    }

    [Fact]
    public async Task RunAsync_T_WithTotalTimeoutSucceedsWithinLimit()
    {
        // Arrange
        int attemptsMade = 0;
        Func<CancellationToken, Task<int>> operation = async ct =>
        {
            attemptsMade++;
            await Task.Delay(100, ct);
            if (attemptsMade < 3)
                throw new Exception("Temporary failure");

            return 42;
        };

        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithMaxAttempts(5)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(100))
                                           .WithTimeout(TimeSpan.FromMilliseconds(1000));

        // Act
        RetryResult<int> result = await builder.RunAsync(operation);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_Void_NullOperation_ThrowsArgumentNullException()
    {
        RetryBuilder builder = RetryBuilder.Configure();
        Func<CancellationToken, Task>? nullOperation = null;

        Func<Task> act = () => builder.RunAsync(nullOperation!);

        await act.Should().ThrowAsync<ArgumentNullException>()
                 .WithMessage("*operation*");
    }

    [Fact]
    public async Task RunAsync_Void_WithCustomMaxAttempts_RespectsSettingAndThrows()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task> failingOperation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            throw new TimeoutException("Always fails");
        };

        int customMaxAttempts = 2;
        RetryBuilder builder = RetryBuilder.Configure()
                                           .WithMaxAttempts(customMaxAttempts)
                                           .WithBaseDelay(TimeSpan.FromMilliseconds(1));

        Func<Task> act = () => builder.RunAsync(failingOperation);

        await act.Should().NotThrowAsync();
        attemptsMade.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_Void_WithDefaultSettings_UsesDefaultsSuccessfully()
    {
        int attemptsMade = 0;
        Func<CancellationToken, Task> operation = async (CancellationToken ct) =>
        {
            attemptsMade++;
            await Task.Delay(1, ct);
            if (attemptsMade < 2) throw new InvalidOperationException("Fail once");
        };

        RetryBuilder builder = RetryBuilder.Configure();

        Func<Task> act = () => builder.RunAsync(operation);

        await act.Should().NotThrowAsync();
        attemptsMade.Should().Be(2);
    }

    [Fact]
    public void WithFilterByPredicate_NullFilter_ThrowsRetryFailedException()
    {
        RetryBuilder builder = RetryBuilder.Configure();
        Func<Exception, bool>? nullFilter = null;

        Action act = () => builder.WithFilterByPredicate(nullFilter!);

        act.Should().Throw<RetryFailedException>()
           .WithMessage("Exception filter predicate cannot be null.");
    }

    [Fact]
    public async Task RunAsync_T_OnFailureAsyncIsCalledOnceOnFailure()
    {
        int failureHookCalls = 0;
        RetryContext? failureContext = null;

        Func<CancellationToken, Task<string>> failingOperation = async (ct) =>
        {
            await Task.Delay(1, ct);
            throw new InvalidOperationException("Fail always");
        };

        Func<RetryContext, Task> onFailure = ctx =>
        {
            failureHookCalls++;
            failureContext = ctx;
            return Task.CompletedTask;
        };

        RetryBuilder builder = RetryBuilder.Configure()
            .WithMaxAttempts(2)
            .WithBaseDelay(TimeSpan.FromMilliseconds(1))
            .OnFailureAsync(onFailure);

        RetryResult<string> result = await builder.RunAsync(failingOperation);

        result.Succeeded.Should().BeFalse();
        result.Attempts.Should().Be(2);
        failureHookCalls.Should().Be(1);
        failureContext.Should().NotBeNull();
        failureContext!.Attempt.Should().Be(2);
        failureContext.LastException.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_T_OnFailureAsyncIsNotCalledOnSuccess()
    {
        int failureHookCalls = 0;

        Func<CancellationToken, Task<string>> successfulOperation = async (ct) =>
        {
            await Task.Delay(1, ct);
            return "OK";
        };

        Func<RetryContext, Task> onFailure = ctx =>
        {
            failureHookCalls++;
            return Task.CompletedTask;
        };

        RetryBuilder builder = RetryBuilder.Configure()
            .WithMaxAttempts(3)
            .OnFailureAsync(onFailure);

        RetryResult<string> result = await builder.RunAsync(successfulOperation);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().Be("OK");
        failureHookCalls.Should().Be(0);
    }

}