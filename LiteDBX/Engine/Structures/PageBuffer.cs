using System.Threading;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// Represent page buffer to be read/write using FileMemory
/// </summary>
internal class PageBuffer : BufferSlice
{
    /// <summary>
    /// Get, on initialize, a unique ID in all database instance for this PageBufer. Is a simple global incremented counter
    /// </summary>
    public readonly int UniqueID;

    /// <summary>
    /// Get the memory segment this page belongs to. 0 means this page was allocated as a standalone buffer.
    /// </summary>
    public readonly int SegmentId;

    /// <summary>
    /// Get/Set page bytes origin (data/log)
    /// </summary>
    public FileOrigin Origin;

    /// <summary>
    /// Get/Set page position. If page are writable, this postion CAN be MaxValue (has not defined position yet)
    /// </summary>
    public long Position;

    /// <summary>
    /// Get/Set how many read-share threads are using this page. -1 means 1 thread are using as writable
    /// </summary>
    public int ShareCounter;

    /// <summary>
    /// Get/Set timestamp from last request
    /// </summary>
    public long Timestamp;

    /// <summary>
    /// Get/Set timestamp from when this page last returned to the free queue.
    /// </summary>
    public long? TimestampFree;

    public PageBuffer(byte[] buffer, int offset, int uniqueID, int segmentId = 0)
        : base(buffer, offset, PAGE_SIZE)
    {
        UniqueID = uniqueID;
        SegmentId = segmentId;
        Position = long.MaxValue;
        Origin = FileOrigin.None;
        ShareCounter = 0;
        Timestamp = 0;
        TimestampFree = null;
    }

    /// <summary>
    /// Release this page - decrement ShareCounter
    /// </summary>
    public void Release()
    {
        ENSURE(ShareCounter > 0, "share counter must be > 0 in Release()");

        Interlocked.Decrement(ref ShareCounter);
    }

#if DEBUG
    ~PageBuffer()
    {
        ENSURE(ShareCounter == 0, $"share count must be 0 in destroy PageBuffer (current: {ShareCounter})");
    }
#endif

    public override string ToString()
    {
        var p = Position == long.MaxValue ? "<empty>" : Position.ToString();
        var s = ShareCounter == BUFFER_WRITABLE ? "<writable>" : ShareCounter.ToString();
        var pageID = this.ReadUInt32(0);
        var pageType = this[4];

        return $"ID: {UniqueID} - Position: {p}/{Origin} - Shared: {s} - ({base.ToString()}) :: Content: [{pageID.ToString("0:0000")}/{(PageType)pageType}]";
    }

    public unsafe bool IsBlank()
    {
        fixed (byte* arrayPtr = Array)
        {
            var ptr = (ulong*)(arrayPtr + Offset);

            return *ptr == 0UL && *(ptr + 1) == 0UL;
        }
    }
}