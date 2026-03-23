using FluentAssertions;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class DropCollection_Tests
{
    [Fact]
    public async Task DropCollection()
    {
        using var file = new TempFile();
        await using var db = new LiteDatabase(file.Filename);

        var names = await db.GetCollectionNames().ToListAsync();
        names.Should().NotContain("col");

        var col = db.GetCollection("col");
        await col.Insert(new BsonDocument { ["a"] = 1 });

        (await db.GetCollectionNames().ToListAsync()).Should().Contain("col");

        await db.DropCollection("col");

        (await db.GetCollectionNames().ToListAsync()).Should().NotContain("col");
    }

    [Fact]
    public async Task InsertDropCollection()
    {
        using var file = new TempFile();

        await using (var db = new LiteDatabase(file.Filename))
        {
            var col = db.GetCollection("test");
            await col.Insert(new BsonDocument { ["_id"] = 1 });
            await db.DropCollection("test");
            await db.Rebuild();
        }

        await using (var db = new LiteDatabase(file.Filename))
        {
            var col = db.GetCollection("test");
            await col.Insert(new BsonDocument { ["_id"] = 1 });
        }
    }
}