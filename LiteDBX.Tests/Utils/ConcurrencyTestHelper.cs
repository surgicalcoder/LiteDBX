using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Tests;

internal static class ConcurrencyTestHelper
{
    public static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan EventuallyTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);

    public static Task RunIsolated(Func<Task> action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(action);
        }
    }

    public static Task<T> RunIsolated<T>(Func<Task<T>> action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(action);
        }
    }

    public static async Task WaitForSignal(SemaphoreSlim semaphore, string description)
    {
        if (!await semaphore.WaitAsync(CoordinationTimeout).ConfigureAwait(false))
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }

    public static async Task Eventually(
        Func<Task<bool>> condition,
        string description,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var effectiveTimeout = timeout ?? EventuallyTimeout;
        var effectivePollInterval = pollInterval ?? PollInterval;
        var deadline = DateTime.UtcNow + effectiveTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(effectivePollInterval).ConfigureAwait(false);
        }

        throw new TimeoutException($"Timed out waiting for condition: {description}.");
    }

    public static ConnectionString CreateConnectionString(TempFile file, ConnectionType connectionType)
        => new()
        {
            Filename = file.Filename,
            Connection = connectionType
        };
}


