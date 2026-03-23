using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Interface to read current or old datafile structure — used to shrink/upgrade a datafile
/// from old LiteDB versions.
///
/// Phase 6 note: The data-enumeration methods below (<see cref="Open"/>,
/// <see cref="GetCollections"/>, <see cref="GetIndexes"/>, <see cref="GetDocuments"/>)
/// remain synchronous because they perform bulk sequential reads from a dedicated
/// recovery stream that is isolated from the main engine I/O path. Making them
/// fully async would require streaming the file read through the async disk service,
/// which is a larger refactor scoped to a future phase.
///
/// <see cref="IAsyncDisposable"/> is implemented so the new async rebuild path can
/// use <c>await using</c> for clean resource release. The sync <see cref="IDisposable"/>
/// path is retained for the legacy engine-startup recovery that runs inside the
/// synchronous <c>Open()</c> constructor call (deferred to Phase 7 async factory).
/// </summary>
internal interface IFileReader : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Open and initialize file reader (run before any other command)
    /// </summary>
    void Open();

    /// <summary>
    /// Get all database pragma variables
    /// </summary>
    IDictionary<string, BsonValue> GetPragmas();

    /// <summary>
    /// Get all collections name from database
    /// </summary>
    IEnumerable<string> GetCollections();

    /// <summary>
    /// Get all indexes from collection (except _id index)
    /// </summary>
    IEnumerable<IndexInfo> GetIndexes(string name);

    /// <summary>
    /// Get all documents from a collection
    /// </summary>
    IEnumerable<BsonDocument> GetDocuments(string collection);
}