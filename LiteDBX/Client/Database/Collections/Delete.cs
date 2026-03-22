using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

public partial class LiteCollection<T>
{
    /// <summary>
    /// Delete a single document on collection based on _id index. Returns true if document was deleted
    /// </summary>
    public async ValueTask<bool> Delete(BsonValue id, CancellationToken cancellationToken = default)
    {
        if (id == null || id.IsNull)
        {
            throw new ArgumentNullException(nameof(id));
        }

        return await _engine.Delete(Name, new[] { id }, cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <summary>
    /// Delete all documents inside collection. Returns how many documents was deleted. Run inside current transaction
    /// </summary>
    public ValueTask<int> DeleteAll(CancellationToken cancellationToken = default)
        => _engine.DeleteMany(Name, null, cancellationToken);

    /// <summary>
    /// Delete all documents based on predicate expression. Returns how many documents was deleted
    /// </summary>
    public ValueTask<int> DeleteMany(BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return _engine.DeleteMany(Name, predicate, cancellationToken);
    }

    /// <summary>
    /// Delete all documents based on predicate expression. Returns how many documents was deleted
    /// </summary>
    public ValueTask<int> DeleteMany(string predicate, BsonDocument parameters, CancellationToken cancellationToken = default)
        => DeleteMany(BsonExpression.Create(predicate, parameters), cancellationToken);

    /// <summary>
    /// Delete all documents based on predicate expression. Returns how many documents was deleted
    /// </summary>
    public ValueTask<int> DeleteMany(string predicate, params BsonValue[] args)
        => DeleteMany(BsonExpression.Create(predicate, args));

    /// <summary>
    /// Delete all documents based on predicate expression. Returns how many documents was deleted
    /// </summary>
    public ValueTask<int> DeleteMany(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken = default)
        => DeleteMany(_mapper.GetExpression(predicate), cancellationToken);
}