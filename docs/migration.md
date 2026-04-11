# LiteDbX.Migrations Usage Guide

`LiteDbX.Migrations` is a raw-BSON migration library for fixing legacy data without depending on typed entity deserialization.

Use it when you need to:

- convert a field or `_id` from `string` to `ObjectId`
- remove obsolete or default-valued fields
- add, set, copy, move, or rename fields
- repair references after `_id` rebuilds
- run one migration against one collection or many collections
- preview changes with dry-run reporting before committing them

---

## Basic shape

A migration run starts from `ILiteDatabase`:

```csharp
using LiteDbX;
using LiteDbX.Migrations;

await using var db = await LiteDatabase.Open("app.db");

var report = await db.Migrations()
    .Migration("2026-04-10-cleanup", m => m.ForCollection("customers", c =>
    {
        c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
    }))
    .RunAsync();
```

The usual flow is:

1. call `db.Migrations()`
2. add one or more named `.Migration(...)` entries
3. inside each migration, target collections with `.ForCollection(...)`
4. configure operations on `CollectionMigrationBuilder`
5. execute with `.RunAsync()`
6. inspect the returned `MigrationReport`

Migrations are journaled and intended to run once per migration name.

---

## Selecting collections

`ForCollection(...)` accepts either an exact collection name or a glob-style selector.

### Exact collection

```csharp
.ForCollection("customers", c => { ... })
```

### All user collections

```csharp
.ForCollection("*", c => { ... })
```

### Pattern matching

```csharp
.ForCollection("tenant_*", c => { ... })
.ForCollection("*_archive", c => { ... })
.ForCollection("*_settings_*", c => { ... })
```

By default, migration infrastructure and backup/shadow collections are excluded from wildcard selection.

If you really need them, use:

```csharp
var runner = db.Migrations().IncludeSystemCollections();
```

---

## Example: convert `_id` from `string` to `ObjectId`

Changing `_id` is a rebuild/swap migration, because the collection identity has to be rewritten safely.

```csharp
using LiteDbX;
using LiteDbX.Migrations;

await using var db = await LiteDatabase.Open("app.db");

var report = await db.Migrations()
    .Migration("customers-id-to-objectid", m => m.ForCollection("customers", c =>
    {
        c.ConvertId()
            .FromStringToObjectId()
            .OnInvalidString(InvalidObjectIdPolicy.GenerateNewId);
    }))
    .RunAsync();
```

### What this does

- reads each document from `customers`
- converts valid string `_id` values into `ObjectId`
- if a string `_id` is invalid, applies the configured `InvalidObjectIdPolicy`
- rebuilds the collection safely
- preserves a backup collection unless retention says otherwise
- stores generated old->new id mappings in `__migration_id_mappings`

### Supported `_id` invalid policies

```csharp
public enum InvalidObjectIdPolicy
{
    Fail,
    SkipDocument,
    LeaveUnchanged,
    RemoveField,
    GenerateNewId
}
```

For `_id` conversion specifically, the meaningful/recommended choices are:

- `Fail` — stop the migration on the first invalid `_id`
- `SkipDocument` — omit documents with invalid `_id` values from the rebuilt collection
- `GenerateNewId` — create a new `ObjectId` and persist an id remap row

For `_id` rebuilds, `GenerateNewId` is the usual recovery choice.

### Optional execution controls

```csharp
var report = await db.Migrations()
    .WithBackupRetention(BackupRetentionPolicy.DeleteOnSuccess)
    .Migration("customers-id-to-objectid", m => m.ForCollection("customers", c =>
    {
        c.ConvertId()
            .FromStringToObjectId()
            .OnInvalidString(InvalidObjectIdPolicy.GenerateNewId);
    }))
    .RunAsync(new MigrationRunOptions
    {
        DryRun = true,
        StrictPathResolution = true
    });
```

In dry-run mode, you still get preview/report information without writing data.

---

## Example: remove an empty-array field and a default-valued field

Use `RemoveFieldWhen(...)` with built-in predicates.

```csharp
using LiteDbX;
using LiteDbX.Migrations;

await using var db = await LiteDatabase.Open("app.db");

await db.Migrations()
    .Migration("cleanup-defaults", m => m.ForCollection("customers", c =>
    {
        c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
        c.RemoveFieldWhen("Status", BsonPredicates.Default(new BsonValue("Unknown")));
    }))
    .RunAsync();
```

### Remove nested empty containers too

If removing a child field leaves parent containers empty, you can prune them:

```csharp
await db.Migrations()
    .Migration("cleanup-nested", m => m.ForCollection("customers", c =>
    {
        c.RemoveFieldWhen("Profile.Legacy.Tags", BsonPredicates.EmptyArray, pruneEmptyParents: true);
        c.RemoveFieldWhen("Profile.Legacy.State", BsonPredicates.Default(new BsonValue("Unknown")), pruneEmptyParents: true);
    }))
    .RunAsync();
```

### Other useful cleanup predicates

Examples:

