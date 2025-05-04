namespace LiteRetry.Core.Retrying.Domain;

public sealed class RetryFailedException
(
    string message,
    int attempts,
    TimeSpan elapsedTime,
    Exception? innerException = null
) : Exception(message, innerException)
{
    public int Attempts { get; } = attempts;
    public TimeSpan ElapsedTime { get; } = elapsedTime;
}