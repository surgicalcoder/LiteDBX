using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Insert a new entity to this collection. Document Id must be a new value in collection - Returns document Id
    /// </summary>
    public async ValueTask<BsonValue> Insert(T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var doc = _mapper.ToDocument(entity);
        var removed = RemoveDocId(doc);

        await _engine.Insert(Name, new[] { doc }, AutoId, cancellationToken).ConfigureAwait(false);

        var id = doc["_id"];

        // checks if must update _id value in entity
        if (removed && _id != null)
        {
            _id.Setter(entity, id.RawValue);
        }

        return id;
    }

    /// <summary>
    /// Insert a new document to this collection using passed id value.
    /// </summary>
    public async ValueTask Insert(BsonValue id, T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (id == null || id.IsNull)
        {
            throw new ArgumentNullException(nameof(id));
        }

        var doc = _mapper.ToDocument(entity);

        doc["_id"] = id;

        await _engine.Insert(Name, new[] { doc }, AutoId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Insert an array of new documents to this collection. Document Id must be a new value in collection. Can be set buffer
    /// size to commit at each N documents
    /// </summary>
    public async ValueTask<int> Insert(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        return (int)await _engine.Insert(Name, GetBsonDocs(entities), AutoId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Implements bulk insert documents in a collection. Usefull when need lots of documents.
    /// </summary>
    [Obsolete("Use normal Insert()")]
    public async ValueTask<int> InsertBulk(IEnumerable<T> entities, int batchSize = 5000, CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        var count = 0;
        var batch = new List<T>(batchSize);

        foreach (var entity in entities)
        {
            batch.Add(entity);

            if (batch.Count >= batchSize)
            {
                count += (int)await _engine.Insert(Name, GetBsonDocs(batch), AutoId, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            count += (int)await _engine.Insert(Name, GetBsonDocs(batch), AutoId, cancellationToken).ConfigureAwait(false);
        }

        return count;
    }

    /// <summary>
    /// Convert each T document in a BsonDocument, setting autoId for each one
    /// </summary>
    private IEnumerable<BsonDocument> GetBsonDocs(IEnumerable<T> documents)
    {
        foreach (var document in documents)
        {
            var doc = _mapper.ToDocument(document);
            var removed = RemoveDocId(doc);

            yield return doc;

            if (removed && _id != null)
            {
                _id.Setter(document, doc["_id"].RawValue);
            }
        }
    }

    /// <summary>
    /// Remove document _id if contains a "empty" value (checks for autoId bson type)
    /// </summary>
    private bool RemoveDocId(BsonDocument doc)
    {
        if (_id != null && doc.TryGetValue("_id", out var id))
        {
            // check if exists _autoId and current id is "empty"
            if ((AutoId == BsonAutoId.Int32 && id.IsInt32 && id.AsInt32 == 0) ||
                (AutoId == BsonAutoId.ObjectId && (id.IsNull || (id.IsObjectId && id.AsObjectId == ObjectId.Empty))) ||
                (AutoId == BsonAutoId.Guid && id.IsGuid && id.AsGuid == Guid.Empty) ||
                (AutoId == BsonAutoId.Int64 && id.IsInt64 && id.AsInt64 == 0))
            {
                // in this cases, remove _id and set new value after
                doc.Remove("_id");

                return true;
            }
        }

        return false;
    }
}