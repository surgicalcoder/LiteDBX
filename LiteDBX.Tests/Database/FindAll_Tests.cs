using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class FindAll_Tests
{
    [Fact]
    public async Task FindAll()
    {
        using var f = new TempFile();

        await using (var db = await LiteDatabase.Open(f.Filename))
        {
            var col = db.GetCollection<Person>("Person");

            await col.Insert(new Person { Fullname = "John" });
            await col.Insert(new Person { Fullname = "Doe" });
            await col.Insert(new Person { Fullname = "Joana" });
            await col.Insert(new Person { Fullname = "Marcus" });
        }
        // close datafile

        await using (var db = await LiteDatabase.Open(f.Filename))
        {
            var p = await db.GetCollection<Person>("Person")
                             .Find(Query.All("Fullname"))
                             .ToListAsync();

            p.Count.Should().Be(4);
        }
    }

    #region Model

    public class Person
    {
        public int Id { get; set; }
        public string Fullname { get; set; }
    }

    #endregion
}