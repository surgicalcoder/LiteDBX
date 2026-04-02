namespace LiteDbX;

/// <summary>
/// Connection/access mode for opening a LiteDbX database.
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// Direct engine access.
    /// This is the normal fully capable mode and the mode that supports explicit
    /// <c>ILiteTransaction</c> scopes.
    /// </summary>
    Direct,

    /// <summary>
    /// Async-safe in-process serialized access.
    /// Supports nested single-call operations in the same async flow, but does not provide
    /// cross-process coordination and does not support explicit <c>ILiteTransaction</c> scope.
    /// </summary>
    Shared,

    /// <summary>
    /// Physical-file cross-process write-coordination mode using a lock file.
    /// Supported only for filename-based databases; does not support custom streams,
    /// <c>:memory:</c>, or <c>:temp:</c>, and does not support explicit
    /// <c>ILiteTransaction</c> scope.
    /// </summary>
    LockFile
}