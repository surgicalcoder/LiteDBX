using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class OrderBy_Tests
{
    [Fact]
    public async Task Query_OrderBy_Using_Index()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Name);

        var r0 = local.OrderBy(x => x.Name).Select(x => new { x.Name }).ToArray();
        var r1 = await collection.Query().OrderBy(x => x.Name).Select(x => new { x.Name }).ToArray();

        r0.Should().Equal(r1);
    }

    [Fact]
    public async Task Query_OrderBy_Using_Index_Desc()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Name);

        var r0 = local.OrderByDescending(x => x.Name).Select(x => new { x.Name }).ToArray();
        var r1 = await collection.Query().OrderByDescending(x => x.Name).Select(x => new { x.Name }).ToArray();

        r0.Should().Equal(r1);
    }

    [Fact]
    public async Task Query_OrderBy_With_Func()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Date.Day);

        var r0 = local.OrderBy(x => x.Date.Day).Select(x => new { d = x.Date.Day }).ToArray();
        var r1 = await collection.Query().OrderBy(x => x.Date.Day).Select(x => new { d = x.Date.Day }).ToArray();

        r0.Should().Equal(r1);
    }

    [Fact]
    public async Task Query_OrderBy_With_Offset_Limit()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.OrderBy(x => x.Date.Day).Select(x => new { d = x.Date.Day }).Skip(5).Take(10).ToArray();
        var r1 = await collection.Query()
                                  .OrderBy(x => x.Date.Day)
                                  .Select(x => new { d = x.Date.Day })
                                  .Offset(5).Limit(10)
                                  .ToArray();

        r0.Should().Equal(r1);
    }

    [Fact]
    public async Task Query_Asc_Desc()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var asc  = await collection.Find(Query.All(Query.Ascending)).ToListAsync();
        var desc = await collection.Find(Query.All(Query.Descending)).ToListAsync();

        asc[0].Id.Should().Be(1);
        desc[0].Id.Should().Be(1000);
    }
}