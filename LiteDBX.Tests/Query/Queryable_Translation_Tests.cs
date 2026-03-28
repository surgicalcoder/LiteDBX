using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.QueryTest;

public class Queryable_Translation_Tests
{
    [Fact]
    public async Task Queryable_Where_Lowers_And_Executes_Like_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable().Where(x => x.Address.State == "FL");

        var state = queryable.ToQueryState();
        var lowered = queryable.ToQuery();
        var expectedLocal = local.Where(x => x.Address.State == "FL").ToArray();
        var expectedNative = await collection.Query().Where(x => x.Address.State == "FL").ToArray();
        var actual = await queryable.ToNativeQueryable().ToArray();

        state.Operators.Should().HaveCount(1);
        state.Operators[0].Kind.Should().Be(LiteDbXQueryMethodKind.Where);
        lowered.Where.Should().HaveCount(1);

        AssertEx.ArrayEqual(expectedLocal, actual, true);
        AssertEx.ArrayEqual(expectedNative, actual, true);
    }

    [Fact]
    public async Task Queryable_OrderBy_ThenBy_Select_Skip_Take_Lowers_And_Executes_Like_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Age);

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Age)
            .ThenByDescending(x => x.Name)
            .Select(x => new { x.Age, x.Name })
            .Skip(5)
            .Take(10);

        var lowered = queryable.ToQuery();

        var expectedNative = await collection.Query()
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Age)
            .ThenByDescending(x => x.Name)
            .Select(x => new { x.Age, x.Name })
            .Skip(5)
            .Limit(10)
            .ToArray();

        var expectedLocal = local
            .Where(x => x.Age >= 18)
            .OrderBy(x => x.Age)
            .ThenByDescending(x => x.Name)
            .Select(x => new { x.Age, x.Name })
            .Skip(5)
            .Take(10)
            .ToArray();

        var actual = await queryable.ToNativeQueryable().ToArray();

        lowered.Where.Should().HaveCount(1);
        lowered.OrderBy.Should().HaveCount(2);
        lowered.Offset.Should().Be(5);
        lowered.Limit.Should().Be(10);

        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_Select_Scalar_Lowers_And_Executes_Like_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        await collection.EnsureIndex(x => x.Address.City);

        var queryable = collection.AsQueryable()
            .OrderBy(x => x.Address.City)
            .Select(x => x.Address.City);

        var lowered = queryable.ToQuery();
        var expectedNative = await collection.Query()
            .OrderBy(x => x.Address.City)
            .Select(x => x.Address.City)
            .ToArray();

        var expectedLocal = local
            .OrderBy(x => x.Address.City)
            .Select(x => x.Address.City)
            .ToArray();

        var actual = await queryable.ToNativeQueryable().ToArray();

        lowered.OrderBy.Should().HaveCount(1);
        lowered.Select.Source.Should().NotBeNullOrWhiteSpace();

        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_Select_Anonymous_Lowers_And_Executes_Like_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var queryable = collection.AsQueryable()
            .Select(x => new { city = x.Address.City.ToUpper(), phone0 = x.Phones[0], address = new Address { Street = x.Name } });

        var expectedNative = await collection.Query()
            .Select(x => new { city = x.Address.City.ToUpper(), phone0 = x.Phones[0], address = new Address { Street = x.Name } })
            .ToArray();

        var expectedLocal = local
            .Select(x => new { city = x.Address.City.ToUpper(), phone0 = x.Phones[0], address = new Address { Street = x.Name } })
            .ToArray();

        var actual = await queryable.ToNativeQueryable().ToArray();

        actual.Should().BeEquivalentTo(expectedNative, options => options.WithStrictOrdering());
        actual.Should().BeEquivalentTo(expectedLocal, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Queryable_MultiWhere_With_ArrayContains_And_StringFilter_Lowers_And_Executes_Like_Native_Query()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();

        var ids = new[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        var queryable = collection.AsQueryable()
            .Where(x => x.Age >= 10 && x.Age <= 40)
            .Where(x => x.Name.StartsWith("Ge"))
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name });

        var lowered = queryable.ToQuery();

        var expectedNative = await collection.Query()
            .Where(x => x.Age >= 10 && x.Age <= 40)
            .Where(x => x.Name.StartsWith("Ge"))
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToArray();

        var expectedLocal = local
            .Where(x => x.Age >= 10 && x.Age <= 40)
            .Where(x => x.Name.StartsWith("Ge"))
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToArray();

        var actual = await queryable.ToNativeQueryable().ToArray();

        lowered.Where.Should().HaveCount(3);

        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_AsQueryable_With_Explicit_Transaction_Preserves_Transaction_And_Parity()
    {
        await using var db = new LiteDatabase(":memory:");
        var collection = db.GetCollection<Person>("person");
        var local = DataGen.Person().ToArray();

        await collection.Insert(local);

        await using var tx = await db.BeginTransaction();

        var queryable = collection.AsQueryable(tx)
            .Where(x => x.Id <= 5)
            .OrderBy(x => x.Name)
            .Select(x => x.Name);

        var state = queryable.ToQueryState();

        var expectedNative = await collection.Query(tx)
            .Where(x => x.Id <= 5)
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToArray();

        var expectedLocal = local
            .Where(x => x.Id <= 5)
            .OrderBy(x => x.Name)
            .Select(x => x.Name)
            .ToArray();

        var actual = await queryable.ToArrayAsync();

        state.Root.Transaction.Should().BeSameAs(tx);
        actual.Should().Equal(expectedNative);
        actual.Should().Equal(expectedLocal);
    }

    [Fact]
    public async Task Queryable_Constant_Boolean_Where_Lowers_And_Executes_Correctly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, local) = db.GetData();
        var always = true;
        var never = false;

        var allQueryable = collection.AsQueryable().Where(x => true);
        var capturedAllQueryable = collection.AsQueryable().Where(x => always);
        var noneQueryable = collection.AsQueryable().Where(x => false);
        var capturedNoneQueryable = collection.AsQueryable().Where(x => never);

        allQueryable.ToQuery().Where.Should().ContainSingle().Which.Source.Should().Be("(@p0=true)");
        capturedAllQueryable.ToQuery().Where.Should().ContainSingle().Which.Source.Should().Be("(@p0=true)");
        noneQueryable.ToQuery().Where.Should().ContainSingle().Which.Source.Should().Be("(@p0=true)");
        capturedNoneQueryable.ToQuery().Where.Should().ContainSingle().Which.Source.Should().Be("(@p0=true)");

        var all = await allQueryable.ToArrayAsync();
        var capturedAll = await capturedAllQueryable.ToArrayAsync();
        var none = await noneQueryable.ToArrayAsync();
        var capturedNone = await capturedNoneQueryable.ToArrayAsync();

        AssertEx.ArrayEqual(local, all, true);
        AssertEx.ArrayEqual(local, capturedAll, true);
        none.Should().BeEmpty();
        capturedNone.Should().BeEmpty();
    }

    [Fact]
    public async Task Queryable_Unsupported_Join_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var other = collection.AsQueryable();

        Action act = () => _ = collection.AsQueryable()
            .Join(other, x => x.Id, y => y.Id, (x, y) => new { x.Id, OtherId = y.Id });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Join*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_GroupJoin_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var other = collection.AsQueryable();

        Action act = () => _ = collection.AsQueryable()
            .GroupJoin(other, x => x.Id, y => y.Id, (x, group) => new { x.Id, Count = group.Count() });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*GroupJoin*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_SelectMany_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .SelectMany(x => x.Phones);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*SelectMany*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_Union_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .Union(collection.AsQueryable());

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Union*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_PostProjection_Where_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .Select(x => x.Name)
            .Where(x => x.StartsWith("G"));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Select*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_Multiple_Primary_OrderBy_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        var ordered = collection.AsQueryable().OrderBy(x => x.Name);
        Expression<Func<Person, int>> nextOrder = x => x.Age;

        Action act = () => _ = ((LiteDbXQueryProvider)ordered.Provider).CreateQuery<Person>(
            Expression.Call(
                typeof(Queryable),
                nameof(Queryable.OrderBy),
                new[] { typeof(Person), typeof(int) },
                ordered.Expression,
                Expression.Quote(nextOrder)));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*ThenBy*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_Skip_After_Take_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .Take(10)
            .Skip(5);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Skip*Take*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_Grouped_OrderBy_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .GroupBy(x => x.Age)
            .OrderBy(g => g.Key);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*GroupBy*OrderBy*Query()*");
    }

    [Fact]
    public async Task Queryable_Unsupported_GroupBy_ResultSelector_Overload_Fails_Clearly()
    {
        await using var db = await PersonQueryData.CreateAsync();
        var (collection, _) = db.GetData();

        Action act = () => _ = collection.AsQueryable()
            .GroupBy(x => x.Age, (age, group) => new { Age = age, Count = group.Count() });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Only Queryable.GroupBy(source, keySelector)*collection.Query()*");
    }
}


