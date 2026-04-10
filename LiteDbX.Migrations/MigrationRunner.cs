using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

public sealed class MigrationRunner
{
    private readonly ILiteDatabase _database;
    private readonly List<MigrationDefinition> _migrations = new();
    private readonly MigrationRunOptions _defaultRunOptions = new();
    private string _journalCollection = "__migrations";
    private string _idMappingCollection = "__migration_id_mappings";
    private bool _includeSystemCollections;

    public MigrationRunner(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public MigrationRunner UseJournal(string collectionName)
    {
        _journalCollection = string.IsNullOrWhiteSpace(collectionName) ? throw new ArgumentNullException(nameof(collectionName)) : collectionName;
        return this;
    }

    public MigrationRunner UseIdMappingCollection(string collectionName)
    {
        _idMappingCollection = string.IsNullOrWhiteSpace(collectionName) ? throw new ArgumentNullException(nameof(collectionName)) : collectionName;
        return this;
    }

    public MigrationRunner IncludeSystemCollections(bool include = true)
    {
        _includeSystemCollections = include;
        return this;
    }

    public MigrationRunner DryRun(bool enabled = true)
    {
        _defaultRunOptions.DryRun = enabled;
        return this;
    }

    public MigrationRunner WithBackupRetention(BackupRetentionPolicy policy)
    {
        _defaultRunOptions.BackupRetentionPolicy = policy;
        return this;
    }

    public MigrationRunner Migration(string name, Action<MigrationBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var builder = new MigrationBuilder();
        configure(builder);
        _migrations.Add(builder.Build(name));
        return this;
    }

    public ValueTask<BackupCleanupReport> CleanupBackupsAsync(string selector, CancellationToken cancellationToken = default)
        => CleanupBackupsAsync(selector, new BackupCleanupOptions(), cancellationToken);

    public async ValueTask<BackupCleanupReport> CleanupBackupsAsync(string selector, BackupCleanupOptions options, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentNullException(nameof(selector));
        options ??= new BackupCleanupOptions();

        var collectionSelector = new MigrationCollectionSelector(_journalCollection, _idMappingCollection, _includeSystemCollections);
        var results = new List<BackupCleanupResult>();

        await foreach (var name in _database.GetCollectionNames(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!TryParseBackupCollectionName(name, out var sourceCollection, out var runIdSuffix))
            {
                continue;
            }

            if (!collectionSelector.IsMatch(selector, sourceCollection))
            {
                continue;
            }

            if (options.DryRun)
            {
                results.Add(new BackupCleanupResult(sourceCollection, name, runIdSuffix, BackupCleanupDisposition.Planned));
                continue;
            }

            if (await _database.DropCollection(name, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new BackupCleanupResult(sourceCollection, name, runIdSuffix, BackupCleanupDisposition.Deleted));
            }
        }

        return new BackupCleanupReport(new ReadOnlyCollection<BackupCleanupResult>(results));
    }

    public ValueTask<MigrationReport> RunAsync(CancellationToken cancellationToken = default)
        => RunAsync(_defaultRunOptions.Clone(), cancellationToken);

    public async ValueTask<MigrationReport> RunAsync(MigrationRunOptions options, CancellationToken cancellationToken = default)
    {
        options ??= new MigrationRunOptions();

        var selector = new MigrationCollectionSelector(_journalCollection, _idMappingCollection, _includeSystemCollections);
        var history = new MigrationHistoryStore(_database, _journalCollection);
        var idRemapLog = new IdRemapLog(_database, _idMappingCollection);
        var results = new List<MigrationExecutionResult>(_migrations.Count);

        foreach (var migration in _migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await history.IsAppliedAsync(migration.Name, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new MigrationExecutionResult(migration.Name, runId: string.Empty, wasApplied: false, isDryRun: false, selectors: Array.Empty<CollectionSelectorResult>(), documentsScanned: 0, documentsModified: 0, documentsRemoved: 0, generatedIdMappings: 0, repairedReferences: 0));
                continue;
            }

            var runId = Guid.NewGuid().ToString("N");
            var selectorResults = new List<CollectionSelectorResult>();
            var scanned = 0;
            var modified = 0;
            var removed = 0;
            var generatedIdMappings = 0;
            var repairedReferences = 0;
            var resolvedDefinitions = new List<ResolvedCollectionMigrationDefinition>(migration.Collections.Count);

            foreach (var definition in migration.Collections)
            {
                var matchedCollections = await selector.ResolveAsync(_database, definition.Selector, cancellationToken).ConfigureAwait(false);
                resolvedDefinitions.Add(new ResolvedCollectionMigrationDefinition(definition, matchedCollections));
            }

            foreach (var resolvedDefinition in resolvedDefinitions)
            {
                var definition = resolvedDefinition.Definition;
                var matchedCollections = resolvedDefinition.MatchedCollections;
                var collectionResults = new List<CollectionMigrationResult>(matchedCollections.Count);

                foreach (var collectionName in matchedCollections)
                {
                    var collectionResult = definition.Plan.RequiresRebuild
                        ? await ExecuteRebuildCollectionAsync(collectionName, definition.Selector, migration.Name, runId, definition.Plan, options, idRemapLog, cancellationToken).ConfigureAwait(false)
                        : await ExecuteInPlaceCollectionAsync(collectionName, migration.Name, definition.Plan, options, cancellationToken).ConfigureAwait(false);

                    var collectionScanned = collectionResult.DocumentsScanned;
                    var collectionModified = collectionResult.DocumentsModified;

                    scanned += collectionScanned;
                    modified += collectionModified;
                    removed += collectionResult.DocumentsRemoved;
                    generatedIdMappings += collectionResult.GeneratedIdMappings;
                    repairedReferences += collectionResult.RepairedReferences;

                    collectionResults.Add(new CollectionMigrationResult(collectionName, collectionScanned, collectionModified, collectionResult.DocumentsRemoved, collectionResult.GeneratedIdMappings, collectionResult.RepairedReferences, collectionResult.BackupCollectionName, collectionResult.BackupDisposition));
                }

                selectorResults.Add(new CollectionSelectorResult(definition.Selector, matchedCollections, new ReadOnlyCollection<CollectionMigrationResult>(collectionResults)));
            }

            var result = new MigrationExecutionResult(
                migration.Name,
                runId,
                wasApplied: !options.DryRun,
                isDryRun: options.DryRun,
                selectors: new ReadOnlyCollection<CollectionSelectorResult>(selectorResults),
                documentsScanned: scanned,
                documentsModified: modified,
                documentsRemoved: removed,
                generatedIdMappings: generatedIdMappings,
                repairedReferences: repairedReferences);

            if (!options.DryRun)
            {
                await history.MarkAppliedAsync(migration.Name, result, cancellationToken).ConfigureAwait(false);
            }

            results.Add(result);
        }

        return new MigrationReport(new ReadOnlyCollection<MigrationExecutionResult>(results));
    }

