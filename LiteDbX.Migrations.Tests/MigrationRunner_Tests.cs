using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Migrations;
using Xunit;

namespace LiteDbX.Migrations.Tests;

public class MigrationRunner_Tests
{
    [Fact]
    public async Task RunAsync_ShouldApplyInPlaceMutationsAcrossWildcardSelectedCollections()
    {
        await using var db = await LiteDatabase.Open(":memory:");

        var tenantOne = db.GetCollection("tenant_one");
        var tenantTwo = db.GetCollection("tenant_two");
        var other = db.GetCollection("other");

        await tenantOne.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Name"] = new BsonValue("  Alice  "),
            ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439011"),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        await tenantTwo.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(2),
            ["Name"] = new BsonValue(" Bob "),
            ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439012"),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        await other.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(3),
            ["Name"] = new BsonValue("  Charlie  "),
            ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439013"),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        var report = await db.Migrations()
            .Migration("tenant-cleanup", m => m.ForCollection("tenant_*", c =>
            {
                c.AddFieldWhen("Metadata.Source", _ => new BsonValue("migration"), BsonPredicates.Missing);
                c.ModifyFieldWhen("Name", ctx => new BsonValue(ctx.Value.AsString.Trim()), BsonPredicates.IsString);
                c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
                c.ConvertField("CustomerId").FromStringToObjectId();
            }))
            .RunAsync();

        var tenantOneDoc = await tenantOne.FindById(new BsonValue(1));
        var tenantTwoDoc = await tenantTwo.FindById(new BsonValue(2));
        var otherDoc = await other.FindById(new BsonValue(3));

        tenantOneDoc["Name"].AsString.Should().Be("Alice");
        tenantTwoDoc["Name"].AsString.Should().Be("Bob");
        tenantOneDoc.ContainsKey("Tags").Should().BeFalse();
        tenantTwoDoc.ContainsKey("Tags").Should().BeFalse();
        tenantOneDoc["Metadata"].AsDocument["Source"].AsString.Should().Be("migration");
        tenantTwoDoc["Metadata"].AsDocument["Source"].AsString.Should().Be("migration");
        tenantOneDoc["CustomerId"].IsObjectId.Should().BeTrue();
        tenantTwoDoc["CustomerId"].IsObjectId.Should().BeTrue();

        otherDoc["Name"].AsString.Should().Be("  Charlie  ");
        otherDoc.ContainsKey("Tags").Should().BeTrue();
        otherDoc["CustomerId"].IsString.Should().BeTrue();

        report.Migrations.Should().ContainSingle();
        report.Migrations[0].WasApplied.Should().BeTrue();
        report.Migrations[0].DocumentsModified.Should().Be(2);
        report.Migrations[0].Selectors.Should().ContainSingle();
        report.Migrations[0].Selectors[0].MatchedCollections.Should().BeEquivalentTo(new[] { "tenant_one", "tenant_two" });
    }

    [Fact]
    public async Task RunAsync_ShouldSkipAlreadyAppliedMigration()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Metadata"] = new BsonDocument()
        });

        var runner = db.Migrations()
            .Migration("only-once", m => m.ForCollection("tenant_*", c =>
                c.AddFieldWhen("Metadata.Source", _ => new BsonValue("migration"), BsonPredicates.Missing)));

        var first = await runner.RunAsync();
        var second = await runner.RunAsync();

        first.Migrations[0].WasApplied.Should().BeTrue();
        second.Migrations[0].WasSkipped.Should().BeTrue();

        var doc = await collection.FindById(new BsonValue(1));
        doc["Metadata"].AsDocument["Source"].AsString.Should().Be("migration");
    }

    [Fact]
    public async Task RunAsync_ShouldExcludeMigrationInfrastructureCollectionsFromWildcardSelection()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var users = db.GetCollection("users");

        await users.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Metadata"] = new BsonDocument()
        });

        await db.Migrations()
            .Migration("seed-history", m => m.ForCollection("users", c =>
                c.AddFieldWhen("Metadata.Source", _ => new BsonValue("seed"), BsonPredicates.Missing)))
            .RunAsync();

        var report = await db.Migrations()
            .Migration("wildcard-pass", m => m.ForCollection("*", c =>
                c.AddFieldWhen("Touched", _ => new BsonValue(true), BsonPredicates.Missing)))
            .RunAsync();

        var userDoc = await users.FindById(new BsonValue(1));
        var journal = db.GetCollection("__migrations");
        var historyDoc = await journal.FindById(new BsonValue("seed-history"));

        userDoc["Touched"].AsBoolean.Should().BeTrue();
        historyDoc.ContainsKey("Touched").Should().BeFalse();
        report.Migrations[0].Selectors[0].MatchedCollections.Should().NotContain("__migrations");
    }

    [Fact]
    public async Task ConvertField_WhenInvalidAndGenerateNewId_ShouldReplaceValue()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["LegacyId"] = new BsonValue("not-an-object-id")
        });

        await db.Migrations()
            .Migration("convert-invalid", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("LegacyId").FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["LegacyId"].IsObjectId.Should().BeTrue();
        doc["LegacyId"].AsObjectId.Should().NotBe(ObjectId.Empty);
    }
}

