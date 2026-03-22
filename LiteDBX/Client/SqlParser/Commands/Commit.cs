namespace LiteDbX;

// Phase 4: SQL-level COMMIT TRANSACTION is no longer supported in the async-only API.
// See Begin.cs for explanation. Use ILiteTransaction.Commit() / DisposeAsync() instead.
