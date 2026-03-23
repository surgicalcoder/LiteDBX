using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Delete_By_Name_Tests
{
    [Fact]
    public async Task Delete_By_Name()
    {
        using var f = new TempFile();
        await using var db = new LiteDatabase(f.Filename);
        var col = db.GetCollection<Person>("Person");

        await col.Insert(new Person { Fullname = "John" });
        await col.Insert(new Person { Fullname = "Doe" });
        await col.Insert(new Person { Fullname = "Joana" });
        await col.Insert(new Person { Fullname = "Marcus" });

        // lets auto-create index in FullName and delete from a non-pk node
        var del = await col.DeleteMany(x => x.Fullname.StartsWith("J"));

        del.Should().Be(2);
    }

    #region Model

    public class Person
    {
        public int Id { get; set; }
        public string Fullname { get; set; }
    }

    #endregion
}