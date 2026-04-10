# Operational Safety Plan

## Goal

Add operator-focused execution controls to `LiteDbX.Migrations` now that the library supports:

- field-level mutation
- whole-document mutation
- `_id` rebuild/swap migrations
- durable id remap logging
- reference repair using `__migration_id_mappings`

The current safety slice now includes:

- dry-run execution
- backup retention policy
- backup cleanup helpers
- preview-only invalid value sampling for ObjectId conversion paths
- pre-swap validation summary for rebuild migrations
- duplicate target `_id` preview details for rebuild dry-runs
- planned secondary-index replay details for rebuild migrations
- keep-latest-N backup cleanup retention
- strict path resolution
- progress callbacks
- collection-level inserted-document reporting
- preview/report behavior that helps operators verify a migration before committing it

---

## Current implemented baseline

At this point the library already supports:

- wildcard `ForCollection(...)`
- `ConvertField(...).FromStringToObjectId()`
- `ConvertId().FromStringToObjectId()`
- `AddFieldWhen(...)`
- `ModifyFieldWhen(...)`
- `RemoveFieldWhen(...)`
- `SetFieldWhen(...)`
- `SetDefaultWhenMissing(...)`
- `CopyField(...)`
- `RenameField(...)`
- `MoveField(...)`
- `RemoveDocumentWhen(...)`
- `ModifyDocumentWhen(...)`
- durable remap logging in `__migration_id_mappings`
- reference repair from durable remap data
- backup collection creation during rebuild/swap migrations
- dry-run execution via `MigrationRunOptions`
- backup retention policy via `BackupRetentionPolicy`
- strict path resolution via `MigrationRunOptions.StrictPathResolution` and `MigrationRunner.UseStrictPathResolution()`
- progress callbacks via `MigrationRunOptions.ProgressCallback` and `MigrationRunner.OnProgress(...)`
- backup cleanup via `CleanupBackupsAsync(...)`
- dry-run invalid value counts and sampled preview entries for `ConvertField(...)` and `ConvertId()` ObjectId conversion failures
- rebuild validation summaries exposing source count, expected/prepared target count, and planned secondary index replay count
- dry-run duplicate target `_id` counts and capped duplicate samples for rebuild migrations
- capped planned secondary-index replay descriptors (`Name`, `Expression`, `Unique`) for rebuild reports
- keep-latest-N pruning via `BackupCleanupOptions.KeepLatestCount`
- collection-level insert operations and inserted-document counters via `InsertDocumentWhen(...)`, `MigrationExecutionResult.DocumentsInserted`, and `CollectionMigrationResult.DocumentsInserted`

So the most important remaining work is operational control, not more mutation surface.

---

## Recommended public API

### Options model

Prefer a dedicated options object for execution concerns:

```csharp
await db.Migrations()
    .Migration("preview-customers", m => m.ForCollection("customers", c =>
        c.ConvertId().FromStringToObjectId().OnInvalidString(InvalidObjectIdPolicy.GenerateNewId)))
    .RunAsync(new MigrationRunOptions
    {
        DryRun = true,
        BackupRetentionPolicy = BackupRetentionPolicy.KeepAll
    });
```

If fluent defaults are configured on the runner first, per-run options now merge over those defaults instead of replacing them wholesale. For example, a runner-level `OnProgress(...)` callback remains active unless a per-run `ProgressCallback` override is explicitly supplied.

### Optional fluent convenience

It is also reasonable to support:

```csharp
db.Migrations()
    .DryRun()
    .WithBackupRetention(BackupRetentionPolicy.DeleteOnSuccess)
```

as a thin wrapper over default run options.

---

## Dry-run behavior

### Rules

A dry run should:

- resolve collection selectors normally
- evaluate predicates normally
- execute document/path mutation logic normally on cloned in-memory documents
- perform rebuild planning normally, including `_id` conversion and duplicate-target-id validation
- read current secondary indexes normally

A dry run must **not**:

- write document updates
- delete documents
- insert into shadow collections
- rename collections
- write `__migrations`
- write `__migration_id_mappings`
- drop or prune backups

