namespace LiteRetry.Core.Retrying.Domain;

public sealed class RetryResult<T>
(
    T? value,
    bool succeeded,
    int attempts,
    TimeSpan elapsedTime,
    TimeSpan lastAttemptDuration,
    Exception? finalException = null
)
{
    public int Attempts { get; } = attempts;
    public TimeSpan ElapsedTime { get; } = elapsedTime;
    public Exception? FinalException { get; } = finalException;
    public TimeSpan LastAttemptDuration { get; } = lastAttemptDuration;
    public bool Succeeded { get; } = succeeded;
    public T? Value { get; } = value;
}