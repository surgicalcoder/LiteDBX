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

        using (var e = new LiteEngine(settings))
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
        var settings = new EngineSettings { DataStream = data, LogStream = log, Password = "abc" };

        using (var e = new LiteEngine(settings))
        {
            await CreateDatabase(e);

            var dataStr = Encoding.UTF8.GetString(data.ToArray());
            dataStr.Should().NotContain("mycol");
            dataStr.Should().NotContain("Mauricio");

            // Use a non-owning LiteDatabase wrapper to query
            using var db = new LiteDatabase(e, disposeOnClose: false);
            var col = db.GetCollection("mycol");
            var doc = await col.FindById(1);

            doc["name"].AsString.Should().Be("Mauricio");
            (data.Length / 8192).Should().Be(5);
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