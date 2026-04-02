using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2570_Tests
{
    [Fact]
    public async Task Issue2570_Tuples()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<Person>("Person");

        await col.Insert(new Person { Name = ("John", "Doe") });
        await col.Insert(new Person { Name = ("Joana", "Doe") });

        var result = await col.FindOne(x => x.Name.FirstName == "John");

        result.Should().NotBeNull();
        result.Name.FirstName.Should().Be("John");
        result.Name.LastName.Should().Be("Doe");
    }

    [Fact]
    public async Task Issue2570_Structs()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col = db.GetCollection<PersonWithStruct>("Person");

        await col.Insert(new PersonWithStruct { Name = new PersonData { FirstName = "John", LastName = "Doe" } });
        await col.Insert(new PersonWithStruct { Name = new PersonData { FirstName = "Joana", LastName = "Doe" } });

        var result = await col.FindOne(x => x.Name.FirstName == "John");

        result.Should().NotBeNull();
        result.Name.FirstName.Should().Be("John");
        result.Name.LastName.Should().Be("Doe");
    }

    public class Person
    {
        public int Id { get; set; }

        public (string FirstName, string LastName) Name { get; set; }
    }

    public struct PersonData
    {
        public string FirstName;
        public string LastName;
    }

    public class PersonWithStruct
    {
        public int Id { get; set; }

        public PersonData Name { get; set; }
    }
}