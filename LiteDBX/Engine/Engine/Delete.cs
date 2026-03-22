using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Implements delete based on IDs enumerable
    /// </summary>
    public ValueTask<int> Delete(string collection, IEnumerable<BsonValue> ids, CancellationToken cancellationToken = default)
    {
        if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));
        if (ids == null) throw new ArgumentNullException(nameof(ids));

        return AutoTransactionAsync(async (transaction, ct) =>
        {
            var snapshot = await transaction.CreateSnapshotAsync(LockMode.Write, collection, false, ct).ConfigureAwait(false);
            var collectionPage = snapshot.CollectionPage;
            var data = new DataService(snapshot, _disk.MAX_ITEMS_COUNT);
            var indexer = new IndexService(snapshot, _header.Pragmas.Collation, _disk.MAX_ITEMS_COUNT);

            if (collectionPage == null) return 0;

            LOG($"delete `{collection}`", "COMMAND");

            var count = 0;
            var pk = collectionPage.PK;

            foreach (var id in ids)
            {
                _state.Validate();
                transaction.Safepoint();

                var pkNode = indexer.Find(pk, id, false, LiteDbX.Query.Ascending);
                if (pkNode == null) continue;

                // delete all index nodes
                indexer.DeleteAll(pkNode.Position);

                // remove object data
                data.Delete(collectionPage, pkNode.DataBlock);

                count++;
            }

            return count;
        }, cancellationToken);
    }

    /// <summary>
    /// Implements delete based on filter expression
    /// </summary>
    public async ValueTask<int> DeleteMany(string collection, BsonExpression predicate, CancellationToken cancellationToken = default)
    {
        if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException(nameof(collection));

        var ids = new List<BsonValue>();
        var q = new Query();
        if (predicate != null) q.Where.Add(predicate);

        var executor = new QueryExecutor(this, _state, _monitor, _sortDisk, _disk, _header.Pragmas, collection, q, null);

        await foreach (var doc in executor.ExecuteQuery(cancellationToken).ConfigureAwait(false))
        {
            ids.Add(doc["_id"]);
        }

        return await Delete(collection, ids, cancellationToken).ConfigureAwait(false);
    }
}