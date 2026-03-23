using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class IndexMultiKeyIndex
{
    [Fact]
    public async Task Index_Multikey_Using_Linq()
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
        var planPhone = await queryPhone.GetPlan();
        planPhone["index"]["expr"].AsString.Should().Be("$.Phones[*]");

        var docsPhone = await queryPhone.ToArray();
        docsPhone.Length.Should().Be(1);

        var queryAddress = col.Query().Where(x => x.Addresses.Select(a => a.Street).Any(s => s == "Av.3"));
        var planAddress = await queryAddress.GetPlan();
        planAddress["index"]["expr"].AsString.Should().Be("MAP($.Addresses[*]=>@.Street)");

        var docsAddress = await queryAddress.ToArray();
        docsAddress.Length.Should().Be(2);

        var queryPhoneAll = col.Query().Where(x => x.Phones.All(p => p == 3));
        var planPhoneAll = await queryPhoneAll.GetPlan();
        planPhoneAll["index"]["expr"].AsString.Should().Be("$._id");

        var docsPhoneAll = await queryPhoneAll.ToArray();
        docsPhoneAll.Length.Should().Be(0);
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

    #endregion
}