    private async ValueTask<CollectionExecutionResult> ExecuteInPlaceCollectionAsync(string collectionName, string migrationName, CollectionMigrationPlan plan, MigrationRunOptions options, CancellationToken cancellationToken)
    {
        var collection = _database.GetCollection(collectionName);
        var collectionScanned = 0;
        var collectionModified = 0;
        var collectionRemoved = 0;
        var remapLookup = await LoadRemapLookupAsync(plan, cancellationToken).ConfigureAwait(false);
        var context = new DocumentMigrationExecutionContext(collectionName, migrationName, string.Empty, remapLookup);

        await foreach (var document in collection.Query().ToDocuments(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            collectionScanned++;

            var currentDocument = document;
            var operationResult = ApplyDocumentOperations(currentDocument, context, plan.Operations, DocumentOperationStage.PreId);

            if (operationResult.DeleteDocument)
            {
                if (options.DryRun)
                {
                    collectionRemoved++;
                }
                else if (await collection.Delete(document["_id"], cancellationToken).ConfigureAwait(false))
                {
                    collectionRemoved++;
                }

                continue;
            }

            currentDocument = operationResult.Document ?? currentDocument;

            var postResult = ApplyDocumentOperations(currentDocument, context, plan.Operations, DocumentOperationStage.PostId);

            if (postResult.DeleteDocument)
            {
                if (options.DryRun)
                {
                    collectionRemoved++;
                }
                else if (await collection.Delete(document["_id"], cancellationToken).ConfigureAwait(false))
                {
                    collectionRemoved++;
                }

                continue;
            }

            currentDocument = postResult.Document ?? currentDocument;

            if (!operationResult.Changed && !postResult.Changed)
            {
                continue;
            }

            if (options.DryRun)
            {
                collectionModified++;
            }
            else
            {
                var updated = await collection.Update(currentDocument, cancellationToken).ConfigureAwait(false);

                if (updated)
                {
                    collectionModified++;
                }
            }
        }

        return new CollectionExecutionResult(collectionScanned, collectionModified, collectionRemoved, 0, context.RepairedReferences, null, BackupDisposition.None);
    }

    private async ValueTask<CollectionExecutionResult> ExecuteRebuildCollectionAsync(
        string collectionName,
        string selector,
        string migrationName,
        string runId,
        CollectionMigrationPlan plan,
        MigrationRunOptions options,
        IdRemapLog idRemapLog,
        CancellationToken cancellationToken)
    {
        ValidateConvertIdPolicy(plan.ConvertId);

        var sourceCollection = _database.GetCollection(collectionName);
        var shadowCollectionName = BuildShadowCollectionName(collectionName, runId);
        var backupCollectionName = BuildBackupCollectionName(collectionName, runId);

        if (await _database.CollectionExists(shadowCollectionName, cancellationToken).ConfigureAwait(false) ||
            await _database.CollectionExists(backupCollectionName, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Migration temporary collection name collision for '{collectionName}'.");
        }

        var shadowCollection = _database.GetCollection(shadowCollectionName);
        var indexes = await ReadIndexesAsync(collectionName, cancellationToken).ConfigureAwait(false);
        var remaps = new List<IdRemapEntry>();
        var scanned = 0;
        var modified = 0;
        var removed = 0;
        var generatedMappings = 0;
        var anyChanged = false;
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long ordinal = 0;
        var remapLookup = await LoadRemapLookupAsync(plan, cancellationToken).ConfigureAwait(false);
        var context = new DocumentMigrationExecutionContext(collectionName, migrationName, runId, remapLookup);

        await foreach (var sourceDocument in sourceCollection.Query().ToDocuments(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            scanned++;
            ordinal++;

            var document = BsonPathNavigator.CloneValue(sourceDocument).AsDocument;
            var preResult = ApplyDocumentOperations(document, context, plan.Operations, DocumentOperationStage.PreId);

            if (preResult.DeleteDocument)
            {
                removed++;
                anyChanged = true;
                continue;
            }

            document = preResult.Document ?? document;
            var idOutcome = ConvertDocumentId(document, collectionName, selector, migrationName, runId, shadowCollectionName, backupCollectionName, plan.ConvertId, ordinal);

            if (idOutcome.ShouldSkipDocument)
            {
                removed++;
                anyChanged = true;
                continue;
            }

            var changed = preResult.Changed || idOutcome.WasChanged;
            var postResult = ApplyDocumentOperations(document, context, plan.Operations, DocumentOperationStage.PostId);

            if (postResult.DeleteDocument)
            {
                removed++;
                anyChanged = true;
                continue;
            }

            document = postResult.Document ?? document;
            changed |= postResult.Changed;

            var targetId = document["_id"].ToString();

            if (!seenIds.Add(targetId))
            {
                throw new InvalidOperationException($"Duplicate target _id '{targetId}' generated during migration '{migrationName}' for collection '{collectionName}'.");
            }

            if (!options.DryRun)
            {
                await shadowCollection.Insert(document, cancellationToken).ConfigureAwait(false);
            }

            if (changed)
            {
                modified++;
                anyChanged = true;
            }

            if (idOutcome.RemapEntry != null)
            {
                remaps.Add(idOutcome.RemapEntry);
                generatedMappings++;
            }
        }

        if (!anyChanged)
        {
            if (!options.DryRun)
            {
                await _database.DropCollection(shadowCollectionName, cancellationToken).ConfigureAwait(false);
            }

            return new CollectionExecutionResult(scanned, 0, 0, 0, 0, null, BackupDisposition.None);
        }

        if (options.DryRun)
        {
            return new CollectionExecutionResult(scanned, modified, removed, generatedMappings, context.RepairedReferences, backupCollectionName, BackupDisposition.Planned);
        }

        foreach (var index in indexes.Where(x => !string.Equals(x.Name, "_id", StringComparison.OrdinalIgnoreCase)))
        {
            await shadowCollection.EnsureIndex(index.Name, BsonExpression.Create(index.Expression), index.Unique, cancellationToken).ConfigureAwait(false);
        }

        if (!await _database.RenameCollection(collectionName, backupCollectionName, cancellationToken).ConfigureAwait(false))
        {
            await _database.DropCollection(shadowCollectionName, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to rename source collection '{collectionName}' to '{backupCollectionName}'.");
        }

        if (!await _database.RenameCollection(shadowCollectionName, collectionName, cancellationToken).ConfigureAwait(false))
        {
            await _database.RenameCollection(backupCollectionName, collectionName, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Failed to rename shadow collection '{shadowCollectionName}' to '{collectionName}'.");
        }

        foreach (var remap in remaps)
        {
            await idRemapLog.WriteAsync(remap, cancellationToken).ConfigureAwait(false);
        }

        var backupDisposition = BackupDisposition.Kept;
        var reportBackupCollectionName = backupCollectionName;

        if (options.BackupRetentionPolicy == BackupRetentionPolicy.DeleteOnSuccess)
        {
            await _database.DropCollection(backupCollectionName, cancellationToken).ConfigureAwait(false);
            backupDisposition = BackupDisposition.Deleted;
            reportBackupCollectionName = null;
        }

        return new CollectionExecutionResult(scanned, modified, removed, generatedMappings, context.RepairedReferences, reportBackupCollectionName, backupDisposition);
    }

    private async ValueTask<IReadOnlyList<CollectionIndexDefinition>> ReadIndexesAsync(string collectionName, CancellationToken cancellationToken)
    {
        var indexes = new List<CollectionIndexDefinition>();
        var indexCollection = _database.GetCollection("$indexes");

        await foreach (var doc in indexCollection.Query().Where("collection = @0", new BsonValue(collectionName)).ToDocuments(cancellationToken).ConfigureAwait(false))
        {
            indexes.Add(new CollectionIndexDefinition(
                doc["name"].AsString,
                doc["expression"].AsString,
                doc["unique"].AsBoolean));
        }

        return indexes;
    }

    private async ValueTask<Dictionary<string, Dictionary<string, ObjectId>>> LoadRemapLookupAsync(CollectionMigrationPlan plan, CancellationToken cancellationToken)
    {
        var lookup = new Dictionary<string, Dictionary<string, ObjectId>>(StringComparer.OrdinalIgnoreCase);
        var remapAwareOperations = plan.Operations.OfType<IIdRemapAwareOperation>()
            .GroupBy(x => DocumentMigrationExecutionContext.BuildLookupKey(x.SourceCollection, x.SourceMigrationName), StringComparer.OrdinalIgnoreCase);

        if (!remapAwareOperations.Any())
        {
            return lookup;
        }

        var idRemapLog = new IdRemapLog(_database, _idMappingCollection);

        foreach (var group in remapAwareOperations)
        {
            var op = group.First();
            lookup[group.Key] = await idRemapLog.ReadMappingsAsync(op.SourceCollection, op.SourceMigrationName, cancellationToken).ConfigureAwait(false);
        }

        return lookup;
    }

    private static AppliedDocumentOperations ApplyDocumentOperations(BsonDocument document, DocumentMigrationExecutionContext context, IReadOnlyList<IDocumentMigrationOperation> operations, DocumentOperationStage stage)
    {
        var changed = false;
        var currentDocument = document;

        foreach (var operation in operations)
        {
            if (operation.Stage != stage)
            {
                continue;
            }

            var result = operation.Apply(currentDocument, context);

            switch (result.Kind)
            {
                case DocumentOperationKind.NoChange:
                    continue;
                case DocumentOperationKind.Updated:
                    changed = true;
                    currentDocument = result.Document ?? currentDocument;
                    continue;
                case DocumentOperationKind.DeleteDocument:
                    return new AppliedDocumentOperations(true, true, currentDocument);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return new AppliedDocumentOperations(changed, false, currentDocument);
    }

    private static void ValidateConvertIdPolicy(ConvertIdOperation operation)
    {
        switch (operation.InvalidPolicy)
        {
            case InvalidObjectIdPolicy.Fail:
            case InvalidObjectIdPolicy.SkipDocument:
            case InvalidObjectIdPolicy.GenerateNewId:
                return;
            default:
                throw new InvalidOperationException($"Invalid policy '{operation.InvalidPolicy}' for ConvertId(). Only Fail, SkipDocument, and GenerateNewId are supported for _id migration.");
        }
    }

    private static ConvertIdOutcome ConvertDocumentId(
        BsonDocument document,
        string collectionName,
        string selector,
        string migrationName,
        string runId,
        string shadowCollectionName,
        string backupCollectionName,
        ConvertIdOperation operation,
        long ordinal)
    {
        var id = document["_id"];

        if (id.IsObjectId)
        {
            return ConvertIdOutcome.Unchanged;
        }

        if (id.IsString && TryParseObjectId(id.AsString, out var parsed))
        {
            document["_id"] = new BsonValue(parsed);
            return ConvertIdOutcome.Changed;
        }

        switch (operation.InvalidPolicy)
        {
            case InvalidObjectIdPolicy.Fail:
                throw new InvalidOperationException($"Invalid _id value '{FormatRawId(id)}' in collection '{collectionName}' for migration '{migrationName}'.");
            case InvalidObjectIdPolicy.SkipDocument:
                return ConvertIdOutcome.Skip;
            case InvalidObjectIdPolicy.GenerateNewId:
                var newId = ObjectId.NewObjectId();
                document["_id"] = new BsonValue(newId);

                return new ConvertIdOutcome(true, false, new IdRemapEntry
                {
                    MigrationName = migrationName,
                    RunId = runId,
                    Collection = collectionName,
                    Selector = selector,
                    SourceCollection = collectionName,
                    ShadowCollection = shadowCollectionName,
                    BackupCollection = backupCollectionName,
                    OldIdRaw = FormatRawId(id),
                    OldIdType = id.Type.ToString(),
                    NewObjectId = newId,
                    Policy = operation.InvalidPolicy.ToString(),
                    Reason = "Generated new ObjectId during _id migration.",
                    DocumentOrdinal = ordinal
                });
            default:
                throw new InvalidOperationException($"Unsupported invalid _id policy '{operation.InvalidPolicy}'.");
        }
    }

    private static bool TryParseObjectId(string value, out ObjectId objectId)
    {
        try
        {
            objectId = new ObjectId(value);
            return true;
        }
        catch
        {
            objectId = null;
            return false;
        }
    }

    private static string BuildShadowCollectionName(string collectionName, string runId)
        => collectionName + "__migrating__" + ShortRunId(runId);

    private static string BuildBackupCollectionName(string collectionName, string runId)
        => collectionName + "__backup__" + ShortRunId(runId);

    private static string ShortRunId(string runId)
        => string.IsNullOrEmpty(runId) || runId.Length <= 8 ? runId : runId.Substring(0, 8);

    private static string FormatRawId(BsonValue id)
    {
        if (id == null || id.IsNull)
        {
            return null;
        }

        return id.IsString ? id.AsString : id.RawValue?.ToString();
    }

    private static bool TryParseBackupCollectionName(string collectionName, out string sourceCollection, out string runIdSuffix)
    {
        var markerIndex = collectionName.IndexOf("__backup__", StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
        {
            sourceCollection = null;
            runIdSuffix = null;
            return false;
        }

        sourceCollection = collectionName.Substring(0, markerIndex);
        runIdSuffix = collectionName.Substring(markerIndex + "__backup__".Length);

        return sourceCollection.Length > 0 && runIdSuffix.Length > 0;
    }

    private readonly struct CollectionExecutionResult
    {
        public CollectionExecutionResult(int documentsScanned, int documentsModified, int documentsRemoved, int generatedIdMappings, int repairedReferences, string backupCollectionName, BackupDisposition backupDisposition)
        {
            DocumentsScanned = documentsScanned;
            DocumentsModified = documentsModified;
            DocumentsRemoved = documentsRemoved;
            GeneratedIdMappings = generatedIdMappings;
            RepairedReferences = repairedReferences;
            BackupCollectionName = backupCollectionName;
            BackupDisposition = backupDisposition;
        }

        public int DocumentsScanned { get; }
        public int DocumentsModified { get; }
        public int DocumentsRemoved { get; }
        public int GeneratedIdMappings { get; }
        public int RepairedReferences { get; }
        public string BackupCollectionName { get; }
        public BackupDisposition BackupDisposition { get; }
    }

    private readonly struct AppliedDocumentOperations
    {
        public AppliedDocumentOperations(bool changed, bool deleteDocument, BsonDocument document)
        {
            Changed = changed;
            DeleteDocument = deleteDocument;
            Document = document;
        }

        public bool Changed { get; }
        public bool DeleteDocument { get; }
        public BsonDocument Document { get; }
    }

    private sealed class CollectionIndexDefinition
    {
        public CollectionIndexDefinition(string name, string expression, bool unique)
        {
            Name = name;
            Expression = expression;
            Unique = unique;
        }

        public string Name { get; }
        public string Expression { get; }
        public bool Unique { get; }
    }

    private sealed class ResolvedCollectionMigrationDefinition
    {
        public ResolvedCollectionMigrationDefinition(CollectionMigrationDefinition definition, IReadOnlyList<string> matchedCollections)
        {
            Definition = definition;
            MatchedCollections = matchedCollections;
        }

        public CollectionMigrationDefinition Definition { get; }

        public IReadOnlyList<string> MatchedCollections { get; }
    }

    private sealed class ConvertIdOutcome
    {
        public static readonly ConvertIdOutcome Unchanged = new(false, false, null);
        public static readonly ConvertIdOutcome Changed = new(true, false, null);
        public static readonly ConvertIdOutcome Skip = new(true, true, null);

        public ConvertIdOutcome(bool wasChanged, bool shouldSkipDocument, IdRemapEntry remapEntry)
        {
            WasChanged = wasChanged;
            ShouldSkipDocument = shouldSkipDocument;
            RemapEntry = remapEntry;
        }

        public bool WasChanged { get; }
        public bool ShouldSkipDocument { get; }
        public IdRemapEntry RemapEntry { get; }
    }
}

