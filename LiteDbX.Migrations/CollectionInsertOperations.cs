using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

public readonly struct InsertDocumentContext
{
    public InsertDocumentContext(string collectionName, string migrationName, int existingDocumentCount, bool existsById, BsonDocument document)
    {
        CollectionName = collectionName;
        MigrationName = migrationName;
        ExistingDocumentCount = existingDocumentCount;
        ExistsById = existsById;
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string CollectionName { get; }

    public string MigrationName { get; }

    public int ExistingDocumentCount { get; }

    public bool ExistsById { get; }

    public BsonDocument Document { get; }
}

public delegate bool InsertDocumentPredicate(InsertDocumentContext context);

public delegate BsonDocument BsonDocumentFactory(BsonDocumentMutationContext context);

internal interface ICollectionMigrationOperation
{
    ValueTask<CollectionOperationResult> ApplyAsync(ILiteCollection<BsonDocument> collection, CollectionOperationExecutionContext context, CancellationToken cancellationToken);
}

internal sealed class CollectionOperationExecutionContext
{
    private readonly HashSet<string> _knownDocumentIdKeys;

    public CollectionOperationExecutionContext(string collectionName, string migrationName, bool dryRun, ISet<string> knownDocumentIdKeys)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        MigrationName = migrationName ?? throw new ArgumentNullException(nameof(migrationName));
        DryRun = dryRun;
        _knownDocumentIdKeys = knownDocumentIdKeys as HashSet<string>
            ?? new HashSet<string>(knownDocumentIdKeys != null ? (System.Collections.Generic.IEnumerable<string>)knownDocumentIdKeys : Array.Empty<string>(), StringComparer.Ordinal);
        ExistingDocumentCount = _knownDocumentIdKeys.Count;
    }

    public string CollectionName { get; }

    public string MigrationName { get; }

    public bool DryRun { get; }

    public int ExistingDocumentCount { get; private set; }

    public BsonDocumentMutationContext ToPublicContext()
        => new(CollectionName, MigrationName);

    public bool ContainsId(BsonValue id)
    {
        var key = BuildDocumentIdKey(id);
        return key != null && _knownDocumentIdKeys.Contains(key);
    }

    public void RegisterInsertedDocument(BsonValue id)
    {
        var key = BuildDocumentIdKey(id);

        if (key != null)
        {
            _knownDocumentIdKeys.Add(key);
        }

        ExistingDocumentCount++;
    }

    internal static string BuildDocumentIdKey(BsonValue id)
    {
        if (id == null || id.IsNull)
        {
            return null;
        }

        var raw = id.IsString ? id.AsString : id.RawValue?.ToString();
        return DocumentMigrationExecutionContext.BuildIdKey(raw, id.Type);
    }
}

internal readonly struct CollectionOperationResult
{
    private CollectionOperationResult(int documentsInserted)
    {
        DocumentsInserted = documentsInserted;
    }

    public int DocumentsInserted { get; }

    public static CollectionOperationResult None => new(0);

    public static CollectionOperationResult Inserted(int count) => new(count);
}

internal sealed class InsertDocumentWhenOperation : ICollectionMigrationOperation
{
    private readonly BsonDocumentFactory _factory;
    private readonly InsertDocumentPredicate _when;

    public InsertDocumentWhenOperation(BsonDocumentFactory factory, InsertDocumentPredicate when)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _when = when ?? throw new ArgumentNullException(nameof(when));
    }

    public async ValueTask<CollectionOperationResult> ApplyAsync(ILiteCollection<BsonDocument> collection, CollectionOperationExecutionContext context, CancellationToken cancellationToken)
    {
        var candidate = _factory(context.ToPublicContext());

        if (candidate == null)
        {
            throw new InvalidOperationException($"InsertDocumentWhen returned null for migration '{context.MigrationName}' in collection '{context.CollectionName}'.");
        }

        var candidateDocument = BsonPathNavigator.CloneValue(candidate).AsDocument;
        var hasId = candidateDocument.TryGetValue("_id", out var candidateId) && candidateId != null && !candidateId.IsNull;
        var existsById = hasId && context.ContainsId(candidateId);
        var insertContext = new InsertDocumentContext(
            context.CollectionName,
            context.MigrationName,
            context.ExistingDocumentCount,
            existsById,
            BsonPathNavigator.CloneValue(candidateDocument).AsDocument);

        if (!_when(insertContext) || existsById)
        {
            return CollectionOperationResult.None;
        }

        if (context.DryRun)
        {
            context.RegisterInsertedDocument(hasId ? candidateId : BsonValue.Null);
            return CollectionOperationResult.Inserted(1);
        }

        var insertedId = await collection.Insert(candidateDocument, cancellationToken).ConfigureAwait(false);
        context.RegisterInsertedDocument(insertedId);
        return CollectionOperationResult.Inserted(1);
    }
}


