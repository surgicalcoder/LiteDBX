using System;
using System.Diagnostics;

namespace LiteDbX.Utils.Extensions;

public static class StopWatchExtensions
{
    // Start the stopwatch and returns an IDisposable that will stop the stopwatch when disposed
    public static IDisposable StartDisposable(this Stopwatch stopwatch)
    {
        stopwatch.Start();

        return new DisposableAction(stopwatch.Stop);
    }

    private class DisposableAction(Action action)
        : IDisposable
    {
        public void Dispose()
        {
            action();
        }
    }
}