using FluentAssertions;
using LiteRetry.Core;
using LiteRetry.Core.Retrying.Fluent;

namespace LiteRetry.Tests;

public class RetryTests
{
    [Fact]
    public void Configure_ShouldReturnRetryBuilderInstance()
    {
        // Act
        var builder = Retry.Configure();

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeOfType<RetryBuilder>();
    }
}