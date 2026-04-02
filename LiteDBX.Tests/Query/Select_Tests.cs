using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class Select_Tests
{
    [Fact]
    public async Task Query_Select_Key_Only()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Address.City);

        var r0 = local.OrderBy(x => x.Address.City).Select(x => x.Address.City).ToArray();
        var r1 = await collection.Query()
                                  .OrderBy(x => x.Address.City)
                                  .Select(x => x.Address.City)
                                  .ToArray();

        r0.Should().Equal(r1);
    }

    [Fact]
    public async Task Query_Select_New_Document()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local
            .Select(x => new { city = x.Address.City.ToUpper(), phone0 = x.Phones[0], address = new Address { Street = x.Name } })
            .ToArray();

        var r1 = await collection.Query()
            .Select(x => new { city = x.Address.City.ToUpper(), phone0 = x.Phones[0], address = new Address { Street = x.Name } })
            .ToArray();

        foreach (var r in r0.Zip(r1, (l, rr) => (l, rr)))
        {
            r.rr.city.Should().Be(r.l.city);
            r.rr.phone0.Should().Be(r.l.phone0);
            r.rr.address.Street.Should().Be(r.l.address.Street);
        }
    }

    [Fact]
    public async Task Query_Or_With_Null()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var r = collection.Find(Query.Or(
            Query.GTE("Date", new DateTime(2001, 1, 1)),
            Query.EQ("Date", null)));
        // verify it streams without exception
        await foreach (var _ in r) { }
    }

    [Fact]
    public async Task Query_Find_All_Predicate()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var r = await collection.Find(x => true).ToListAsync();
        r.Should().HaveCount(1000);
    }

    [Fact]
    public async Task Query_With_No_Collection()
    {
        await using var db = await LiteDatabase.Open(":memory:");

        var reader = await db.Execute("SELECT DAY(NOW()) as DIA");
        await using (reader)
        {
            while (await reader.Read())
            {
                reader.Current["DIA"].Should().Be(DateTime.Now.Day);
            }
        }
    }
}