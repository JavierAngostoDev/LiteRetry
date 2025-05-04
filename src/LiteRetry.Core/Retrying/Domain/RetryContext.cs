namespace LiteRetry.Core.Retrying.Domain;

public sealed class RetryContext
(
    int attempt,
    Exception lastException,
    TimeSpan delay,
    DateTimeOffset startTime
)
{
    public int Attempt { get; } = attempt;
    public TimeSpan Delay { get; } = delay;
    public Exception? LastException { get; } = lastException;
    public DateTimeOffset StartTime { get; } = startTime;
}