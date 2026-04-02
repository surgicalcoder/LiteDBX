using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1838_Tests
{
    [Fact]
    public async Task Find_ByDatetime_Offset()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection<TestType>(nameof(TestType));

        // sample data
        await collection.Insert(new TestType
        {
            Foo = "abc",
            Timestamp = DateTimeOffset.UtcNow
        });
        await collection.Insert(new TestType
        {
            Foo = "def",
            Timestamp = DateTimeOffset.UtcNow
        });

        // filter from 1 hour in the past to 1 hour in the future
        var timeRange = TimeSpan.FromHours(2);

        var result = await collection // throws exception
                           .Find(x => x.Timestamp > DateTimeOffset.UtcNow - timeRange)
                           .ToListAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    public class TestType
    {
        [BsonId]
        public int Id { get; set; }

        [BsonField]
        public string Foo { get; set; }

        [BsonField]
        public DateTimeOffset Timestamp { get; set; }
    }
}