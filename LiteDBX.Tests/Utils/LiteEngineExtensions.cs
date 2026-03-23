using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LiteDbX.Engine;

namespace LiteDbX.Tests;

/// <summary>
/// Async convenience wrappers over <see cref="ILiteEngine"/> for use in engine-level tests.
/// </summary>
public static class LiteEngineExtensions
{
    public static ValueTask<int> Insert(this LiteEngine engine, string collection, BsonDocument doc, BsonAutoId autoId = BsonAutoId.ObjectId)
    {
        return engine.Insert(collection, new[] { doc }, autoId);
    }

    public static ValueTask<int> Update(this LiteEngine engine, string collection, BsonDocument doc)
    {
        return engine.Update(collection, new[] { doc });
    }

    public static async Task<List<BsonDocument>> Find(this LiteEngine engine, string collection, BsonExpression where)
    {
        var q = new Query();

        if (where != null)
        {
            q.Where.Add(where);
        }

        var docs = new List<BsonDocument>();

        await foreach (var doc in engine.Query(collection, q))
        {
            docs.Add(doc);
        }

        return docs;
    }

    public static async Task<BsonDocument> GetPageLog(this LiteEngine engine, int pageID)
    {
        var results = await engine.Find($"$dump({pageID})", "1=1");
        return results.Last();
    }
}