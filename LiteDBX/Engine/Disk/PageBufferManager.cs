using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace LiteDbX.Engine;

/// <summary>
/// Tracks page-buffer segments so cache cleanup can retire whole idle segments safely.
/// A segment is eligible for retirement only when every page slice from the segment is back in
/// the free queue and the segment has been idle long enough.
/// </summary>
internal sealed class PageBufferManager
{
    private readonly MemoryCacheConfig _config;
    private readonly Dictionary<int, SegmentInfo> _segments = new();

    private long _lastCleanupRunTicks;
    private int _nextSegmentId;
    private int _retiredSegments;

    public PageBufferManager(MemoryCacheConfig config)
    {
        _config = config ?? new MemoryCacheConfig();
    }

    public MemoryCacheConfig Config => _config;

    public int ActiveSegments
    {
        get
        {
            lock (_segments)
            {
                return _segments.Count;
            }
        }
    }

    public int RetiredSegments => Volatile.Read(ref _retiredSegments);

    public int NextSegmentId() => Interlocked.Increment(ref _nextSegmentId);

    public void RegisterSegment(int segmentId, PageBuffer[] pages)
    {
        if (segmentId == 0) return;

        foreach (var page in pages)
        {
            page.TimestampFree = null;
        }

        lock (_segments)
        {
            _segments[segmentId] = new SegmentInfo(segmentId, pages.Length);
        }
    }

    public void MarkDequeued(PageBuffer page)
    {
        page.TimestampFree = null;

        if (page.SegmentId == 0) return;

        lock (_segments)
        {
            if (_segments.TryGetValue(page.SegmentId, out var segment) && segment.FreePages > 0)
            {
                segment.FreePages--;
            }
        }
    }

    public void MarkEnqueued(PageBuffer page, long nowTicks)
    {
        page.TimestampFree = nowTicks;

        if (page.SegmentId == 0) return;

        lock (_segments)
        {
            if (_segments.TryGetValue(page.SegmentId, out var segment))
            {
                segment.FreePages++;
                segment.LastFreeTicks = nowTicks;
            }
        }
    }

    public bool ShouldCleanup(int freePages, long nowTicks)
    {
        if (!_config.Enabled) return false;

        var lastCleanupRun = Interlocked.Read(ref _lastCleanupRunTicks);

        return freePages > _config.MaxFreePages || nowTicks - lastCleanupRun >= _config.CleanupInterval.Ticks;
    }

    public void MarkCleanupRun(long nowTicks)
    {
        Interlocked.Exchange(ref _lastCleanupRunTicks, nowTicks);
    }

    public SegmentCandidate[] GetRetireableSegments(long nowTicks)
    {
        if (!_config.Enabled) return Array.Empty<SegmentCandidate>();

        lock (_segments)
        {
            return _segments.Values
                            .Where(x => x.FreePages == x.TotalPages)
                            .Where(x => x.LastFreeTicks > 0)
                            .Where(x => nowTicks - x.LastFreeTicks >= _config.MaxIdleTime.Ticks)
                            .OrderBy(x => x.LastFreeTicks)
                            .Take(Math.Max(1, _config.BatchSize))
                            .Select(x => new SegmentCandidate(x.SegmentId, x.TotalPages))
                            .ToArray();
        }
    }

    public bool TryRetireSegment(int segmentId)
    {
        lock (_segments)
        {
            if (!_segments.TryGetValue(segmentId, out var segment)) return false;
            if (segment.FreePages != segment.TotalPages) return false;

            _segments.Remove(segmentId);
            Interlocked.Increment(ref _retiredSegments);

            return true;
        }
    }

    internal readonly struct SegmentCandidate
    {
        public SegmentCandidate(int segmentId, int totalPages)
        {
            SegmentId = segmentId;
            TotalPages = totalPages;
        }

        public int SegmentId { get; }
        public int TotalPages { get; }
    }

    private sealed class SegmentInfo
    {
        public SegmentInfo(int segmentId, int totalPages)
        {
            SegmentId = segmentId;
            TotalPages = totalPages;
            FreePages = totalPages;
            LastFreeTicks = 0;
        }

        public int SegmentId { get; }
        public int TotalPages { get; }
        public int FreePages { get; set; }
        public long LastFreeTicks { get; set; }
    }
}



