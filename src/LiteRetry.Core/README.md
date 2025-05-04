# LiteRetry ✨

[![NuGet version](https://img.shields.io/nuget/v/LiteRetry.svg?style=flat-square)](https://www.nuget.org/packages/LiteRetry/) [![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**LiteRetry** is a lightweight, fluent, and extensible retry utility for .NET.
It helps developers eliminate repetitive `try/catch` blocks and build resilient code with ease when dealing with transient failures.

---

## 🤔 Why LiteRetry?

Modern applications often interact with external services (APIs, databases, etc.) over networks that can be unreliable. Operations might fail temporarily due to network glitches, rate limiting, temporary service unavailability, or deadlocks. Instead of letting these transient errors fail the entire operation, a common pattern is to retry.

LiteRetry provides a clean, configurable, and easy-to-use way to implement this retry logic without cluttering your core business code.

---

## 🚀 Installation

Install LiteRetry via the .NET CLI:

```bash
dotnet add package LiteRetry
```

Or via the NuGet Package Manager Console:

```powershell
Install-Package LiteRetry
```

---

## ✨ Features

* **Fluent Configuration**: Intuitive API using RetryBuilder for setting up retry logic.
* **Direct Execution**: Optional static RetryExecutor for simpler use cases.
* **Async First**: Built for modern asynchronous programming (Task and Task<T>).
* **Configurable Retries**: Define the maximum number of attempts.
* **Delay Strategies**:

  * Fixed: Constant delay between retries.
  * Exponential: Delay increases exponentially.
  * ExponentialWithJitter: Exponential delay with added randomness to help prevent the "thundering herd" problem under high contention.
* **Exception Filtering**: Retry only on specific exceptions using type (WithFilterByType<TException>) or a custom predicate (WithFilterByPredicate).
* **Retry Hook**: Execute asynchronous actions (OnRetryAsync) before each retry attempt (e.g., for logging, metrics).
* **Success Hook**: Execute asynchronous actions (OnSuccessAsync) after a successful attempt, even after retries.
* **Cancellation Support**: Gracefully cancel operations and pending retries using CancellationToken.
* **Detailed Results**: RetryResult<T> provides information on success/failure, final value, attempts, timing, and the final exception.
* **Reliable**: Fully unit-tested.

---

## 🛠️ Usage Examples

The primary way to use LiteRetry is via the fluent RetryBuilder.

### Example 1: Basic Retry for Task<T>

```csharp
using LiteRetry.Core.Retrying.Fluent;
using LiteRetry.Core.Retrying.Application.Enums;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class DataFetcher
{
    private readonly HttpClient _httpClient = new HttpClient();
    private int _attemptCount = 0;

    public async Task<string> GetDataWithRetriesAsync(string url, CancellationToken cancellationToken = default)
    {
        _attemptCount = 0;

        Func<CancellationToken, Task<string>> fetchOperation = async (CancellationToken ct) =>
        {
            _attemptCount++;
            Console.WriteLine($"Attempt {_attemptCount}: Fetching {url}...");
            if (_attemptCount < 2)
                throw new HttpRequestException("Simulated network error");

            HttpResponseMessage response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        };

        RetryResult<string> result = await Retry.Configure()
            .WithMaxAttempts(3)
            .WithBaseDelay(TimeSpan.FromMilliseconds(500))
            .WithFilterByType<HttpRequestException>()
            .OnSuccessAsync(ctx => {
                Console.WriteLine($"Operation succeeded on attempt {ctx.Attempt}.");
                return Task.CompletedTask;
            })
            .RunAsync(fetchOperation, cancellationToken);

        if (result.Succeeded)
        {
            Console.WriteLine($"Success after {result.Attempts} attempts!");
            return result.Value;
        }
        else
        {
            Console.WriteLine($"Operation failed after {result.Attempts} attempts.");
            Console.Error.WriteLine($"Error: {result.FinalException?.InnerException?.Message}");
            throw result.FinalException ?? new Exception("Retry failed for unknown reason.");
        }
    }
}
```

### Example 2: Handling Task (Void) Operations

```csharp
using LiteRetry.Core.Retrying.Fluent;
using LiteRetry.Core.Retrying.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;

public class TaskProcessor
{
    private int _processAttempt = 0;

    public async Task ProcessSomethingWithRetryAsync(CancellationToken cancellationToken = default)
    {
        _processAttempt = 0;

        Func<CancellationToken, Task> processOperation = async (CancellationToken ct) =>
        {
            _processAttempt++;
            Console.WriteLine($"Attempt {_processAttempt}: Processing...");
            await Task.Delay(200, ct);
            if (_processAttempt < 3)
                throw new TimeoutException("Simulated processing timeout");

            Console.WriteLine("Processing completed successfully.");
        };

        try
        {
            IRetryExecutor retryExecutor = Retry.Configure()
                .WithMaxAttempts(4)
                .WithBaseDelay(TimeSpan.FromMilliseconds(300))
                .WithStrategy(DelayStrategy.Fixed)
                .WithFilterByType<TimeoutException>()
                .OnRetryAsync(ctx => {
                    Console.WriteLine($"Attempt {ctx.Attempt} failed. Retrying after {ctx.Delay.TotalMilliseconds}ms...");
                    return Task.CompletedTask;
                })
                .OnSuccessAsync(ctx => {
                    Console.WriteLine($"Successfully completed after {ctx.Attempt} attempts.");
                    return Task.CompletedTask;
                });

            await retryExecutor.RunAsync(processOperation, cancellationToken);

            Console.WriteLine("Task succeeded!");
        }
        catch (RetryFailedException ex)
        {
            Console.Error.WriteLine($"Task failed definitively after {ex.Attempts} attempts.");
            Console.Error.WriteLine($"Last error: {ex.InnerException?.Message}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Operation was cancelled.");
        }
    }
}
```

### Example 3: Using RetryExecutor Directly (Alternative)

```csharp
using LiteRetry.Core.Retrying.Application;
using LiteRetry.Core.Retrying.Domain;
using LiteRetry.Core.Retrying.Application.Enums;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.Net.Http;

Func<CancellationToken, Task<string>> myOperation = async ct =>
{
    Console.WriteLine("Executing operation via RetryExecutor...");
    await Task.Delay(100, ct);
    return await Task.FromResult("Executor Result");
};

try
{
    RetryResult<string> result = await RetryExecutor.ExecuteAsync<string>
    (
        operation: myOperation,
        maxAttempts: 5,
        baseDelay: TimeSpan.FromMilliseconds(200),
        delayStrategy: DelayStrategy.ExponentialWithJitter,
        shouldRetry: ex => ex is TimeoutException || ex is HttpRequestException,
        onRetryAsync: ctx =>
        {
            Console.WriteLine($"RetryExecutor: Retry #{ctx.Attempt} after {ctx.Delay.TotalMilliseconds}ms due to {ctx.LastException.GetType().Name}");
            return Task.CompletedTask;
        },
        onSuccessAsync: ctx =>
        {
            Console.WriteLine($"RetryExecutor: Operation succeeded on attempt {ctx.Attempt}.");
            return Task.CompletedTask;
        },
        cancellationToken: CancellationToken.None
    );

    if (result.Succeeded)
        Console.WriteLine($"RetryExecutor succeeded: {result.Value}");
    else
        Console.WriteLine($"RetryExecutor failed after {result.Attempts} attempts.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("RetryExecutor operation cancelled.");
}
```

---

## 📆 API Overview

### Key Classes & Enums

* **RetryBuilder**: Fluent API for configuring and executing retry logic.
* **RetryExecutor**: Static class with ExecuteAsync methods.
* **RetryContext**:

  * `Attempt`
  * `LastException`
  * `Delay`
  * `StartTime`
* **RetryResult<T>**:

  * `Succeeded`
  * `Value`
  * `Attempts`
  * `ElapsedTime`
  * `LastAttemptDuration`
  * `FinalException`
* **RetryFailedException**: Thrown when all attempts fail.
* **DelayStrategy**: `Fixed`, `Exponential`, `ExponentialWithJitter`.

---

## 📜 License

Distributed under the MIT License. See LICENSE file for more information.

---

## 🙌 Author

Created by [Javier Angosto Barjollo](https://github.com/JavierAngostoDev)
