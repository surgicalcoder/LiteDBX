using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class Mapper_Tests
{
    private readonly BsonMapper _mapper = new();

    [Fact]
    public void ToDocument_ReturnsNull_WhenFail()
    {
        var array = new[] { 1, 2, 3, 4, 5 };
        var doc1 = _mapper.ToDocument(array);
        doc1.Should<BsonDocument>().Be(null);

        var doc2 = _mapper.ToDocument(typeof(int[]), array);
        doc2.Should<BsonDocument>().Be(null);
    }

    [Fact]
    public async Task Class_Not_Assignable()
    {
        await using (var db = new LiteDatabase(":memory:"))
        {
            var col = db.GetCollection<MyClass>("Test");
            await col.Insert(new MyClass { Id = 1, Member = null });
            var type = typeof(OtherClass);
            var typeName = type.FullName + ", " + type.GetTypeInfo().Assembly.GetName().Name;

            await db.Execute($"update Test set Member = {{_id: 1, Name: null, _type: \"{typeName}\"}} where _id = 1");

            var act = async () => await col.FindById(1);
            await act.Should().ThrowAsync<LiteException>();
        }
    }

    public class MyClass
    {
        public int Id { get; set; }
        public MyClass Member { get; set; }
    }

    public class OtherClass
    {
        public string Name { get; set; }
    }
}