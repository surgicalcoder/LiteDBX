using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace LiteDbX;

internal static class IOExceptionExtensions
{
    private const int ERROR_SHARING_VIOLATION = 32;
    private const int ERROR_LOCK_VIOLATION    = 33;

    /// <summary>
    /// Returns <c>true</c> if the exception is a file-locking error.
    /// </summary>
    public static bool IsLocked(this IOException ex)
    {
        var errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);

        return
            errorCode == ERROR_SHARING_VIOLATION ||
            errorCode == ERROR_LOCK_VIOLATION;
    }

    /// <summary>
    /// Blocks the calling thread for <paramref name="timerInMilliseconds"/> ms if the
    /// exception is a file-locking error; rethrows for any other exception.
    ///
    /// Phase 6: replaced <c>Task.Delay(...).Wait()</c> with <c>Thread.Sleep()</c>.
    /// <c>Task.Delay().Wait()</c> misuses the async machinery for a deliberately
    /// synchronous delay inside a sync retry loop (<see cref="FileHelper.TryExec"/>
    /// / <see cref="FileHelper.Exec"/>). Blocking the thread is intentional in that
    /// context — <c>Thread.Sleep</c> is the correct primitive.
    /// </summary>
    public static void WaitIfLocked(this IOException ex, int timerInMilliseconds)
    {
        if (ex.IsLocked())
        {
            if (timerInMilliseconds > 0)
            {
                Thread.Sleep(timerInMilliseconds);
            }
        }
        else
        {
            throw ex;
        }
    }
}