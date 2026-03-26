using System;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Internals;

public class CacheTrim_Tests
{
    [Fact]
    public void Discarded_Page_Tracks_TimestampFree_And_Clears_It_On_Reuse()
    {
        var cache = CreateTrimEnabledCache();
        var page = cache.NewPage();

        page.TimestampFree.Should().BeNull();

        cache.DiscardPage(page);

        page.TimestampFree.Should().NotBeNull();

        var reused = cache.NewPage();

        reused.TimestampFree.Should().BeNull();

        cache.DiscardPage(reused);
    }

    [Fact]
    public void Cache_Trim_Retires_A_Fully_Free_Idle_Segment()
    {
        var cache = CreateTrimEnabledCache();
        var first = cache.NewPage();
        var second = cache.NewPage();

        cache.DiscardPage(first);
        cache.DiscardPage(second);

        cache.ActiveSegments.Should().Be(1);
        cache.RetiredSegments.Should().Be(0);
        cache.FreePages.Should().Be(2);

        var reused = cache.NewPage();

        cache.RetiredSegments.Should().Be(1);
        cache.ActiveSegments.Should().Be(1);
        cache.FreePages.Should().Be(1);
        reused.SegmentId.Should().NotBe(0);

        cache.DiscardPage(reused);
    }

    [Fact]
    public void Cache_Trim_Does_Not_Retire_Segment_When_A_Writable_Sibling_Is_Still_In_Use()
    {
        var cache = CreateTrimEnabledCache();
        var first = cache.NewPage();
        var second = cache.NewPage();

        cache.DiscardPage(first);

        var reused = cache.NewPage();

        cache.RetiredSegments.Should().Be(0);
        cache.ActiveSegments.Should().Be(1);
        reused.UniqueID.Should().Be(first.UniqueID);

        cache.DiscardPage(reused);
        cache.DiscardPage(second);
    }

    [Fact]
    public void Cache_Trim_Does_Not_Retire_Segment_When_A_Readable_Sibling_Is_Still_Cached()
    {
        var cache = CreateTrimEnabledCache();
        var readableSource = cache.NewPage();
        var writableSibling = cache.NewPage();

        readableSource.Origin = FileOrigin.Log;
        readableSource.Position = 0;
        cache.TryMoveToReadable(readableSource).Should().BeTrue();

        cache.DiscardPage(writableSibling);

        var reused = cache.NewPage();

        cache.RetiredSegments.Should().Be(0);
        cache.ActiveSegments.Should().Be(1);
        reused.UniqueID.Should().Be(writableSibling.UniqueID);

        var readable = cache.GetReadablePage(0, FileOrigin.Log, (_, _) => { });
        readable.UniqueID.Should().Be(readableSource.UniqueID);
        readable.Release();

        cache.DiscardPage(reused);
    }

    private static MemoryCache CreateTrimEnabledCache(int segmentSize = 2)
    {
        return new MemoryCache(
            new[] { segmentSize },
            new MemoryCacheConfig
            {
                Enabled = true,
                MaxFreePages = 0,
                MaxIdleTime = TimeSpan.Zero,
                CleanupInterval = TimeSpan.Zero,
                BatchSize = 10
            });
    }
}

