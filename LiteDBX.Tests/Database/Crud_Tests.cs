using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Crud_Tests
{
    [Fact]
    public async Task Insert_With_AutoId()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var users = db.GetCollection<User>("users");

        var u1 = new User { Name = "John" };
        var u2 = new User { Name = "Zarlos" };
        var u3 = new User { Name = "Ana" };

        // insert ienumerable
        await users.Insert(new[] { u1, u2 });
        await users.Insert(u3);

        // test auto-id
        u1.Id.Should().Be(1);
        u2.Id.Should().Be(2);
        u3.Id.Should().Be(3);

        // adding without autoId
        var u4 = new User { Id = 20, Name = "Marco" };
        await users.Insert(u4);

        // adding more auto id after fixed id
        var u5 = new User { Name = "Julio" };
        await users.Insert(u5);

        u5.Id.Should().Be(21);
    }

    [Fact]
    public async Task Delete_Many()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var users = db.GetCollection<User>("users");

        var u1 = new User { Id = 1, Name = "John" };
        var u2 = new User { Id = 2, Name = "Zarlos" };
        var u3 = new User { Id = 3, Name = "Ana" };

        await users.Insert(new[] { u1, u2, u3 });

        var ids = new[] { 1, 2, 3 };

        var docs = await users.Query().Where(x => ids.Contains(x.Id)).ToArray();

        await users.DeleteMany(x => ids.Contains(x.Id));

        (await users.Count()).Should().Be(0);
    }

    #region Model

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    #endregion
}