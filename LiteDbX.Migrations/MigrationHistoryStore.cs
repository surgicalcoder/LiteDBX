using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

internal sealed class MigrationHistoryStore
{
    private readonly ILiteCollection<BsonDocument> _collection;

    public MigrationHistoryStore(ILiteDatabase database, string collectionName)
    {
        if (database == null) throw new ArgumentNullException(nameof(database));
        if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        _collection = database.GetCollection(collectionName, BsonAutoId.ObjectId);
    }

    public async ValueTask<bool> IsAppliedAsync(string migrationName, CancellationToken cancellationToken = default)
    {
        return await _collection.Exists(BsonExpression.Create("_id = @0", new BsonValue(migrationName)), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask MarkAppliedAsync(string migrationName, MigrationExecutionResult result, CancellationToken cancellationToken = default)
    {
        if (migrationName == null) throw new ArgumentNullException(nameof(migrationName));
        if (result == null) throw new ArgumentNullException(nameof(result));

        var doc = new BsonDocument
        {
            ["_id"] = new BsonValue(migrationName),
            ["name"] = new BsonValue(migrationName),
            ["appliedUtc"] = new BsonValue(DateTime.UtcNow),
            ["wasApplied"] = new BsonValue(result.WasApplied),
            ["documentsScanned"] = new BsonValue(result.DocumentsScanned),
            ["documentsModified"] = new BsonValue(result.DocumentsModified),
            ["generatedIdMappings"] = new BsonValue(result.GeneratedIdMappings),
            ["runId"] = new BsonValue(result.RunId)
        };

        await _collection.Upsert(new BsonValue(migrationName), doc, cancellationToken).ConfigureAwait(false);
    }
}

