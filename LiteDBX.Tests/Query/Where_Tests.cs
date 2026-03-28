using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class Where_Tests
{
    [Fact]
    public async Task Query_Where_With_Parameter()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Address.State == "FL").ToArray();
        var r1 = await collection.Query().Where(x => x.Address.State == "FL").ToArray();

        AssertEx.ArrayEqual(r0, r1, true);
    }

    [Fact]
    public async Task Query_Multi_Where_With_Like()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Age >= 10 && x.Age <= 40).Where(x => x.Name.StartsWith("Ge")).ToArray();
        var r1 = await collection.Query()
                                  .Where(x => x.Age >= 10 && x.Age <= 40)
                                  .Where(x => x.Name.StartsWith("Ge"))
                                  .ToArray();

        AssertEx.ArrayEqual(r0, r1, true);
    }

    [Fact]
    public async Task Query_Single_Where_With_And()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Age == 25 && x.Active).ToArray();
        var r1 = await collection.Query().Where("age = 25 AND active = true").ToArray();

        AssertEx.ArrayEqual(r0, r1, true);
    }

    [Fact]
    public async Task Query_Single_Where_With_Or_And_In()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Age == 25 || x.Age == 26 || x.Age == 27).ToArray();
        var r1 = await collection.Query().Where("age = 25 OR age = 26 OR age = 27").ToArray();
        var r2 = await collection.Query().Where("age IN [25, 26, 27]").ToArray();

        AssertEx.ArrayEqual(r0, r1, true);
        AssertEx.ArrayEqual(r1, r2, true);
    }

    [Fact]
    public async Task Query_With_Array_Ids()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var ids = new[] { 1, 2, 3 };
        var r0 = local.Where(x => ids.Contains(x.Id)).ToArray();
        var r1 = await collection.Query().Where(x => ids.Contains(x.Id)).ToArray();

        AssertEx.ArrayEqual(r0, r1, true);
    }

    [Fact]
    public async Task Query_Where_With_Constant_Boolean_Predicates()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var all = await collection.Query().Where(x => true).ToArray();
        var none = await collection.Query().Where(x => false).ToArray();

        AssertEx.ArrayEqual(local, all, true);
        none.Should().BeEmpty();
    }

    private class Entity
    {
        public string Name { get; set; }
        public int Size { get; set; }
    }
}