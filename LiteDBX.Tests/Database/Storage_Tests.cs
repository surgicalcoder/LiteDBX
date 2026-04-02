using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Storage_Tests
{
    private readonly byte[] _bigFile;
    private readonly string _bigHash;
    private readonly Random _rnd = new();
    private readonly byte[] _smallFile;
    private readonly string _smallHash;

    public Storage_Tests()
    {
        _smallFile = new byte[_rnd.Next(100000, 200000)];
        _bigFile = new byte[_rnd.Next(400000, 600000)];

        _rnd.NextBytes(_smallFile);
        _rnd.NextBytes(_bigFile);

        _smallHash = HashBytes(_smallFile);
        _bigHash = HashBytes(_bigFile);
    }

    [Fact]
    public async Task Storage_Upload_Download()
    {
        using var f = new TempFile();
        await using var db = await LiteDatabase.Open(f.Filename);
        var fs = db.GetStorage<int>();

        // ── Upload two files ──────────────────────────────────────────────────
        var small = await fs.Upload(10, "photo_small.png", new MemoryStream(_smallFile));
        var big   = await fs.Upload(100, "photo_big.png",  new MemoryStream(_bigFile));

        _smallFile.Length.Should().Be((int)small.Length);
        _bigFile.Length.Should().Be((int)big.Length);

        // ── Find by predicate ─────────────────────────────────────────────────
        LiteFileInfo<int> f0 = null, f1 = null;

        await foreach (var file in fs.Find(x => x.Filename == "photo_small.png"))
            f0 = file;

        await foreach (var file in fs.Find(x => x.Filename == "photo_big.png"))
            f1 = file;

        f0.Should().NotBeNull();
        f1.Should().NotBeNull();

        // ── Verify content round-trip via Download ────────────────────────────
        var smallMs = new MemoryStream();
        await fs.Download(f0!.Id, smallMs);
        HashBytes(smallMs.ToArray()).Should().Be(_smallHash);

        var bigMs = new MemoryStream();
        await fs.Download(f1!.Id, bigMs);
        HashBytes(bigMs.ToArray()).Should().Be(_bigHash);

        // ── Overwrite small with big content ──────────────────────────────────
        var repl = await fs.Upload(10, "new_photo.jpg", new MemoryStream(_bigFile));

        (await fs.Exists(10)).Should().BeTrue();

        var nrepl = await fs.FindById(10);
        nrepl.Chunks.Should().Be(repl.Chunks);

        // ── Update and find by metadata ───────────────────────────────────────
        await fs.SetMetadata(100, new BsonDocument { ["x"] = 100, ["y"] = 99 });

        LiteFileInfo<int> md = null;
        await foreach (var file in fs.Find(x => x.Metadata["x"] == 100))
            md = file;

        md.Should().NotBeNull();
        md!.Metadata["y"].AsInt32.Should().Be(99);
    }

    [Fact]
    public async Task Storage_Empty_Upload_Persists_Metadata_And_Can_Be_Found()
    {
        using var f = new TempFile();
        await using var db = await LiteDatabase.Open(f.Filename);
        var fs = db.GetStorage<string>("myFiles", "myChunks");

        await fs.Upload("photos/2014/picture-01.jpg", "picture-01.jpg", new MemoryStream());

        var file = await fs.FindById("photos/2014/picture-01.jpg");
        file.Should().NotBeNull();
        file!.Length.Should().Be(0);
        file.Chunks.Should().Be(0);

        var files = new System.Collections.Generic.List<LiteFileInfo<string>>();
        await foreach (var item in fs.Find("_id LIKE 'photos/2014/%'"))
        {
            files.Add(item);
        }

        files.Should().ContainSingle();
        files[0].Id.Should().Be("photos/2014/picture-01.jpg");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string HashBytes(byte[] input)
    {
        using var md5 = MD5.Create();
        return Convert.ToBase64String(md5.ComputeHash(input));
    }
}

