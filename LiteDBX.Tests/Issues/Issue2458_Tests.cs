using System;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

/// <summary>
/// Phase 5: migrated from sync Stream API to async ILiteFileHandle API.
/// </summary>
public class Issue2458_Tests
{
    [Fact]
    public async Task NegativeSeekFails()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var fs = db.FileStorage;
        await AddTestFile("test", 1, fs);

        await using var handle = await fs.OpenRead("test");
        // Seek(-1) must throw ArgumentOutOfRangeException
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => handle.Seek(-1).AsTask());
    }

    // https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.position?view=net-8.0
    // says seeking past the end is supported — error occurs on read, not on seek.
    [Fact]
    public async Task SeekPastFileSucceeds()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var fs = db.FileStorage;
        await AddTestFile("test", 1, fs);

        await using var handle = await fs.OpenRead("test");
        // Should not throw; position is clamped to Length.
        await handle.Seek(int.MaxValue);
    }

    [Fact]
    public async Task SeekShortChunks()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var fs = db.FileStorage;

        // Write three single-byte flushes to produce (at most) three separate buffered regions.
        await using (var writer = await fs.OpenWrite("test", "test"))
        {
            await writer.Write(new byte[] { 0 }.AsMemory());
            await writer.Flush();
            await writer.Write(new byte[] { 1 }.AsMemory());
            await writer.Flush();
            await writer.Write(new byte[] { 2 }.AsMemory());
        } // DisposeAsync calls Flush, committing the final byte and upserting metadata.

        await using var reader = await fs.OpenRead("test");
        await reader.Seek(2);

        var buf = new byte[1];
        var read = await reader.Read(buf.AsMemory());
        Assert.Equal(1, read);
        Assert.Equal(2, buf[0]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task AddTestFile(string id, long length, ILiteStorage<string> fs)
    {
        await using var writer = await fs.OpenWrite(id, id);
        await writer.Write(new byte[length].AsMemory());
    }
}

