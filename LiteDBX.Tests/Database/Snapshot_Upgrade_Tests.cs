using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

/// <summary>
/// Transaction semantics test: update + upsert within an explicit async transaction scope.
/// The old ambient BeginTrans/Commit pattern is replaced with ILiteTransaction.
/// </summary>
public class Snapshot_Upgrade_Tests
{
    [Fact]
    public async Task Transaction_Update_Upsert()
    {
        await using var db = new LiteDatabase(":memory:");
        var col = db.GetCollection("test");

        await using var tx = await db.BeginTransaction();

        var updatedDocs = await col.UpdateMany("{name: \"xxx\"}", BsonExpression.Create("_id = 1"));
        Assert.Equal(0, updatedDocs);

        await col.Upsert(new BsonDocument { ["_id"] = 1, ["name"] = "xxx" });
        var result = await col.FindById(1);
        Assert.NotNull(result);

        await tx.Commit();
    }
}