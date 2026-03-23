using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Contains_Tests
{
    [Fact]
    public async Task ArrayContains_ShouldHaveCount1()
    {
        var random = new Random();
        var randomValue = random.Next();

        await using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<ItemWithEnumerable>();
        await collection.Insert(new ItemWithEnumerable { Array = new[] { randomValue } });

        var result = await collection.Find(i => i.Array.Contains(randomValue)).ToListAsync();
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnumerableAssignedArrayContains_ShouldHaveCount1()
    {
        var random = new Random();
        var randomValue = random.Next();

        await using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<ItemWithEnumerable>();
        await collection.Insert(new ItemWithEnumerable { Enumerable = new[] { randomValue } });

        var result = await collection.Find(i => i.Enumerable.Contains(randomValue)).ToListAsync();
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task EnumerableAssignedListContains_ShouldHaveCount1()
    {
        var random = new Random();
        var randomValue = random.Next();

        await using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<ItemWithEnumerable>();
        await collection.Insert(new ItemWithEnumerable { Enumerable = new List<int> { randomValue } });

        var result = await collection.Find(i => i.Enumerable.Contains(randomValue)).ToListAsync();
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ListContains_ShouldHaveCount1()
    {
        var random = new Random();
        var randomValue = random.Next();

        await using var database = new LiteDatabase(new MemoryStream());
        var collection = database.GetCollection<ItemWithEnumerable>();
        await collection.Insert(new ItemWithEnumerable { List = new List<int> { randomValue } });

        var result = await collection.Find(i => i.List.Contains(randomValue)).ToListAsync();
        result.Should().HaveCount(1);
    }

    public class ItemWithEnumerable
    {
        public int[] Array { get; set; }
        public IEnumerable<int> Enumerable { get; set; }
        public IList<int> List { get; set; }
    }
}