using LiteRetry.Core.Retrying.Fluent;

namespace LiteRetry.Core
{
    public static class Retry
    {
        public static RetryBuilder Configure() => RetryBuilder.Configure();
    }
}