```csharp
BsonPredicates.EmptyString
BsonPredicates.WhiteSpaceString
BsonPredicates.TrimmedEmptyString
BsonPredicates.Null
BsonPredicates.NullOrMissing
BsonPredicates.EmptyDocument
BsonPredicates.EmptyBinary
BsonPredicates.EmptyGuid
BsonPredicates.EmptyObjectId
BsonPredicates.ZeroNumber
BsonPredicates.FalseBoolean
BsonPredicates.MinValue
BsonPredicates.MaxValue
BsonPredicates.NullLike
BsonPredicates.StructurallyEmpty
BsonPredicates.UselessValue
BsonPredicates.UselessValueAggressive
```

You can also compose them:

```csharp
var cleanup = BsonPredicates.AnyOf(
    BsonPredicates.EmptyArray,
    BsonPredicates.EmptyDocument,
    BsonPredicates.Default(new BsonValue("Unknown")));

await db.Migrations()
    .Migration("cleanup-composed", m => m.ForCollection("customers", c =>
        c.RemoveFieldWhen("Legacy.Payload", cleanup)))
    .RunAsync();
```

---

## Common field operations

### Add a field if missing

```csharp
c.AddFieldWhen("Metadata.Source", new BsonValue("migration"), BsonPredicates.Missing);
```

### Add with parent creation or a write mode

```csharp
c.AddFieldWhen(
    "Metadata.Source.Name",
    new BsonValue("generated"),
    BsonPredicates.Missing,
    new FieldMutationOptions
    {
        CreateParents = true,
        WriteMode = FieldWriteMode.MissingOnly
    });
```

### Set/overwrite a field

```csharp
c.SetFieldWhen("Flags.Active", new BsonValue(true), BsonPredicates.Always);
```

### Set only when target exists, or is null/missing

```csharp
c.SetFieldWhen(
    "Metadata.Source",
    new BsonValue("new"),
    BsonPredicates.Always,
    new FieldMutationOptions { WriteMode = FieldWriteMode.ExistingOnly });

c.SetFieldWhen(
    "Metadata.Source",
    new BsonValue("new"),
    BsonPredicates.Always,
    new FieldMutationOptions { WriteMode = FieldWriteMode.NullOrMissing });
```

### Default a field only when missing

```csharp
c.SetDefaultWhenMissing("Metadata.Source", new BsonValue("migration"));
```

### Modify a field in place

```csharp
c.ModifyFieldWhen(
    "Name",
    ctx => new BsonValue(ctx.Value.AsString.Trim()),
    BsonPredicates.IsString);
```

### Rename / copy / move fields

```csharp
c.RenameField("Legacy.OwnerId", "Owner.Id");
c.CopyField("Profile.Settings", "Profile.SettingsCopy");
c.MoveField("LegacyId", "CurrentId");
```

### Convert a non-`_id` field from `string` to `ObjectId`

```csharp
c.ConvertField("CustomerId")
    .FromStringToObjectId()
    .OnInvalidString(InvalidObjectIdPolicy.RemoveField);
```

### Remove or modify whole documents

```csharp
c.RemoveDocumentWhen((doc, _) => doc["Active"].IsBoolean && doc["Active"].AsBoolean == false);

c.ModifyDocumentWhen(
    (doc, _) => doc.ContainsKey("FirstName") && doc.ContainsKey("LastName"),
    (doc, _) => new BsonDocument
    {
        ["_id"] = doc["_id"],
        ["FullName"] = new BsonValue(doc["FirstName"].AsString + " " + doc["LastName"].AsString)
    });
```

### Insert seed/reference documents

```csharp
c.InsertDocumentWhen(new BsonDocument
{
    ["_id"] = new BsonValue(42),
    ["Code"] = new BsonValue("default")
});
```

Or compute the document dynamically:

```csharp
c.InsertDocumentWhen(
    ctx => new BsonDocument
    {
        ["_id"] = new BsonValue(42),
        ["Collection"] = new BsonValue(ctx.CollectionName)
    },
    when: insert => !insert.ExistsById);
```

### Collection-wide cleanup passes

```csharp
c.RemoveWhere(BsonPredicates.UselessValueAggressive, CleanupScope.Recursive);
c.PruneEmptyContainers();
```

### Repair references after `_id` remapping

```csharp
c.RepairReference("Customer.$id")
    .FromCollection("customers")
    .WhenReferenceCollectionIs("Customer.$ref")
    .Apply();
```

---

## Path syntax

Supported path styles include:

- top-level field: `Name`
- nested path: `Profile.CustomerId`
- fixed array index: `Orders[0].CustomerId`
- wildcard array traversal: `Orders[*].CustomerId`
- recursive descent: `**.LegacyId`

Examples:

```csharp
c.RemoveFieldWhen("Orders[*].Tags", BsonPredicates.EmptyArray);
c.SetFieldWhen("**.Touched", new BsonValue(true), BsonPredicates.Always);
c.ConvertField("Orders[0].CustomerId").FromStringToObjectId();
```

### Important path notes

