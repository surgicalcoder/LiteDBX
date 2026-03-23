using System.Collections.Generic;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    private IEnumerable<BsonDocument> SysIndexes()
    {
        // get the transaction that is currently driving this query context
        var transaction = _monitor.GetCurrentTransaction();

        foreach (var collection in _header.GetCollections())
        {
            var snapshot = transaction.CreateSnapshot(LockMode.Read, collection.Key, false);

            foreach (var index in snapshot.CollectionPage.GetCollectionIndexes())
            {
                yield return new BsonDocument
                {
                    ["collection"] = collection.Key,
                    ["name"] = index.Name,
                    ["expression"] = index.Expression,
                    ["unique"] = index.Unique
                };
            }
        }
    }
}