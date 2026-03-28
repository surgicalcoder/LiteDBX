using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Update a document in this collection. Returns false if not found document in collection
    /// </summary>
    public async ValueTask<bool> Update(T entity, CancellationToken cancellationToken = default)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        // get BsonDocument from object
        var doc = _mapper.ToDocument(entity);

        return await _engine.Update(Name, [doc], cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Update a document in this collection. Returns false if not found document in collection
    /// </summary>
    public async ValueTask<bool> Update(BsonValue id, T entity, CancellationToken cancellationToken = default)
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

        return await _engine.Update(Name, [doc], cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <summary>
    /// Update all documents
    /// </summary>
    public async ValueTask<int> Update(IEnumerable<T> entities, CancellationToken cancellationToken = default)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        return (int)await _engine.Update(Name, entities.Select(x => _mapper.ToDocument(x)), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Update many documents based on transform expression. This expression must return a new document that will be replaced
    /// over current document (according with predicate).
    /// Eg: col.UpdateMany("{ Name: UPPER($.Name), Age }", "_id > 0")
    /// </summary>
    public ValueTask<int> UpdateMany(BsonExpression transform, BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (transform == null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        if (transform.Type != BsonExpressionType.Document)
        {
            throw new ArgumentException("Extend expression must return a document. Eg: `col.UpdateMany('{ Name: UPPER(Name) }', 'Age > 10')`");
        }

        return _engine.UpdateMany(Name, transform, predicate, cancellationToken);
    }

    /// <summary>
    /// Update many document based on merge current document with extend expression. Use your class with initializers.
    /// Eg: col.UpdateMany(x => new Customer { Name = x.Name.ToUpper(), Salary: 100 }, x => x.Name == "John")
    /// </summary>
    public ValueTask<int> UpdateMany(Expression<Func<T, T>> extend, Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
    {
        if (extend == null)
        {
            throw new ArgumentNullException(nameof(extend));
        }

        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        var ext = _mapper.GetExpression(extend);
        var pred = _mapper.GetExpression(predicate);

        if (ext.Type != BsonExpressionType.Document)
        {
            throw new ArgumentException("Extend expression must return an anonymous class to be merge with entities. Eg: `col.UpdateMany(x => new { Name = x.Name.ToUpper() }, x => x.Age > 10)`");
        }

        return _engine.UpdateMany(Name, ext, pred, cancellationToken);
    }
}