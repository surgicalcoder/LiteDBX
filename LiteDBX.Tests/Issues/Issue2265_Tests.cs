using System;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

// issue 2265
public class Issue2265_Tests
{
    [Fact]
    public async Task Test()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var c = db.GetCollection<Weights>("weights");
        var w = await c.FindOne(x => true);

        if (w == null)
        {
            w = new Weights();
            await c.Insert(w);
        }
    }

    public class Weights
    {
        public Weights(int id, Weights[] parents)
        {
            Id = id;
            Parents = parents;
        }

        public Weights()
        {
            Id = 0;
            Parents = Array.Empty<Weights>();
        }

        public int Id { get; set; }

        // comment out [BsonRef] and the the test works
        [BsonRef("weights")]
        public Weights[] Parents { get; set; }
    }
}