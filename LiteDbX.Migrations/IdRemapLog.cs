using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

public sealed class IdRemapLog
{
    private readonly ILiteCollection<BsonDocument> _collection;

    public IdRemapLog(ILiteDatabase database, string collectionName)
    {
        if (database == null) throw new ArgumentNullException(nameof(database));
        if (string.IsNullOrWhiteSpace(collectionName)) throw new ArgumentNullException(nameof(collectionName));

        _collection = database.GetCollection(collectionName, BsonAutoId.ObjectId);
    }

    public ValueTask<BsonValue> WriteAsync(IdRemapEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));

        var doc = new BsonDocument
        {
            ["migrationName"] = new BsonValue(entry.MigrationName),
            ["runId"] = new BsonValue(entry.RunId),
            ["collection"] = new BsonValue(entry.Collection),
            ["selector"] = new BsonValue(entry.Selector),
            ["sourceCollection"] = new BsonValue(entry.SourceCollection),
            ["shadowCollection"] = new BsonValue(entry.ShadowCollection),
            ["backupCollection"] = new BsonValue(entry.BackupCollection),
            ["oldIdRaw"] = new BsonValue(entry.OldIdRaw),
            ["oldIdType"] = new BsonValue(entry.OldIdType),
            ["newObjectId"] = new BsonValue(entry.NewObjectId),
            ["policy"] = new BsonValue(entry.Policy),
            ["reason"] = new BsonValue(entry.Reason),
            ["documentOrdinal"] = new BsonValue(entry.DocumentOrdinal),
            ["createdUtc"] = new BsonValue(entry.CreatedUtc)
        };

        return _collection.Insert(doc, cancellationToken);
    }

    public async ValueTask<Dictionary<string, ObjectId>> ReadMappingsAsync(string sourceCollection, string sourceMigrationName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCollection)) throw new ArgumentNullException(nameof(sourceCollection));

        var mappings = new Dictionary<string, ObjectId>(StringComparer.Ordinal);
        var query = _collection.Query().Where("sourceCollection = @0", new BsonValue(sourceCollection));

        if (!string.IsNullOrWhiteSpace(sourceMigrationName))
        {
            query = query.Where("migrationName = @0", new BsonValue(sourceMigrationName));
        }

        await foreach (var doc in query.ToDocuments(cancellationToken).ConfigureAwait(false))
        {
            var type = (BsonType)Enum.Parse(typeof(BsonType), doc["oldIdType"].AsString);
            var key = DocumentMigrationExecutionContext.BuildIdKey(doc["oldIdRaw"].AsString, type);
            mappings[key] = doc["newObjectId"].AsObjectId;
        }

        return mappings;
    }
}

public sealed class IdRemapEntry
{
    public string MigrationName { get; set; }
    public string RunId { get; set; }
    public string Collection { get; set; }
    public string Selector { get; set; }
    public string SourceCollection { get; set; }
    public string ShadowCollection { get; set; }
    public string BackupCollection { get; set; }
    public string OldIdRaw { get; set; }
    public string OldIdType { get; set; }
    public ObjectId NewObjectId { get; set; }
    public string Policy { get; set; }
    public string Reason { get; set; }
    public long DocumentOrdinal { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

