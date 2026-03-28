using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class AutoId_Tests
{
    [Fact]
    public async Task AutoId_Strong_Typed()
    {
        var mapper = new BsonMapper();

        await using var db = new LiteDatabase(new MemoryStream(), mapper, new MemoryStream());

        var cs_int  = db.GetCollection<EntityInt>("int");
        var cs_long = db.GetCollection<EntityLong>("long");
        var cs_guid = db.GetCollection<EntityGuid>("guid");
        var cs_oid  = db.GetCollection<EntityOid>("oid");
        var cs_str  = db.GetCollection<EntityString>("str");

        var cint_1 = new EntityInt { Name = "R1" }; var cint_2 = new EntityInt { Name = "R2" };
        var cint_3 = new EntityInt { Name = "R3" }; var cint_4 = new EntityInt { Name = "R4" };
        var clong_1 = new EntityLong { Name = "R1" }; var clong_2 = new EntityLong { Name = "R2" };
        var clong_3 = new EntityLong { Name = "R3" }; var clong_4 = new EntityLong { Name = "R4" };
        var cguid_1 = new EntityGuid { Name = "R1" }; var cguid_2 = new EntityGuid { Name = "R2" };
        var cguid_3 = new EntityGuid { Name = "R3" }; var cguid_4 = new EntityGuid { Name = "R4" };
        var coid_1 = new EntityOid { Name = "R1" }; var coid_2 = new EntityOid { Name = "R2" };
        var coid_3 = new EntityOid { Name = "R3" }; var coid_4 = new EntityOid { Name = "R4" };
        var cstr_1 = new EntityString { Id = "a", Name = "R1" }; var cstr_2 = new EntityString { Id = "b", Name = "R2" };
        var cstr_3 = new EntityString { Id = "c", Name = "R3" }; var cstr_4 = new EntityString { Id = "d", Name = "R4" };

        // insert first 3
        await cs_int.Insert(new[] { cint_1, cint_2, cint_3 });
        await cs_long.Insert(new[] { clong_1, clong_2, clong_3 });
        await cs_guid.Insert(new[] { cguid_1, cguid_2, cguid_3 });
        await cs_oid.Insert(new[] { coid_1, coid_2, coid_3 });
        await cs_str.Insert(new[] { cstr_1, cstr_2, cstr_3 });

        cint_2.Name = "Changed 2"; clong_2.Name = "Changed 2"; cguid_2.Name = "Changed 2";
        coid_2.Name = "Changed 2"; cstr_2.Name = "Changed 2";

        (await cs_int.Update(cint_2)).Should().BeTrue();
        (await cs_long.Update(clong_2)).Should().BeTrue();
        (await cs_guid.Update(cguid_2)).Should().BeTrue();
        (await cs_oid.Update(coid_2)).Should().BeTrue();
        (await cs_str.Update(cstr_2)).Should().BeTrue();

        cint_3.Name = "Changed 3"; clong_3.Name = "Changed 3"; cguid_3.Name = "Changed 3";
        coid_3.Name = "Changed 3"; cstr_3.Name = "Changed 3";

        (await cs_int.Upsert(cint_3)).Should().BeFalse();
        (await cs_long.Upsert(clong_3)).Should().BeFalse();
        (await cs_guid.Upsert(cguid_3)).Should().BeFalse();
        (await cs_oid.Upsert(coid_3)).Should().BeFalse();
        (await cs_str.Upsert(cstr_3)).Should().BeFalse();

        (await cs_int.FindOne(x => x.Id == cint_3.Id)).Name.Should().Be(cint_3.Name);
        (await cs_long.FindOne(x => x.Id == clong_3.Id)).Name.Should().Be(clong_3.Name);
        (await cs_guid.FindOne(x => x.Id == cguid_3.Id)).Name.Should().Be(cguid_3.Name);
        (await cs_oid.FindOne(x => x.Id == coid_3.Id)).Name.Should().Be(coid_3.Name);
        (await cs_str.FindOne(x => x.Id == cstr_3.Id)).Name.Should().Be(cstr_3.Name);

        (await cs_int.Upsert(cint_4)).Should().BeTrue();
        (await cs_long.Upsert(clong_4)).Should().BeTrue();
        (await cs_guid.Upsert(cguid_4)).Should().BeTrue();
        (await cs_oid.Upsert(coid_4)).Should().BeTrue();
        (await cs_str.Upsert(cstr_4)).Should().BeTrue();

        (await cs_int.FindOne(x => x.Id == cint_4.Id)).Name.Should().Be(cint_4.Name);
        (await cs_long.FindOne(x => x.Id == clong_4.Id)).Name.Should().Be(clong_4.Name);
        (await cs_guid.FindOne(x => x.Id == cguid_4.Id)).Name.Should().Be(cguid_4.Name);
        (await cs_oid.FindOne(x => x.Id == coid_4.Id)).Name.Should().Be(coid_4.Name);
        (await cs_str.FindOne(x => x.Id == cstr_4.Id)).Name.Should().Be(cstr_4.Name);

        (await cs_int.Count(Query.All())).Should().Be(4);
        (await cs_long.Count(Query.All())).Should().Be(4);
        (await cs_guid.Count(Query.All())).Should().Be(4);
        (await cs_oid.Count(Query.All())).Should().Be(4);
        (await cs_str.Count(Query.All())).Should().Be(4);

        var cint_10 = new EntityInt { Id = 10, Name = "R10" };
        var cint_11 = new EntityInt { Name = "R11" };
        var cint_7  = new EntityInt { Id = 7,  Name = "R7"  };
        var cint_12 = new EntityInt { Name = "R12" };

        await cs_int.Insert(cint_10);
        await cs_int.Insert(cint_11);
        await cs_int.Insert(cint_7);
        await cs_int.Insert(cint_12);

        cint_10.Id.Should().Be(10);
        cint_11.Id.Should().Be(11);
        cint_7.Id.Should().Be(7);
        cint_12.Id.Should().Be(12);
    }

    [Fact]
    public async Task AutoId_BsonDocument()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection("Writers");
        await col.Insert(new BsonDocument { ["Name"] = "Mark Twain" });
        await col.Insert(new BsonDocument { ["Name"] = "Jack London", ["_id"] = 1 });

        await col.EnsureIndex("LowerName", "LOWER($.Name)");

        var mark = await col.FindOne(Query.EQ("LOWER($.Name)", "mark twain"));
        var jack = await col.FindOne(Query.EQ("LOWER($.Name)", "jack london"));

        mark["_id"].IsObjectId.Should().BeTrue();
        jack["_id"].IsInt32.Should().BeTrue();
    }

    [Fact]
    public async Task AutoId_String_Property_Generates_ObjectId_And_Writes_Back_Hex_String()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<EntityString>("string_autoid");
        var entity = new EntityString { Id = null, Name = "John" };

        var bsonId = await col.Insert(entity);
        var loaded = await col.FindOne(x => x.Name == "John");

        bsonId.IsObjectId.Should().BeTrue();
        entity.Id.Should().Be(bsonId.AsObjectId.ToString());
        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(entity.Id);
        loaded.Name.Should().Be("John");
    }

    [Fact]
    public async Task AutoId_No_Duplicate_After_Delete()
    {
        await using (var db = new LiteDatabase(new MemoryStream()))
        {
            var col = db.GetCollection<EntityInt>("col1");
            var one = new EntityInt { Name = "One" }; var two = new EntityInt { Name = "Two" };
            var three = new EntityInt { Name = "Three" }; var four = new EntityInt { Name = "Four" };

            await col.Insert(one); await col.Insert(two);
            one.Id.Should().Be(1); two.Id.Should().Be(2);

            await col.Delete(one.Id); await col.Delete(two.Id);
            await col.Insert(new[] { three, four });
            three.Id.Should().Be(3); four.Id.Should().Be(4);
        }

        await using (var db = new LiteDatabase(new MemoryStream()))
        {
            var one = new BsonDocument { ["Name"] = "One" }; var two = new BsonDocument { ["Name"] = "Two" };
            var three = new BsonDocument { ["Name"] = "Three" }; var four = new BsonDocument { ["Name"] = "Four" };
            var col = db.GetCollection("col", BsonAutoId.Int32);

            await col.Insert(one); await col.Insert(two);
            one["_id"].AsInt32.Should().Be(1); two["_id"].AsInt32.Should().Be(2);

            await col.Delete(one["_id"].AsInt32); await col.Delete(two["_id"].AsInt32);
            await col.Insert(new[] { three, four });
            three["_id"].AsInt32.Should().Be(3); four["_id"].AsInt32.Should().Be(4);
        }
    }

    [Fact]
    public async Task AutoId_Zero_Int()
    {
        await using var db = new LiteDatabase(":memory:");
        var test = db.GetCollection("Test", BsonAutoId.Int32);
        var doc = new BsonDocument { ["_id"] = 0, ["p1"] = 1 };
        await test.Insert(doc);
    }

    [Fact]
    public async Task AutoId_property()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col1 = db.GetCollection("Col1");
        col1.AutoId.Should().Be(BsonAutoId.ObjectId);

        var col2 = db.GetCollection("Col2", BsonAutoId.Int32);
        col2.AutoId.Should().Be(BsonAutoId.Int32);
    }

    #region Model

    public class EntityInt    { public int    Id { get; set; } public string Name { get; set; } }
    public class EntityLong   { public long   Id { get; set; } public string Name { get; set; } }
    public class EntityGuid   { public Guid   Id { get; set; } public string Name { get; set; } }
    public class EntityOid    { public ObjectId Id { get; set; } public string Name { get; set; } }
    public class EntityString { public string Id { get; set; } public string Name { get; set; } }

    #endregion
}