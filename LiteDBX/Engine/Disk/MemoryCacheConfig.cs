using System;

namespace LiteDbX.Engine;

/// <summary>
/// Controls optional trimming behavior for <see cref="MemoryCache"/>.
/// Defaults preserve the current LiteDbX behavior by keeping trimming disabled.
/// </summary>
public class MemoryCacheConfig
{
    /// <summary>
    /// Enable opportunistic cleanup of fully free/idle memory segments.
    /// Disabled by default so existing applications keep the current cache behavior.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Soft threshold for how many free pages can accumulate before cleanup is considered.
    /// Cleanup still retires memory only when an entire segment is reclaimable.
    /// </summary>
    public int MaxFreePages { get; set; } = 500;

    /// <summary>
    /// Minimum idle time for a fully free segment before it becomes a retirement candidate.
    /// </summary>
    public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum number of reclaimable segments to process in a single cleanup pass.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Minimum time between cleanup passes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}

