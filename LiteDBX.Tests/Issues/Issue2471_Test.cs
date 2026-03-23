using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2471_Test
{
    [Fact]
    public async Task TestFragmentDB_FindByIDException()
    {
        await using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<object>("fragtest");

        var fragment = new object();
        var id = await collection.Insert(fragment);

        id.AsInt32.Should().BeGreaterThan(0);

        var frag2 = await collection.FindById(id);
        frag2.Should().NotBeNull();

        var act = async () => await db.Checkpoint();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task MultipleReadCleansUpTransaction()
    {
        await using var database = new LiteDatabase(":memory:");
        var collection = database.GetCollection("test");
        await collection.Insert(new BsonDocument { ["_id"] = 1 });

        for (var i = 0; i < 500; i++)
        {
            await collection.FindById(1);
        }
    }

    [Fact]
    public async Task Ensure_Query_GetPlan_Releases_Lock()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<User>();

        await col.Insert(new User { Name = "John Doe", Phones = new[] { 1, 3, 5 }, Addresses = new List<Address> { new() { Street = "Av.1" }, new() { Street = "Av.3" } } });
        await col.Insert(new User { Name = "Joana Mark", Phones = new[] { 1, 4 }, Addresses = new List<Address> { new() { Street = "Av.3" } } });

        await col.EnsureIndex(x => x.Phones);
        await col.EnsureIndex(x => x.Addresses.Select(z => z.Street));

        var indexes = await db.GetCollection("$indexes").FindAll().ToArrayAsync();
        indexes[1]["expression"].AsString.Should().Be("$.Phones[*]");
        indexes[2]["expression"].AsString.Should().Be("MAP($.Addresses[*]=>@.Street)");

        var queryPhone = col.Query().Where(x => x.Phones.Contains(3));
        await queryPhone.GetPlan();

        var act = async () => await db.Checkpoint();
        await act.Should().NotThrowAsync();
    }

    #region Model

    public class User
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int[] Phones { get; set; }
        public List<Address> Addresses { get; set; }
    }

    public class Address
    {
        public string Street { get; set; }
    }

    #endregion Model
}