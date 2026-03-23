using System.Collections.Generic;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1695_Tests
{
    [Fact]
    public async Task ICollection_Parameter_Test()
    {
        await using var db = new LiteDatabase(":memory:");
        var col = db.GetCollection<StateModel>("col");

        ICollection<ObjectId> ids = new List<ObjectId>();

        for (var i = 1; i <= 10; i++)
        {
            ids.Add((ObjectId)(await col.Insert(new StateModel())));
        }

        var items = await col.Query()
                             .Where(x => ids.Contains(x.Id))
                             .ToList();

        items.Should().HaveCount(10);
    }

    public class StateModel
    {
        [BsonId]
        public ObjectId Id { get; set; }
    }
}