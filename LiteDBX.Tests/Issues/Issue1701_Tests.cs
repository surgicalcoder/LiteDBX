using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1701_Tests
{
    [Fact]
    public async Task Deleted_Index_Slot_Test()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection("col", BsonAutoId.Int32);
        var id = await col.Insert(new BsonDocument { ["attr1"] = "attr", ["attr2"] = "attr", ["attr3"] = "attr" });

        await col.EnsureIndex("attr1", "$.attr1");
        await col.EnsureIndex("attr2", "$.attr2");
        await col.EnsureIndex("attr3", "$.attr3");
        await col.DropIndex("attr2");

        await col.Update(id, new BsonDocument { ["attr1"] = "new" });
    }
}