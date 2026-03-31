using System;
using System.IO;
using System.Text;
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

    [Fact]
    public async Task Crypto_Datafile()
    {
        var data = new MemoryStream();
        var log = new MemoryStream();
        var settings = new EngineSettings { DataStream = data, LogStream = log, Password = "abc", AESEncryption = AESEncryptionType.ECB };

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

    [Fact]
    public void Crypto_Datafile_Gcm_Without_Provider_Throws()
    {
        var data = new MemoryStream();
        var log = new MemoryStream();

        System.Action act = () =>
        {
            _ = new LiteEngine(new EngineSettings
            {
                DataStream = data,
                LogStream = log,
                Password = "abc",
                AESEncryption = AESEncryptionType.GCM
            });
        };

        act.Should().Throw<LiteException>()
            .Where(x => x.ErrorCode == LiteException.ENCRYPTION_PROVIDER_NOT_REGISTERED);
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