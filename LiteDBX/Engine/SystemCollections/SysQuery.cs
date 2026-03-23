using System;
using System.Collections.Generic;

namespace LiteDbX.Engine;

/// <summary>
/// <c>$query</c> experimental system collection that allows a sub-query via SQL.
///
/// Phase 6 status: <b>explicitly disabled</b>.
///
/// Reason: <see cref="SqlParser.Execute"/> now returns <c>ValueTask&lt;IBsonDataReader&gt;</c>
/// and <see cref="IBsonDataReader.Read"/> returns <c>ValueTask&lt;bool&gt;</c>, but
/// <see cref="SystemCollection.Input"/> must return the synchronous
/// <c>IEnumerable&lt;BsonDocument&gt;</c>. The two contracts are incompatible without
/// migrating the system-collection pipeline to <c>IAsyncEnumerable&lt;BsonDocument&gt;</c>,
/// which is a Phase 7 work item.
///
/// Deferred to Phase 7: redesign <see cref="SystemCollection"/> to support
/// <c>IAsyncEnumerable&lt;BsonDocument&gt;</c>, then re-enable <c>$query</c> using a
/// fully awaited <see cref="SqlParser.Execute"/> call.
/// </summary>
internal class SysQuery : SystemCollection
{
    /// <summary>
    /// The <paramref name="engine"/> parameter is kept for API compatibility with
    /// <see cref="Register.InitializeSystemCollections"/> but is not used by the
    /// disabled implementation.
    /// </summary>
    public SysQuery(ILiteEngine engine) : base("$query") { }

    /// <summary>
    /// Phase 6: always throws <see cref="NotSupportedException"/>.
    /// The async SQL execution path cannot be driven from a synchronous
    /// <c>IEnumerable</c> source. Re-enable after Phase 7 migrates system collections
    /// to <c>IAsyncEnumerable&lt;BsonDocument&gt;</c>.
    /// </summary>
    public override IEnumerable<BsonDocument> Input(BsonValue options)
    {
        throw new NotSupportedException(
            "The $query system collection is not supported in the async-only API. " +
            "Phase 7 will re-enable it after the system-collection pipeline is migrated " +
            "to IAsyncEnumerable<BsonDocument>.");
    }
}