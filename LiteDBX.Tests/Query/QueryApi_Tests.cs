using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class QueryApi_Tests
{
    [Fact]
    public async Task Query_And()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Age == 22 && x.Active).ToArray();
        var r1 = await collection.Find(Query.And(Query.EQ("Age", 22), Query.EQ("Active", true))).ToListAsync();

        AssertEx.ArrayEqual(r0, r1.ToArray(), true);
    }

    [Fact]
    public async Task Query_And_Same_Field()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var r0 = local.Where(x => x.Age > 22 && x.Age < 25).ToArray();
        var r1 = await collection.Find(Query.And(Query.GT("Age", 22), Query.LT("Age", 25))).ToListAsync();

        AssertEx.ArrayEqual(r0, r1.ToArray(), true);
    }
}