using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class Queryable_Execution_Tests
{
    [Fact]
    public async Task Queryable_ToListAsync_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name });

        var expectedNative = await collection.Query()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToList();

        var expectedLocal = local
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToList();

        var actual = await queryable.ToListAsync();

        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_ToArrayAsync_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .Where(x => x.Address.State == "FL")
            .OrderByDescending(x => x.Age)
            .ThenBy(x => x.Name)
            .Select(x => x.Address.City)
            .Take(15);

        var expectedNative = await collection.Query()
            .Where(x => x.Address.State == "FL")
            .OrderByDescending(x => x.Age)
            .ThenBy(x => x.Name)
            .Select(x => x.Address.City)
            .Limit(15)
            .ToArray();

        var expectedLocal = local
            .Where(x => x.Address.State == "FL")
            .OrderByDescending(x => x.Age)
            .ThenBy(x => x.Name)
            .Select(x => x.Address.City)
            .Take(15)
            .ToArray();

        var actual = await queryable.ToArrayAsync();

        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_FirstAsync_And_FirstOrDefaultAsync_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var firstQuery = collection.AsQueryable()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name });

        var expectedFirst = await collection.Query()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .First();

        var actualFirst = await firstQuery.FirstAsync();
        actualFirst.Should().BeEquivalentTo(expectedFirst);

        var emptyQuery = collection.AsQueryable()
            .Where(x => x.Id < 0)
            .Select(x => x.Name);

        var expectedEmpty = await collection.Query()
            .Where(x => x.Id < 0)
            .Select(x => x.Name)
            .FirstOrDefault();

        var actualEmpty = await emptyQuery.FirstOrDefaultAsync();
        actualEmpty.Should().Be(expectedEmpty);
    }

    [Fact]
    public async Task Queryable_SingleAsync_And_SingleOrDefaultAsync_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var singleQuery = collection.AsQueryable()
            .Where(x => x.Id == 1)
            .Select(x => x.Name);

        var expectedSingle = await collection.Query()
            .Where(x => x.Id == 1)
            .Select(x => x.Name)
            .Single();

        var actualSingle = await singleQuery.SingleAsync();
        actualSingle.Should().Be(expectedSingle);

        var noneQuery = collection.AsQueryable()
            .Where(x => x.Id < 0)
            .Select(x => x.Name);

        var expectedNone = await collection.Query()
            .Where(x => x.Id < 0)
            .Select(x => x.Name)
            .SingleOrDefault();

        var actualNone = await noneQuery.SingleOrDefaultAsync();
        actualNone.Should().Be(expectedNone);
    }

    [Fact]
    public async Task Queryable_Any_Count_LongCount_Async_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable().Where(x => x.Age >= 30);

        var expectedAny = await collection.Query().Where(x => x.Age >= 30).Exists();
        var expectedCount = await collection.Query().Where(x => x.Age >= 30).Count();
        var expectedLongCount = await collection.Query().Where(x => x.Age >= 30).LongCount();

        var actualAny = await queryable.AnyAsync();
        var actualCount = await queryable.CountAsync();
        var actualLongCount = await queryable.LongCountAsync();

        actualAny.Should().Be(expectedAny);
        actualCount.Should().Be(expectedCount);
        actualLongCount.Should().Be(expectedLongCount);

        actualCount.Should().Be(local.Count(x => x.Age >= 30));
        actualLongCount.Should().Be(local.LongCount(x => x.Age >= 30));
    }

    [Fact]
    public async Task Queryable_Constant_Boolean_Predicates_Work_Across_Async_Terminals()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();
        var always = true;
        var never = false;

        var allQueryable = collection.AsQueryable().Where(x => always);
        var noneQueryable = collection.AsQueryable().Where(x => never);

        var all = await allQueryable.ToArrayAsync();
        var none = await noneQueryable.ToArrayAsync();
        var anyAll = await allQueryable.AnyAsync();
        var anyNone = await noneQueryable.AnyAsync();
        var countAll = await allQueryable.CountAsync();
        var countNone = await noneQueryable.CountAsync();
        var longCountAll = await allQueryable.LongCountAsync();
        var longCountNone = await noneQueryable.LongCountAsync();

        AssertEx.ArrayEqual(local, all, true);
        none.Should().BeEmpty();
        anyAll.Should().BeTrue();
        anyNone.Should().BeFalse();
        countAll.Should().Be(local.Length);
        countNone.Should().Be(0);
        longCountAll.Should().Be(local.LongLength);
        longCountNone.Should().Be(0);
    }

    [Fact]
    public async Task Queryable_GetPlanAsync_Parity_With_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        await collection.EnsureIndex(x => x.Age);

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 30)
            .OrderBy(x => x.Age)
            .ThenByDescending(x => x.Name)
            .Select(x => new { x.Age, x.Name })
            .Take(10);

        var providerPlan = await queryable.GetPlanAsync();
        var nativePlan = await collection.Query()
            .Where(x => x.Age >= 30)
            .OrderBy(x => x.Age)
            .ThenByDescending(x => x.Name)
            .Select(x => new { x.Age, x.Name })
            .Limit(10)
            .GetPlan();

        providerPlan["pipe"].Should().Be(nativePlan["pipe"]);
        providerPlan["select"]["expr"].AsString.Should().Be(nativePlan["select"]["expr"].AsString);
        providerPlan["orderBy"].AsArray.Count.Should().Be(nativePlan["orderBy"].AsArray.Count);
        providerPlan["limit"].AsInt32.Should().Be(nativePlan["limit"].AsInt32);
        providerPlan["index"]["expr"].Should().Be(nativePlan["index"]["expr"]);
    }

    [Fact]
    public async Task Queryable_GroupBy_Skip_Take_Parity_With_Native_Query_And_Local()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 18)
            .GroupBy(x => x.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() })
            .Skip(1)
            .Take(5);

        var lowered = queryable.ToQuery();

        var expectedLocal = local
            .Where(x => x.Age >= 18)
            .GroupBy(x => x.Age)
            .OrderBy(x => x.Key)
            .Select(x => new { Age = x.Key, Count = x.Count() })
            .Skip(1)
            .Take(5)
            .ToArray();

        var expectedNative = await collection.Query()
            .Where(x => x.Age >= 18)
            .GroupBy(lowered.GroupBy)
            .Select(lowered.Select)
            .Skip(1)
            .Limit(5)
            .ToArray();

        var actual = await queryable.ToArrayAsync();

        actual.Should().Equal(expectedLocal);
        actual.Select(x => x.Age).Should().Equal(expectedNative.Select(x => x["Age"].AsInt32));
        actual.Select(x => x.Count).Should().Equal(expectedNative.Select(x => x["Count"].AsInt32));
    }

    [Fact]
    public async Task Queryable_GroupBy_GetPlanAsync_Routes_Through_GroupBy_Pipe_And_Matches_Native_Query()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, _) = db.GetData();

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 18)
            .GroupBy(x => x.Age)
            .Where(g => g.Count() >= 2)
            .Select(g => new { Age = g.Key, Count = g.Count() })
            .Skip(1)
            .Take(3);

        var lowered = queryable.ToQuery();

        var providerPlan = await queryable.GetPlanAsync();
        var nativePlan = await collection.Query()
            .Where(x => x.Age >= 18)
            .GroupBy(lowered.GroupBy)
            .Having(lowered.Having)
            .Select(lowered.Select)
            .Skip(1)
            .Limit(3)
            .GetPlan();

        providerPlan["pipe"].Should().Be("groupByPipe");
        providerPlan["pipe"].Should().Be(nativePlan["pipe"]);
        providerPlan["groupBy"]["expr"].Should().Be(nativePlan["groupBy"]["expr"]);
        providerPlan["groupBy"]["having"].Should().Be(nativePlan["groupBy"]["having"]);
        providerPlan["groupBy"]["select"].Should().Be(nativePlan["groupBy"]["select"]);
        providerPlan["offset"].AsInt32.Should().Be(nativePlan["offset"].AsInt32);
        providerPlan["limit"].AsInt32.Should().Be(nativePlan["limit"].AsInt32);
    }

    [Fact]
    public async Task Queryable_Grouped_CountAsync_Fails_Clearly()
    {
        await using var db = await PersonGroupByData.CreateAsync();
        var (collection, _) = db.GetData();

        var queryable = collection.AsQueryable()
            .GroupBy(x => x.Age)
            .Select(g => new { Age = g.Key, Count = g.Count() });

        Func<Task> act = async () => _ = await queryable.CountAsync();

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Count*grouped LINQ queries*ToListAsync*ToArrayAsync*Query()*");
    }

    [Fact]
    public async Task Queryable_Sync_Execution_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var queryable = collection.AsQueryable().Where(x => x.Age >= 18);

        Action enumerate = () => _ = queryable.ToList();
        Action count = () => _ = queryable.Count();

        enumerate.Should().Throw<NotSupportedException>()
            .WithMessage("*async queryable terminals*collection.Query()*");

        count.Should().Throw<NotSupportedException>()
            .WithMessage("*async queryable terminals*collection.Query()*");
    }
}

