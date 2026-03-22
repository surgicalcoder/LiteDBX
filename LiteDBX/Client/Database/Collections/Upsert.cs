using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Insert or Update a document in this collection.
    /// </summary>
    public async ValueTask<bool> Upsert(T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        return await Upsert(new[] { entity }, cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Insert or Update all documents
    /// </summary>
    public async ValueTask<int> Upsert(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        return (int)await _engine.Upsert(Name, GetBsonDocs(entities), AutoId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Insert or Update a document in this collection.
    /// </summary>
    public async ValueTask<bool> Upsert(BsonValue id, T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        if (id == null || id.IsNull)
        {
            throw new ArgumentNullException(nameof(id));
        }

        // get BsonDocument from object
        var doc = _mapper.ToDocument(entity);

        // set document _id using id parameter
        doc["_id"] = id;

        return await _engine.Upsert(Name, new[] { doc }, AutoId, cancellationToken).ConfigureAwait(false) > 0;
    }
}