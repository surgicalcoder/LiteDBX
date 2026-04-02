using FluentAssertions;
using System.Threading.Tasks;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2487_tests
{
    [Fact]
    public async Task Test_Contains_EmptyStrings()
    {
        await using var engine = await LiteEngine.Open(new EngineSettings { Filename = ":memory:" });

        await using var db = new LiteDatabase(engine, disposeOnClose: false);
        var collection = db.GetCollection<DataClass>("data");

        await collection.Insert(new DataClass { Foo = "bar", Bar = "abc" });
        await collection.Insert(new DataClass { Foo = " ", Bar = "def" });
        await collection.Insert(new DataClass { Foo = "fo bar", Bar = "def" });
        await collection.Insert(new DataClass { Foo = "", Bar = "def" });
        await collection.Insert(new DataClass { Foo = null, Bar = "def" });

        var containsAction = () => collection.FindOne(x => x.Foo.Contains(" "));
        containsAction.Should().NotThrow();

        var def = await containsAction();
        def.Should().NotBeNull();
        def.Bar.Should().Be("def");

        var shouldExecute = () => engine.Query("data", Query.All(Query.Contains("Foo", " ")));
        shouldExecute.Should().NotThrow();
    }

    private class DataClass
    {
        [BsonId]
        public int Id { get; set; }

        public string Foo { get; set; }

        public string Bar { get; set; }
    }
}