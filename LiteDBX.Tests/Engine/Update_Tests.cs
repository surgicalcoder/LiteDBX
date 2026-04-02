using FluentAssertions;
using System.Threading.Tasks;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Update_Tests
{
    [Fact]
    public async Task Update_IndexNodes()
    {
        await using var db = await LiteEngine.Open(new EngineSettings { DataStream = new System.IO.MemoryStream() });

        var doc = new BsonDocument { ["_id"] = 1, ["name"] = "Mauricio", ["phones"] = new BsonArray { "51", "11" } };

        await db.Insert("col1", doc);
        await db.EnsureIndex("col1", "idx_name", "name", false);
        await db.EnsureIndex("col1", "idx_phones", "phones[*]", false);

        doc["name"] = "David";
        doc["phones"] = new BsonArray { "11", "25" };
        await db.Update("col1", doc);

        doc["name"] = "John";
        await db.Update("col1", doc);
    }

    [Fact]
    public async Task Update_ExtendBlocks()
    {
        await using var db = await LiteEngine.Open(new EngineSettings { DataStream = new System.IO.MemoryStream() });

        var doc = new BsonDocument { ["_id"] = 1, ["d"] = new byte[1000] };
        await db.Insert("col1", doc);

        // small (same page)
        doc["d"] = new byte[300];
        await db.Update("col1", doc);
        var page3 = await db.GetPageLog(3);
        page3["freeBytes"].AsInt32.Should().Be(7828);

        // big (same page)
        doc["d"] = new byte[2000];
        await db.Update("col1", doc);
        page3 = await db.GetPageLog(3);
        page3["freeBytes"].AsInt32.Should().Be(6128);

        // big (extend page)
        doc["d"] = new byte[20000];
        await db.Update("col1", doc);
        page3 = await db.GetPageLog(3);
        var page4 = await db.GetPageLog(4);
        var page5 = await db.GetPageLog(5);
        page3["freeBytes"].AsInt32.Should().Be(0);
        page4["freeBytes"].AsInt32.Should().Be(0);
        page5["freeBytes"].AsInt32.Should().Be(4428);

        // small (shrink page)
        doc["d"] = new byte[10000];
        await db.Update("col1", doc);
        page3 = await db.GetPageLog(3);
        page4 = await db.GetPageLog(4);
        page5 = await db.GetPageLog(5);
        page3["freeBytes"].AsInt32.Should().Be(0);
        page4["freeBytes"].AsInt32.Should().Be(6278);
        page5["pageType"].AsString.Should().Be("Empty");
    }

    [Fact]
    public async Task Update_Empty_Collection()
    {
        await using var e = await LiteEngine.Open(new EngineSettings { DataStream = new System.IO.MemoryStream() });
        var d = new BsonDocument { ["_id"] = 1, ["a"] = "demo" };
        var r = await e.Update("col1", new[] { d });
        r.Should().Be(0);
    }
}