using System;
using System.Threading.Tasks;
using Xunit;
using static LiteDbX.Tests.Issues.Issue1838_Tests;

namespace LiteDbX.Tests.Issues;

public class Pull2468_Tests
{
    // tests if lowerinvariant works
    [Fact]
    public async Task Supports_LowerInvariant()
    {
        await using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<TestType>(nameof(TestType));

        await collection.Insert(new TestType
        {
            Foo = "Abc",
            Timestamp = DateTimeOffset.UtcNow
        });

        await collection.Insert(new TestType
        {
            Foo = "Def",
            Timestamp = DateTimeOffset.UtcNow
        });

        var result = await collection.Query()
                                     .Where(x => x.Foo.ToLowerInvariant() == "abc")
                                     .ToList();

        Assert.NotNull(result);
        Assert.Single(result);
    }

    // tests if upperinvariant works
    [Fact]
    public async Task Supports_UpperInvariant()
    {
        await using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<TestType>(nameof(TestType));

        await collection.Insert(new TestType
        {
            Foo = "Abc",
            Timestamp = DateTimeOffset.UtcNow
        });

        await collection.Insert(new TestType
        {
            Foo = "Def",
            Timestamp = DateTimeOffset.UtcNow
        });

        var result = await collection.Query()
                                     .Where(x => x.Foo.ToUpperInvariant() == "ABC")
                                     .ToList();

        Assert.NotNull(result);
        Assert.Single(result);
    }
}