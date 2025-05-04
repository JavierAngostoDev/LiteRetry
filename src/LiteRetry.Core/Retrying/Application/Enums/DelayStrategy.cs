namespace LiteRetry.Core.Retrying.Application.Enums;

public enum DelayStrategy
{
    Fixed,
    Exponential,
    ExponentialWithJitter
}