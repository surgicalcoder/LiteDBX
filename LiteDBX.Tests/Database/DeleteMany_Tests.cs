using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class DeleteMany_Tests
{
    [Fact]
    public async Task DeleteMany_With_Arguments()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var c1 = db.GetCollection("Test");

        var d1 = new BsonDocument { ["_id"] = 1, ["p1"] = 1 };
        await c1.Insert(d1);

        (await c1.Count()).Should().Be(1);

        // try BsonExpression predicate with argument - not deleted
        var e1 = BsonExpression.Create("$._id = @0", 1);
        var r1 = await c1.DeleteMany(e1);

        r1.Should().Be(1);

        // the same BsonExpression predicate works fine in FindOne
        var r = await c1.FindOne(e1);
    }
}