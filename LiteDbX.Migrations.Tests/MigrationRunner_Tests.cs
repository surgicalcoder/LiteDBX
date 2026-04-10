using System;
using System.Collections.Generic;
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

    [Fact]
    public async Task ConvertField_ShouldSupportIndexedArrayDocumentPath()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439011")
                },
                new BsonDocument
                {
                    ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439012")
                }
            }
        });

        var report = await db.Migrations()
            .Migration("convert-indexed-customer", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("Orders[0].CustomerId").FromStringToObjectId()))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        orders[0].AsDocument["CustomerId"].IsObjectId.Should().BeTrue();
        orders[1].AsDocument["CustomerId"].IsString.Should().BeTrue();
        report.Migrations[0].DocumentsModified.Should().Be(1);
    }

    [Fact]
    public async Task ConvertField_ShouldSupportIndexedArrayValuePath()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["LegacyIds"] = new BsonArray
            {
                new BsonValue("507f1f77bcf86cd799439011"),
                new BsonValue("leave-me-alone")
            }
        });

        await db.Migrations()
            .Migration("convert-indexed-value", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("LegacyIds[0]").FromStringToObjectId()))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var legacyIds = doc["LegacyIds"].AsArray;

        legacyIds[0].IsObjectId.Should().BeTrue();
        legacyIds[1].AsString.Should().Be("leave-me-alone");
    }

    [Fact]
    public async Task ConvertField_ShouldSupportWildcardArrayDocumentPath()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument { ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439011") },
                new BsonDocument { ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439012") },
                new BsonDocument { ["Other"] = new BsonValue("skip") },
                new BsonValue("not-a-document")
            }
        });

        var report = await db.Migrations()
            .Migration("convert-wildcard-customer", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("Orders[*].CustomerId").FromStringToObjectId()))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        orders[0].AsDocument["CustomerId"].IsObjectId.Should().BeTrue();
        orders[1].AsDocument["CustomerId"].IsObjectId.Should().BeTrue();
        orders[2].AsDocument.ContainsKey("CustomerId").Should().BeFalse();
        orders[3].IsString.Should().BeTrue();
        report.Migrations[0].DocumentsModified.Should().Be(1);
    }

    [Fact]
    public async Task ConvertField_ShouldSupportRecursivePathAcrossDocumentsAndArrays()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["LegacyId"] = new BsonValue("507f1f77bcf86cd799439011"),
            ["Profile"] = new BsonDocument
            {
                ["LegacyId"] = new BsonValue("507f1f77bcf86cd799439012")
            },
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["LegacyId"] = new BsonValue("507f1f77bcf86cd799439013")
                },
                new BsonDocument
                {
                    ["Nested"] = new BsonDocument
                    {
                        ["LegacyId"] = new BsonValue("507f1f77bcf86cd799439014")
                    }
                },
                new BsonValue("skip")
            }
        });

        var report = await db.Migrations()
            .Migration("convert-recursive-legacy", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("**.LegacyId").FromStringToObjectId()))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["LegacyId"].IsObjectId.Should().BeTrue();
        doc["Profile"].AsDocument["LegacyId"].IsObjectId.Should().BeTrue();
        doc["Orders"].AsArray[0].AsDocument["LegacyId"].IsObjectId.Should().BeTrue();
        doc["Orders"].AsArray[1].AsDocument["Nested"].AsDocument["LegacyId"].IsObjectId.Should().BeTrue();
        doc["Orders"].AsArray[2].AsString.Should().Be("skip");
        report.Migrations[0].DocumentsModified.Should().Be(1);
    }

    [Fact]
    public async Task SetFieldWhen_ShouldOverwriteExistingNestedValue_WhenParentExists()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Metadata"] = new BsonDocument
            {
                ["Source"] = new BsonValue("old")
            }
        });

        await db.Migrations()
            .Migration("set-source", m => m.ForCollection("tenant_*", c =>
                c.SetFieldWhen("Metadata.Source", _ => new BsonValue("new"), BsonPredicates.Always)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["Metadata"].AsDocument["Source"].AsString.Should().Be("new");
    }

    [Fact]
    public async Task SetFieldWhen_ShouldNotCreateMissingParents_InV1()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1)
        });

        await db.Migrations()
            .Migration("set-missing-parent", m => m.ForCollection("tenant_*", c =>
                c.SetFieldWhen("Metadata.Source", _ => new BsonValue("new"), BsonPredicates.Always)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc.ContainsKey("Metadata").Should().BeFalse();
    }

    [Fact]
    public async Task IndexedPaths_ShouldBeSafeNoOp_ForMixedShapesAndOutOfRangeIndexes()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonDocument
            {
                ["CustomerId"] = new BsonValue("507f1f77bcf86cd799439011")
            }
        });

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(2),
            ["Orders"] = new BsonArray()
        });

        var report = await db.Migrations()
            .Migration("indexed-noop", m => m.ForCollection("tenant_*", c =>
            {
                c.ConvertField("Orders[0].CustomerId").FromStringToObjectId();
                c.SetFieldWhen("Orders[1].Touched", new BsonValue(true), BsonPredicates.Always);
            }))
            .RunAsync();

        var first = await collection.FindById(new BsonValue(1));
        var second = await collection.FindById(new BsonValue(2));

        first["Orders"].IsDocument.Should().BeTrue();
        first["Orders"].AsDocument["CustomerId"].IsString.Should().BeTrue();
        second["Orders"].AsArray.Count.Should().Be(0);
        report.Migrations[0].DocumentsModified.Should().Be(0);
    }

    [Fact]
    public async Task SetDefaultWhenMissing_ShouldSupportWildcardArrayDocumentPath()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument(),
                new BsonDocument { ["Status"] = new BsonValue("existing") },
                new BsonValue("skip")
            }
        });

        await db.Migrations()
            .Migration("default-wildcard-status", m => m.ForCollection("tenant_*", c =>
                c.SetDefaultWhenMissing("Orders[*].Status", new BsonValue("new"))))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        orders[0].AsDocument["Status"].AsString.Should().Be("new");
        orders[1].AsDocument["Status"].AsString.Should().Be("existing");
        orders[2].AsString.Should().Be("skip");
    }

    [Fact]
    public async Task SetFieldWhen_ShouldSupportWildcardArrayDocumentPath()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument { ["Touched"] = new BsonValue(false) },
                new BsonDocument(),
                new BsonValue("skip")
            }
        });

        await db.Migrations()
            .Migration("set-wildcard-touched", m => m.ForCollection("tenant_*", c =>
                c.SetFieldWhen("Orders[*].Touched", new BsonValue(true), BsonPredicates.Always)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        orders[0].AsDocument["Touched"].AsBoolean.Should().BeTrue();
        orders[1].AsDocument["Touched"].AsBoolean.Should().BeTrue();
        orders[2].AsString.Should().Be("skip");
    }

    [Fact]
    public async Task RecursiveConvertField_ShouldBeSafeNoOp_ForMixedShapesWithoutMatches()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument { ["Name"] = new BsonValue("Alice") },
                new BsonArray { new BsonValue(123) },
                new BsonValue("skip")
            }
        });

        var report = await db.Migrations()
            .Migration("recursive-noop", m => m.ForCollection("tenant_*", c =>
                c.ConvertField("**.LegacyId").FromStringToObjectId()))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["Orders"].AsArray[0].AsDocument.ContainsKey("LegacyId").Should().BeFalse();
        report.Migrations[0].DocumentsModified.Should().Be(0);
    }

    [Fact]
    public async Task CopyField_ShouldDeepCloneDocument_AndPreserveSource()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Profile"] = new BsonDocument
            {
                ["Settings"] = new BsonDocument
                {
                    ["Theme"] = new BsonValue("dark")
                }
            }
        });

        await db.Migrations()
            .Migration("copy-profile", m => m.ForCollection("tenant_*", c =>
                c.CopyField("Profile.Settings", "Profile.SettingsCopy")))
            .RunAsync();

        await db.Migrations()
            .Migration("mutate-copy", m => m.ForCollection("tenant_*", c =>
                c.SetFieldWhen("Profile.SettingsCopy.Theme", _ => new BsonValue("light"), BsonPredicates.Always)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["Profile"].AsDocument["Settings"].AsDocument["Theme"].AsString.Should().Be("dark");
        doc["Profile"].AsDocument["SettingsCopy"].AsDocument["Theme"].AsString.Should().Be("light");
    }

    [Fact]
    public async Task RenameField_ShouldMoveValue_AndRemoveSource()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Legacy"] = new BsonDocument
            {
                ["OwnerId"] = new BsonValue("abc")
            },
            ["Owner"] = new BsonDocument()
        });

        await db.Migrations()
            .Migration("rename-owner", m => m.ForCollection("tenant_*", c =>
                c.RenameField("Legacy.OwnerId", "Owner.Id")))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["Owner"].AsDocument["Id"].AsString.Should().Be("abc");
        doc["Legacy"].AsDocument.ContainsKey("OwnerId").Should().BeFalse();
    }

    [Fact]
    public async Task MoveField_ShouldNotOverwriteExistingTarget()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["LegacyId"] = new BsonValue("legacy"),
            ["CurrentId"] = new BsonValue("current")
        });

        await db.Migrations()
            .Migration("move-id", m => m.ForCollection("tenant_*", c =>
                c.MoveField("LegacyId", "CurrentId")))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc["LegacyId"].AsString.Should().Be("legacy");
        doc["CurrentId"].AsString.Should().Be("current");
    }

    [Fact]
    public void RenameField_ShouldRejectOverlappingPaths()
    {
        var action = () => new CollectionMigrationBuilder().RenameField("Profile", "Profile.Settings");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SetDefaultWhenMissing_ShouldAddValueOnlyWhenMissing()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Metadata"] = new BsonDocument()
        });

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(2),
            ["Metadata"] = new BsonDocument
            {
                ["Source"] = new BsonValue("existing")
            }
        });

        await db.Migrations()
            .Migration("set-default", m => m.ForCollection("tenant_*", c =>
                c.SetDefaultWhenMissing("Metadata.Source", new BsonValue("default"))))
            .RunAsync();

        var first = await collection.FindById(new BsonValue(1));
        var second = await collection.FindById(new BsonValue(2));

        first["Metadata"].AsDocument["Source"].AsString.Should().Be("default");
        second["Metadata"].AsDocument["Source"].AsString.Should().Be("existing");
    }

    [Fact]
    public async Task ConvertId_ShouldRebuildCollection_PreserveIndexes_AndApplyDocumentOperations()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.EnsureIndex("idx_code", BsonExpression.Create("Code"), cancellationToken: default);

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("507f1f77bcf86cd799439011"),
            ["Code"] = new BsonValue("A"),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("507f1f77bcf86cd799439012"),
            ["Code"] = new BsonValue("B"),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        await db.Migrations()
            .Migration("convert-id", m => m.ForCollection("tenant_*", c =>
            {
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId);
                c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
                c.SetDefaultWhenMissing("Metadata.Source", new BsonValue("migration"));
            }))
            .RunAsync();

        var docs = await GetAllDocumentsAsync(collection);
        var collectionNames = await GetCollectionNamesAsync(db);
        var indexes = await GetAllDocumentsAsync(db.GetCollection("$indexes").Query().Where("collection = @0", new BsonValue("tenant_one")));

        docs.Should().HaveCount(2);
        docs.Should().OnlyContain(x => x["_id"].IsObjectId);
        docs.Should().OnlyContain(x => x.ContainsKey("Tags") == false);
        docs.Should().OnlyContain(x => x["Metadata"].AsDocument["Source"].AsString == "migration");
        indexes.Should().Contain(x => x["name"].AsString == "idx_code");
        collectionNames.Should().Contain(name => name.StartsWith("tenant_one__backup__", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ConvertId_WhenInvalidAndGenerateNewId_ShouldWriteDurableRemapEntry()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-id-value"),
            ["Code"] = new BsonValue("A")
        });

        await db.Migrations()
            .Migration("convert-invalid-id", m => m.ForCollection("tenant_*", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var docs = await GetAllDocumentsAsync(collection);
        var remaps = await GetAllDocumentsAsync(db.GetCollection("__migration_id_mappings"));
        var history = await db.GetCollection("__migrations").FindById(new BsonValue("convert-invalid-id"));

        docs.Should().ContainSingle();
        docs[0]["_id"].IsObjectId.Should().BeTrue();
        remaps.Should().ContainSingle();
        remaps[0]["migrationName"].AsString.Should().Be("convert-invalid-id");
        remaps[0]["oldIdRaw"].AsString.Should().Be("bad-id-value");
        remaps[0]["newObjectId"].IsObjectId.Should().BeTrue();
        history["generatedIdMappings"].AsInt32.Should().Be(1);
    }

    [Fact]
    public async Task RepairReference_ShouldUseDurableRemapLog_InLaterMigrationRun()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var orders = db.GetCollection("orders");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        await orders.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["CustomerId"] = new BsonValue("bad-customer-id")
        });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var report = await db.Migrations()
            .Migration("repair-orders", m => m.ForCollection("orders", c =>
                c.RepairReference("CustomerId").FromCollection("customers").Apply()))
            .RunAsync();

        var order = await orders.FindById(new BsonValue(1));

        order["CustomerId"].IsObjectId.Should().BeTrue();
        report.Migrations[0].RepairedReferences.Should().Be(1);
        report.Migrations[0].Selectors[0].Collections[0].RepairedReferences.Should().Be(1);
    }

    [Fact]
    public async Task RepairReference_ShouldHonorDbRefCollectionGuard()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var invoices = db.GetCollection("invoices");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        await invoices.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Customer"] = new BsonDocument
            {
                ["$id"] = new BsonValue("bad-customer-id"),
                ["$ref"] = new BsonValue("customers")
            }
        });

        await invoices.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(2),
            ["Customer"] = new BsonDocument
            {
                ["$id"] = new BsonValue("bad-customer-id"),
                ["$ref"] = new BsonValue("suppliers")
            }
        });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        await db.Migrations()
            .Migration("repair-dbrefs", m => m.ForCollection("invoices", c =>
                c.RepairReference("Customer.$id").FromCollection("customers").WhenReferenceCollectionIs("Customer.$ref").Apply()))
            .RunAsync();

        var repaired = await invoices.FindById(new BsonValue(1));
        var untouched = await invoices.FindById(new BsonValue(2));

        repaired["Customer"].AsDocument["$id"].IsObjectId.Should().BeTrue();
        untouched["Customer"].AsDocument["$id"].IsString.Should().BeTrue();
    }

    [Fact]
    public async Task RepairReference_ShouldSupportPairedWildcardDbRefPaths()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var invoices = db.GetCollection("invoices");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        await invoices.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Customer"] = new BsonDocument
                    {
                        ["$id"] = new BsonValue("bad-customer-id"),
                        ["$ref"] = new BsonValue("customers")
                    }
                },
                new BsonDocument
                {
                    ["Customer"] = new BsonDocument
                    {
                        ["$id"] = new BsonValue("bad-customer-id"),
                        ["$ref"] = new BsonValue("suppliers")
                    }
                },
                new BsonValue("skip")
            }
        });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var report = await db.Migrations()
            .Migration("repair-dbrefs-array", m => m.ForCollection("invoices", c =>
                c.RepairReference("Orders[*].Customer.$id")
                    .FromCollection("customers")
                    .WhenReferenceCollectionIs("Orders[*].Customer.$ref")
                    .Apply()))
            .RunAsync();

        var invoice = await invoices.FindById(new BsonValue(1));
        var orders = invoice["Orders"].AsArray;

        orders[0].AsDocument["Customer"].AsDocument["$id"].IsObjectId.Should().BeTrue();
        orders[1].AsDocument["Customer"].AsDocument["$id"].IsString.Should().BeTrue();
        orders[2].AsString.Should().Be("skip");
        report.Migrations[0].RepairedReferences.Should().Be(1);
        report.Migrations[0].Selectors[0].Collections[0].RepairedReferences.Should().Be(1);
    }

    [Fact]
    public async Task RepairReference_WildcardPair_ShouldSkipMissingSiblingGuardPaths()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var invoices = db.GetCollection("invoices");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        await invoices.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Customer"] = new BsonDocument
                    {
                        ["$id"] = new BsonValue("bad-customer-id")
                    }
                }
            }
        });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var report = await db.Migrations()
            .Migration("repair-dbrefs-array", m => m.ForCollection("invoices", c =>
                c.RepairReference("Orders[*].Customer.$id")
                    .FromCollection("customers")
                    .WhenReferenceCollectionIs("Orders[*].Customer.$ref")
                    .Apply()))
            .RunAsync();

        var invoice = await invoices.FindById(new BsonValue(1));

        invoice["Orders"].AsArray[0].AsDocument["Customer"].AsDocument["$id"].IsString.Should().BeTrue();
        report.Migrations[0].RepairedReferences.Should().Be(0);
    }

    [Fact]
    public async Task ConvertId_ReportShouldExposeBackupCollectionName_AndGeneratedMappingsPerCollection()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id")
        });

        var report = await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var collectionReport = report.Migrations[0].Selectors[0].Collections[0];

        collectionReport.GeneratedIdMappings.Should().Be(1);
        collectionReport.BackupCollectionName.Should().StartWith("customers__backup__");
    }

    [Fact]
    public async Task RemoveDocumentWhen_ShouldDeleteMatchingDocuments_InPlace()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["IsDeleted"] = new BsonValue(true),
            ["Touched"] = new BsonValue(false)
        });

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(2),
            ["IsDeleted"] = new BsonValue(false),
            ["Touched"] = new BsonValue(false)
        });

        var report = await db.Migrations()
            .Migration("remove-docs", m => m.ForCollection("tenant_*", c =>
            {
                c.RemoveDocumentWhen((doc, _) => doc["IsDeleted"].IsBoolean && doc["IsDeleted"].AsBoolean);
                c.SetFieldWhen("Touched", new BsonValue(true), BsonPredicates.Always);
            }))
            .RunAsync();

        var docs = await GetAllDocumentsAsync(collection);

        docs.Should().HaveCount(1);
        docs[0]["_id"].AsInt32.Should().Be(2);
        docs[0]["Touched"].AsBoolean.Should().BeTrue();
        report.Migrations[0].DocumentsRemoved.Should().Be(1);
    }

    [Fact]
    public async Task RemoveFieldWhen_ShouldPruneEmptyIndexedContainers_WhenEnabled()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Legacy"] = new BsonDocument
                    {
                        ["Tags"] = new BsonArray()
                    }
                }
            }
        });

        await db.Migrations()
            .Migration("remove-indexed-tags", m => m.ForCollection("tenant_*", c =>
                c.RemoveFieldWhen("Orders[0].Legacy.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc.ContainsKey("Orders").Should().BeFalse();
    }

    [Fact]
    public async Task RemoveFieldWhen_ShouldSupportWildcardPruningAcrossArrayElements()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Legacy"] = new BsonDocument
                    {
                        ["Tags"] = new BsonArray()
                    }
                },
                new BsonDocument
                {
                    ["Legacy"] = new BsonDocument
                    {
                        ["Tags"] = new BsonArray
                        {
                            new BsonValue("keep")
                        }
                    }
                }
            }
        });

        await db.Migrations()
            .Migration("remove-wildcard-tags", m => m.ForCollection("tenant_*", c =>
                c.RemoveFieldWhen("Orders[*].Legacy.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        orders.Count.Should().Be(1);
        orders[0].AsDocument["Legacy"].AsDocument["Tags"].AsArray.Count.Should().Be(1);
    }

    [Fact]
    public async Task RemoveFieldWhen_ShouldSupportRecursivePruningAcrossNestedContainers()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Profile"] = new BsonDocument
            {
                ["Legacy"] = new BsonDocument
                {
                    ["Tags"] = new BsonArray()
                }
            },
            ["Orders"] = new BsonArray
            {
                new BsonDocument
                {
                    ["Legacy"] = new BsonDocument
                    {
                        ["Tags"] = new BsonArray()
                    }
                },
                new BsonDocument
                {
                    ["Legacy"] = new BsonDocument
                    {
                        ["Tags"] = new BsonArray
                        {
                            new BsonValue("keep")
                        }
                    }
                }
            }
        });

        await db.Migrations()
            .Migration("remove-recursive-tags", m => m.ForCollection("tenant_*", c =>
                c.RemoveFieldWhen("**.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true)))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));
        var orders = doc["Orders"].AsArray;

        doc.ContainsKey("Profile").Should().BeFalse();
        orders.Count.Should().Be(1);
        orders[0].AsDocument["Legacy"].AsDocument["Tags"].AsArray.Count.Should().Be(1);
    }

    [Fact]
    public void RenameField_ShouldRejectWildcardPaths_InCurrentV2Slice()
    {
        var action = () => new CollectionMigrationBuilder().RenameField("Orders[*].LegacyId", "Orders[*].CustomerId");

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task SetFieldWhen_ShouldRejectRecursivePaths_InCurrentV2Slice()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Profile"] = new BsonDocument()
        });

        Func<Task> action = async () => await db.Migrations()
            .Migration("bad-recursive-set", m => m.ForCollection("tenant_*", c =>
                c.SetFieldWhen("**.Touched", new BsonValue(true), BsonPredicates.Always)))
            .RunAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void RepairReference_ShouldRejectWildcardPathsWithoutPairedGuard_InCurrentV2Slice()
    {
        var action = () => new CollectionMigrationBuilder()
            .RepairReference("Orders[*].CustomerId")
            .FromCollection("customers")
            .Apply();

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RepairReference_ShouldRejectMismatchedWildcardPairTopology_InCurrentV2Slice()
    {
        var action = () => new CollectionMigrationBuilder()
            .RepairReference("Orders[*].Customer.$id")
            .FromCollection("customers")
            .WhenReferenceCollectionIs("Orders.Customer.$ref")
            .Apply();

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RepairReference_ShouldRejectRecursivePaths_InCurrentV2Slice()
    {
        var action = () => new CollectionMigrationBuilder()
            .RepairReference("**.$id")
            .FromCollection("customers")
            .WhenReferenceCollectionIs("**.$ref")
            .Apply();

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ModifyDocumentWhen_ShouldReplaceWholeDocument_WhilePreservingId()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["FirstName"] = new BsonValue("Alice"),
            ["LastName"] = new BsonValue("Smith")
        });

        await db.Migrations()
            .Migration("modify-doc", m => m.ForCollection("tenant_*", c =>
                c.ModifyDocumentWhen(
                    (doc, _) => doc.ContainsKey("FirstName") && doc.ContainsKey("LastName"),
                    (doc, _) => new BsonDocument
                    {
                        ["_id"] = doc["_id"],
                        ["FullName"] = new BsonValue(doc["FirstName"].AsString + " " + doc["LastName"].AsString)
                    })))
            .RunAsync();

        var doc = await collection.FindById(new BsonValue(1));

        doc.ContainsKey("FirstName").Should().BeFalse();
        doc.ContainsKey("LastName").Should().BeFalse();
        doc["FullName"].AsString.Should().Be("Alice Smith");
    }

    [Fact]
    public async Task ModifyDocumentWhen_ShouldThrow_WhenMutatorChangesId()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Name"] = new BsonValue("Alice")
        });

        Func<Task> action = async () => await db.Migrations()
            .Migration("bad-modify", m => m.ForCollection("tenant_*", c =>
                c.ModifyDocumentWhen(
                    (doc, _) => true,
                    (doc, _) => new BsonDocument
                    {
                        ["_id"] = new BsonValue(2),
                        ["Name"] = doc["Name"]
                    })))
            .RunAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RemoveDocumentWhen_ShouldWorkDuringRebuildBeforeIdConversion()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("customers");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("507f1f77bcf86cd799439011"),
            ["Active"] = new BsonValue(true)
        });

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-id-value"),
            ["Active"] = new BsonValue(false)
        });

        var report = await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
            {
                c.RemoveDocumentWhen((doc, _) => doc["Active"].IsBoolean && doc["Active"].AsBoolean == false);
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId);
            }))
            .RunAsync();

        var docs = await GetAllDocumentsAsync(collection);
        var remaps = await GetAllDocumentsAsync(db.GetCollection("__migration_id_mappings"));

        docs.Should().ContainSingle();
        docs[0]["_id"].IsObjectId.Should().BeTrue();
        remaps.Should().BeEmpty();
        report.Migrations[0].DocumentsRemoved.Should().Be(1);
    }

    [Fact]
    public async Task DryRun_ShouldPreviewInPlaceMutations_WithoutPersistingChangesOrHistory()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var collection = db.GetCollection("tenant_one");

        await collection.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue(1),
            ["Name"] = new BsonValue("  Alice  "),
            ["Tags"] = new BsonArray(),
            ["Metadata"] = new BsonDocument()
        });

        var report = await db.Migrations()
            .Migration("preview-in-place", m => m.ForCollection("tenant_*", c =>
            {
                c.ModifyFieldWhen("Name", ctx => new BsonValue(ctx.Value.AsString.Trim()), BsonPredicates.IsString);
                c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
                c.SetDefaultWhenMissing("Metadata.Source", new BsonValue("preview"));
            }))
            .RunAsync(new MigrationRunOptions { DryRun = true });

        var doc = await collection.FindById(new BsonValue(1));
        var history = await db.GetCollection("__migrations").FindById(new BsonValue("preview-in-place"));

        report.Migrations[0].IsDryRun.Should().BeTrue();
        report.Migrations[0].WasApplied.Should().BeFalse();
        report.Migrations[0].WasSkipped.Should().BeFalse();
        report.Migrations[0].DocumentsModified.Should().Be(1);
        doc["Name"].AsString.Should().Be("  Alice  ");
        doc.ContainsKey("Tags").Should().BeTrue();
        doc["Metadata"].AsDocument.ContainsKey("Source").Should().BeFalse();
        ((object)history).Should().BeNull();
    }

    [Fact]
    public async Task DryRun_ShouldPreviewRebuildWithoutCreatingBackupsOrRemapRows()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        var report = await db.Migrations()
            .Migration("preview-rebuild", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync(new MigrationRunOptions { DryRun = true, BackupRetentionPolicy = BackupRetentionPolicy.DeleteOnSuccess });

        var docs = await GetAllDocumentsAsync(customers);
        var remaps = await GetAllDocumentsAsync(db.GetCollection("__migration_id_mappings"));
        var history = await db.GetCollection("__migrations").FindById(new BsonValue("preview-rebuild"));
        var collectionNames = await GetCollectionNamesAsync(db);
        var collectionReport = report.Migrations[0].Selectors[0].Collections[0];

        report.Migrations[0].IsDryRun.Should().BeTrue();
        report.Migrations[0].GeneratedIdMappings.Should().Be(1);
        docs.Should().ContainSingle();
        docs[0]["_id"].IsString.Should().BeTrue();
        remaps.Should().BeEmpty();
        ((object)history).Should().BeNull();
        collectionNames.Should().NotContain(name => name.StartsWith("customers__backup__", StringComparison.OrdinalIgnoreCase));
        collectionNames.Should().NotContain(name => name.StartsWith("customers__migrating__", StringComparison.OrdinalIgnoreCase));
        collectionReport.BackupDisposition.Should().Be(BackupDisposition.Planned);
        collectionReport.BackupCollectionName.Should().StartWith("customers__backup__");
    }

    [Fact]
    public async Task BackupRetention_DeleteOnSuccess_ShouldRemoveBackupCollectionAfterSwap()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");

        await customers.Insert(new BsonDocument
        {
            ["_id"] = new BsonValue("bad-customer-id"),
            ["Name"] = new BsonValue("Alice")
        });

        var report = await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .WithBackupRetention(BackupRetentionPolicy.DeleteOnSuccess)
            .RunAsync();

        var collectionNames = await GetCollectionNamesAsync(db);
        var collectionReport = report.Migrations[0].Selectors[0].Collections[0];

        collectionNames.Should().NotContain(name => name.StartsWith("customers__backup__", StringComparison.OrdinalIgnoreCase));
        collectionReport.BackupDisposition.Should().Be(BackupDisposition.Deleted);
        collectionReport.BackupCollectionName.Should().BeNull();
    }

    [Fact]
    public async Task CleanupBackupsAsync_ShouldDeleteMatchingBackupsOnly()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var orders = db.GetCollection("orders");

        await customers.Insert(new BsonDocument { ["_id"] = new BsonValue("bad-customer-id") });
        await orders.Insert(new BsonDocument { ["_id"] = new BsonValue("bad-order-id") });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .Migration("convert-orders", m => m.ForCollection("orders", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var cleanup = await db.Migrations().CleanupBackupsAsync("customers");
        var collectionNames = await GetCollectionNamesAsync(db);

        cleanup.TotalDeleted.Should().Be(1);
        cleanup.Results.Should().ContainSingle();
        cleanup.Results[0].SourceCollection.Should().Be("customers");
        collectionNames.Should().NotContain(name => name.StartsWith("customers__backup__", StringComparison.OrdinalIgnoreCase));
        collectionNames.Should().Contain(name => name.StartsWith("orders__backup__", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CleanupBackupsAsync_DryRun_ShouldReportWithoutDeleting()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");

        await customers.Insert(new BsonDocument { ["_id"] = new BsonValue("bad-customer-id") });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var cleanup = await db.Migrations().CleanupBackupsAsync("customers", new BackupCleanupOptions { DryRun = true });
        var collectionNames = await GetCollectionNamesAsync(db);

        cleanup.TotalDeleted.Should().Be(0);
        cleanup.TotalPlanned.Should().Be(1);
        cleanup.Results[0].Disposition.Should().Be(BackupCleanupDisposition.Planned);
        collectionNames.Should().Contain(name => name.StartsWith("customers__backup__", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WildcardMigrations_ShouldSkipBackupArtifactsByDefault()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var customers = db.GetCollection("customers");
        var users = db.GetCollection("users");

        await customers.Insert(new BsonDocument { ["_id"] = new BsonValue("bad-customer-id") });
        await users.Insert(new BsonDocument { ["_id"] = new BsonValue(1), ["Touched"] = new BsonValue(false) });

        await db.Migrations()
            .Migration("convert-customers", m => m.ForCollection("customers", c =>
                c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
            .RunAsync();

        var report = await db.Migrations()
            .Migration("touch-all", m => m.ForCollection("*", c =>
                c.SetFieldWhen("Touched", new BsonValue(true), BsonPredicates.Always)))
            .RunAsync();

        report.Migrations[0].Selectors[0].MatchedCollections.Should().NotContain(name => name.Contains("__backup__", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<List<BsonDocument>> GetAllDocumentsAsync(ILiteCollection<BsonDocument> collection)
    {
        var result = new List<BsonDocument>();

        await foreach (var doc in collection.FindAll())
        {
            result.Add(doc);
        }

        return result;
    }

    private static async Task<List<BsonDocument>> GetAllDocumentsAsync(ILiteQueryableResult<BsonDocument> query)
    {
        var result = new List<BsonDocument>();

        await foreach (var doc in query.ToDocuments())
        {
            result.Add(doc);
        }

        return result;
    }

    private static async Task<List<string>> GetCollectionNamesAsync(ILiteDatabase database)
    {
        var result = new List<string>();

        await foreach (var name in database.GetCollectionNames())
        {
            result.Add(name);
        }

        return result;
    }
}

