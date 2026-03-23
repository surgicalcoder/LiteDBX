using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Index_Tests
{
    [Fact]
    public async Task Index_With_No_Name()
    {
        await using var db = new LiteDatabase("filename=:memory:");
        var users   = db.GetCollection("users");
        var indexes = db.GetCollection("$indexes");

        await users.Insert(new BsonDocument { ["name"] = new BsonDocument { ["first"] = "John",  ["last"] = "Doe"   } });
        await users.Insert(new BsonDocument { ["name"] = new BsonDocument { ["first"] = "Marco", ["last"] = "Pollo" } });

        await users.EnsureIndex("name.last");
        await users.EnsureIndex("$.name.first", true);

        // Invoking FindOne just checks the result is non-null via the async call
        Assert.NotNull(await indexes.FindOne("collection = 'users' AND name = 'namelast'"));
        Assert.NotNull(await indexes.FindOne("collection = 'users' AND name = 'namefirst'"));
    }

    [Fact]
    public async Task Index_Order()
    {
        await using var db = new LiteDatabase("filename=:memory:");
        var col     = db.GetCollection("col");
        var indexes = db.GetCollection("$indexes");

        await col.Insert(new BsonDocument { { "text", "D" } });
        await col.Insert(new BsonDocument { { "text", "A" } });
        await col.Insert(new BsonDocument { { "text", "E" } });
        await col.Insert(new BsonDocument { { "text", "C" } });
        await col.Insert(new BsonDocument { { "text", "B" } });

        await col.EnsureIndex("text");

        var ascDocs = await col.Query().OrderBy("text").Select("text").ToDocuments().ToListAsync();
        var asc  = string.Join("", ascDocs.Select(x => x["text"].AsString));

        var descDocs = await col.Query().OrderByDescending("text").Select("text").ToDocuments().ToListAsync();
        var desc = string.Join("", descDocs.Select(x => x["text"].AsString));

        asc.Should().Be("ABCDE");
        desc.Should().Be("EDCBA");

        (await indexes.Count("name = 'text'")).Should().Be(1);
    }

    [Fact]
    public async Task Index_With_Like()
    {
        await using var db = new LiteDatabase("filename=:memory:");
        var col = db.GetCollection("names", BsonAutoId.Int32);

        await col.Insert(new[]
        {
            new BsonDocument { ["name"] = "marcelo" },
            new BsonDocument { ["name"] = "mauricio" },
            new BsonDocument { ["name"] = "Mauricio" },
            new BsonDocument { ["name"] = "MAUricio" },
            new BsonDocument { ["name"] = "MAURICIO" },
            new BsonDocument { ["name"] = "mauRO" },
            new BsonDocument { ["name"] = "ANA" }
        });

        await col.EnsureIndex("idx_name", "name");

        var all = await (await db.Execute("SELECT name FROM names")).ToArray();
        var r0  = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'Mau%'")).ToArray();
        var r1  = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'MAU%'")).ToArray();
        var r2  = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'mau%'")).ToArray();

        r0.Length.Should().Be(5);
        r1.Length.Should().Be(5);
        r2.Length.Should().Be(5);

        var r3 = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'ma%ci%'")).ToArray();
        var r4 = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'maUriCIO'")).ToArray();
        r3.Length.Should().Be(4);
        r4.Length.Should().Be(4);

        var r5 = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'marc_o'")).ToArray();
        r5.Length.Should().Be(0);

        var r6 = await (await db.Execute("SELECT name FROM names WHERE name LIKE 'marc__o'")).ToArray();
        r6.Length.Should().Be(1);
    }

    [Fact]
    public async Task EnsureIndex_Invalid_Arguments()
    {
        await using var db   = new LiteDatabase("filename=:memory:");
        var test = db.GetCollection("test");

        // null name — EnsureIndex(string name, BsonExpression) validates eagerly
        var exn1 = await Assert.ThrowsAsync<ArgumentNullException>(
            () => test.EnsureIndex(null, "x").AsTask());
        Assert.Equal("name", exn1.ParamName);

        // null expression overload 1
        var exn2 = await Assert.ThrowsAsync<ArgumentNullException>(
            () => test.EnsureIndex((BsonExpression)null).AsTask());
        Assert.Equal("expression", exn2.ParamName);

        // null expression overload 2
        var exn3 = await Assert.ThrowsAsync<ArgumentNullException>(
            () => test.EnsureIndex("x", (BsonExpression)null).AsTask());
        Assert.Equal("expression", exn3.ParamName);
    }

    [Fact]
    public async Task MultiKey_Index_Test()
    {
        await using var db  = new LiteDatabase("filename=:memory:");
        var col = db.GetCollection("customers", BsonAutoId.Int32);
        await col.EnsureIndex("$.Phones[*].Type");

        var doc1 = new BsonDocument
        {
            ["Name"] = "John Doe",
            ["Phones"] = new BsonArray(
                new BsonDocument { ["Type"] = "Mobile", ["Number"] = "9876-5432" },
                new BsonDocument { ["Type"] = "Fixed",  ["Number"] = "3333-3333" })
        };
        var doc2 = new BsonDocument
        {
            ["Name"] = "Jane Doe",
            ["Phones"] = new BsonArray(
                new BsonDocument { ["Type"] = "Fixed", ["Number"] = "3000-0000" })
        };

        await col.Insert(doc1);
        await col.Insert(doc2);

        var query1 = "select $ from customers where $.Phones[*].Type any = 'Mobile'";
        var query2 = "select $ from customers where $.Phones[*].Type any = 'Fixed'";

        var explain1 = await (await db.Execute("explain " + query1)).First();
        Assert.True(!explain1["index"]["mode"].AsString.Contains("_id"));

        var explain2 = await (await db.Execute("explain " + query2)).First();
        Assert.True(!explain2["index"]["mode"].AsString.Contains("_id"));

        var result1 = await (await db.Execute(query1)).ToArray();
        Assert.True(result1.Length == 1);

        var result2 = await (await db.Execute(query2)).ToArray();
        Assert.True(result2.Length == 2);
    }
}