- mixed document shapes are safe no-ops by default
- enable strict failures with `StrictPathResolution`
- paired wildcard transfer operations require aligned source/target topology
- recursive `RepairReference(...)` is still intentionally unsupported

---

## Runner options

### Runner-level defaults

These set defaults for later runs:

```csharp
var runner = db.Migrations()
    .UseJournal("__migrations")
    .UseIdMappingCollection("__migration_id_mappings")
    .IncludeSystemCollections(false)
    .DryRun()
    .WithBackupRetention(BackupRetentionPolicy.KeepAll)
    .UseStrictPathResolution()
    .OnProgress(progress => Console.WriteLine(progress.Stage));
```

### Per-run options

```csharp
var report = await runner.RunAsync(new MigrationRunOptions
{
    DryRun = true,
    BackupRetentionPolicy = BackupRetentionPolicy.DeleteOnSuccess,
    StrictPathResolution = true,
    ProgressCallback = progress => Console.WriteLine(progress.Stage)
});
```

Per-run options override the runner defaults only for properties you explicitly set.

---

## Progress callbacks

Progress callbacks receive `MigrationProgress` values with:

- `Stage`
- `MigrationName`
- `Selector`
- `CollectionName`
- `IsDryRun`
- `CompletedCollections`
- `TotalCollections`
- `DocumentsScanned`
- `DocumentsModified`
- `DocumentsRemoved`
- `DocumentsInserted`

Example:

```csharp
await db.Migrations()
    .OnProgress(progress =>
        Console.WriteLine($"[{progress.Stage}] {progress.CollectionName} modified={progress.DocumentsModified}"))
    .Migration("demo", m => m.ForCollection("customers", c =>
        c.SetDefaultWhenMissing("Metadata.Source", new BsonValue("migration"))))
    .RunAsync();
```

---

## Backup cleanup helpers

After rebuild migrations, you can clean up retained backups:

```csharp
await db.Migrations().CleanupBackupsAsync("customers");
await db.Migrations().CleanupBackupsAsync("customers", new BackupCleanupOptions
{
    DryRun = true,
    KeepLatestCount = 1
});
```

---

## Reporting

`RunAsync()` returns `MigrationReport`.

Useful values include:

### `MigrationExecutionResult`

- `Name`
- `RunId`
- `WasApplied`
- `IsDryRun`
- `WasSkipped`
- `DocumentsScanned`
- `DocumentsModified`
- `DocumentsRemoved`
- `DocumentsInserted`
- `GeneratedIdMappings`
- `RepairedReferences`
- `InvalidValueCount`
- `Selectors`

### `CollectionMigrationResult`

- `CollectionName`
- `DocumentsScanned`
- `DocumentsModified`
- `DocumentsRemoved`
- `DocumentsInserted`
- `GeneratedIdMappings`
- `RepairedReferences`
- `InvalidValueCount`
- `InvalidValueSamples`
- `RebuildValidation`
- `BackupCollectionName`
- `BackupDisposition`

### Rebuild-specific report details

`RebuildValidation` can include:

- `SourceDocumentCount`
- `ExpectedTargetDocumentCount`
- `PreparedTargetDocumentCount`
- `SecondaryIndexesToReplayCount`
- `SecondaryIndexesToReplay`
- `DuplicateTargetIdCount`
- `DuplicateTargetIdSamples`

---

## Quick reference: available operations

On `CollectionMigrationBuilder`, the main public operations are:

- `RemoveFieldWhen(...)`
- `RemoveDocumentWhen(...)`
- `InsertDocumentWhen(...)`
- `RemoveWhere(...)`
- `PruneEmptyContainers(...)`
- `AddFieldWhen(...)`
- `SetDefaultWhenMissing(...)`
- `SetFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `ModifyDocumentWhen(...)`
- `RenameField(...)`
- `CopyField(...)`
- `MoveField(...)`
- `ConvertField(...)`
- `ConvertId()`
- `RepairReference(...)`

Predicate helpers include:

- `Default(...)`
- `AnyOfDefaults(...)`
- `And(...)`
- `Or(...)`
- `Not(...)`
- `AnyOf(...)`
- `AllOf(...)`
- all the built-in field/value predicates in `BsonPredicates`

---

## A practical pattern

A common production workflow is:

1. write the migration with a stable name
2. run it once with `DryRun = true`
3. inspect `MigrationReport`
4. run it for real
5. clean up retained backups later if desired

Example:

```csharp
var preview = await db.Migrations()
    .Migration("customers-cleanup-v2", m => m.ForCollection("customers", c =>
    {
        c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
        c.RemoveFieldWhen("Status", BsonPredicates.Default(new BsonValue("Unknown")));
    }))
    .RunAsync(new MigrationRunOptions { DryRun = true });

var applied = await db.Migrations()
    .Migration("customers-cleanup-v2", m => m.ForCollection("customers", c =>
    {
        c.RemoveFieldWhen("Tags", BsonPredicates.EmptyArray);
        c.RemoveFieldWhen("Status", BsonPredicates.Default(new BsonValue("Unknown")));
    }))
    .RunAsync();
```