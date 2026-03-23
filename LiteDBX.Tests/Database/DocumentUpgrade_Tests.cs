using System.IO;
using FluentAssertions;
using LiteDbX.Engine;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class DocumentUpgrade_Tests
{
    [Fact]
    public async Task DocumentUpgrade_Test()
    {
        var ms = new MemoryStream();

        await using (var db = new LiteDatabase(ms))
        {
            var col = db.GetCollection("col");
            await col.Insert(new BsonDocument { ["version"] = 1, ["_id"] = 1, ["name"] = "John" });
        }

        ms.Position = 0;

        await using (var db = new LiteDatabase(ms))
        {
            var col = db.GetCollection("col");
            (await col.Count()).Should().Be(1);
            var doc = await col.FindById(1);
            doc["version"].AsInt32.Should().Be(1);
            doc["name"].AsString.Should().Be("John");
            doc["age"].AsInt32.Should().Be(0);
        }

        ms.Position = 0;

        await using var engine = new LiteEngine(new EngineSettings
        {
            DataStream = ms,
            ReadTransform = (collectionName, val) =>
            {
                if (val is not BsonDocument doc) return val;
                if (doc.TryGetValue("version", out var version) && version.AsInt32 == 1)
                {
                    doc["version"] = 2;
                    doc["age"] = 30;
                }
                return val;
            }
        });

        await using (var db = new LiteDatabase(engine, disposeOnClose: false))
        {
            var col = db.GetCollection("col");
            (await col.Count()).Should().Be(1);
            var doc = await col.FindById(1);
            doc["version"].AsInt32.Should().Be(2);
            doc["name"].AsString.Should().Be("John");
            doc["age"].AsInt32.Should().Be(30);
        }
    }

    [Fact]
    public async Task DocumentUpgrade_BsonMapper_Test()
    {
        var ms = new MemoryStream();

        await using (var db = new LiteDatabase(ms))
        {
            var col = db.GetCollection("col");
            await col.Insert(new BsonDocument { ["version"] = 1, ["_id"] = 1, ["name"] = "John" });
        }

        ms.Position = 0;

        await using (var db = new LiteDatabase(ms))
        {
            var col = db.GetCollection("col");
            (await col.Count()).Should().Be(1);
            var doc = await col.FindById(1);
            doc["version"].AsInt32.Should().Be(1);
            doc["name"].AsString.Should().Be("John");
            doc["age"].AsInt32.Should().Be(0);
        }

        ms.Position = 0;

        var mapper = new BsonMapper();
        mapper.OnDeserialization = (sender, type, val) =>
        {
            if (val is not BsonDocument doc) return val;
            if (doc.TryGetValue("version", out var version) && version.AsInt32 == 1)
            {
                doc["version"] = 2;
                doc["age"] = 30;
            }
            return doc;
        };

        await using (var db = new LiteDatabase(ms, mapper))
        {
            var col = db.GetCollection("col");
            (await col.Count()).Should().Be(1);
            var doc = await col.FindById(1);
            doc["version"].AsInt32.Should().Be(2);
            doc["name"].AsString.Should().Be("John");
            doc["age"].AsInt32.Should().Be(30);
        }
    }
}