### Reporting semantics

In dry-run mode, the normal report counters should be interpreted as “would” values:

- `DocumentsModified`
- `DocumentsRemoved`
- `DocumentsInserted`
- `GeneratedIdMappings`
- `RepairedReferences`

The report should also expose:

- `IsDryRun`
- planned backup name for rebuild migrations
- planned backup disposition
- progress events emitted during selector/migration execution
- invalid value counts and a capped sample list for ObjectId conversion preview
- rebuild validation summary with source vs prepared target counts and secondary index replay count
- duplicate target `_id` counts and capped duplicate samples during rebuild preview
- planned secondary-index replay details for rebuild preview and applied rebuild reports

---

## Backup retention policy

### Recommended enum

```csharp
public enum BackupRetentionPolicy
{
    KeepAll,
    DeleteOnSuccess
}
```

### Semantics

#### `KeepAll`
- preserve current behavior
- retain the backup collection after a successful swap

#### `DeleteOnSuccess`
- delete the backup collection only after:
  - shadow collection rename succeeded
  - remap log writes succeeded
  - migration is otherwise complete
- never delete backups on failure
- never delete backups in dry-run mode

### Reporting

Per collection, expose:

- `BackupCollectionName`
- `BackupDisposition`

Suggested values:

- `None`
- `Planned`
- `Kept`
- `Deleted`

---

## Additional small operational additions worth planning

These fit naturally in the same slice and should stay on the roadmap:

### 1. Duplicate target `_id` validation
This now surfaces preview details in rebuild dry-runs.

Per collection, `RebuildValidation` now also exposes:

- `DuplicateTargetIdCount`
- `DuplicateTargetIdSamples`

with representative source/duplicate pairs for conflicting target ids.

### 2. Preview-only invalid value samples
This is now implemented for ObjectId conversion preview paths.

Per collection, dry-run reports now expose:

- `InvalidValueCount`
- `InvalidValueSamples`

with concrete resolved paths such as:

- `Orders[0].LegacyId`
- `Profile.LegacyId`
- `_id`

### 3. Backup cleanup helper
This is now implemented. Operators can use:

```csharp
await db.Migrations().CleanupBackupsAsync("customers");
await db.Migrations().CleanupBackupsAsync("customers", new BackupCleanupOptions { KeepLatestCount = 1 });
```

to prune retained backup collections by collection selector, with optional dry-run preview.

Cleanup reports now also expose retained entries when `KeepLatestCount` is used, including:

- `Disposition = Retained`
- `AppliedUtc`

### 4. Pre-swap validation summary
This is now implemented for rebuild migrations.

Per collection, rebuild reports now expose `RebuildValidation` with:

- `SourceDocumentCount`
- `ExpectedTargetDocumentCount`
- `PreparedTargetDocumentCount`
- `SecondaryIndexesToReplayCount`
- `SecondaryIndexesToReplay`

---

## Tests to keep in the roadmap

### Dry run

- in-place dry run does not mutate source documents
- rebuild dry run does not create backup/shadow collections
- rebuild dry run does not write remap rows
- dry run does not write migration history
- dry run still reports would-modify / would-remove / would-remap counts
- dry run reports invalid value counts and capped samples for field/id ObjectId conversion preview
- rebuild dry-run reports pre-swap validation summary counts without creating shadow/backup collections
- rebuild dry-run reports duplicate target `_id` counts and capped samples without failing the preview run
- backup cleanup supports keep-latest-N retention with dry-run preview and retained/deleted reporting

### Backup retention

- `KeepAll` retains backup collection after successful swap
- `DeleteOnSuccess` removes backup collection after successful swap
- `DeleteOnSuccess` does not remove backup on failure
- dry-run ignores retention deletion and reports `Planned`

---

## Recommendation

The mutation surface is now broad enough that operational safety should be treated as a first-class feature area.

The next priority after this slice should likely be one of:

- V2 path support for arrays and wildcards
- richer dry-run reporting samples
- optional shell/CLI integration for operators
- more advanced backup lifecycle policies such as keep-latest-N or age-based pruning

