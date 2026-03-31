using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Crypto_Tests
{
    [Fact]
    public async Task Plain_Datafile()
    {
        var data = new MemoryStream();
        var log = new MemoryStream();
        var settings = new EngineSettings { DataStream = data, LogStream = log };

        await using (var e = new LiteEngine(settings))
        {
            await CreateDatabase(e);

            var dataStr = Encoding.UTF8.GetString(data.ToArray());
            dataStr.Should().Contain("mycol");
            dataStr.Should().Contain("Mauricio");
            (data.Length / 8192).Should().Be(4);
        }
    }

    [Theory]
    [InlineData(AESEncryptionType.ECB)]
    [InlineData(AESEncryptionType.GCM)]
    public async Task Crypto_Datafile(AESEncryptionType encryptionType)
    {
        var data = new MemoryStream();
        var log = new MemoryStream();
        var settings = new EngineSettings { DataStream = data, LogStream = log, Password = "abc", AESEncryption = encryptionType };

        using (var e = new LiteEngine(settings))
        {
            await CreateDatabase(e);

            var dataStr = Encoding.UTF8.GetString(data.ToArray());
            dataStr.Should().NotContain("mycol");
            dataStr.Should().NotContain("Mauricio");

            // Use a non-owning LiteDatabase wrapper to query
            await using var db = new LiteDatabase(e, disposeOnClose: false);
            var col = db.GetCollection("mycol");
            var doc = await col.FindById(1);

            doc["name"].AsString.Should().Be("Mauricio");
            (data.Length / 8192).Should().Be(5);
        }
    }

    [Theory]
    [InlineData(AESEncryptionType.ECB)]
    [InlineData(AESEncryptionType.GCM)]
    public async Task Crypto_File_Datafile_Can_Reopen_With_Opposite_Config(AESEncryptionType encryptionType)
    {
        var file = Path.Combine(Path.GetTempPath(), $"litedbx-crypto-{Guid.NewGuid():N}.db");

        try
        {
            var createSettings = new EngineSettings
            {
                Filename = file,
                Password = "abc",
                AESEncryption = encryptionType
            };

            await using (var engine = new LiteEngine(createSettings))
            {
                await CreateDatabase(engine);
            }

            var fileText = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            fileText.Should().NotContain("mycol");
            fileText.Should().NotContain("Mauricio");

            var reopenSettings = new EngineSettings
            {
                Filename = file,
                Password = "abc",
                AESEncryption = encryptionType == AESEncryptionType.ECB ? AESEncryptionType.GCM : AESEncryptionType.ECB
            };

            await using (var reopenedEngine = new LiteEngine(reopenSettings))
            await using (var db = new LiteDatabase(reopenedEngine, disposeOnClose: false))
            {
                var col = db.GetCollection("mycol");
                var doc = await col.FindById(1);

                doc["name"].AsString.Should().Be("Mauricio");
            }
        }
        finally
        {
            for (var attempt = 0; attempt < 5 && File.Exists(file); attempt++)
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException) when (attempt < 4)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);
                }
            }
        }
    }

    private static async Task CreateDatabase(LiteEngine engine)
    {
        await engine.Insert("mycol", new[]
        {
            new BsonDocument { ["_id"] = 1, ["name"] = "Mauricio" }
        }, BsonAutoId.Int32);

        await engine.Checkpoint();
    }
}