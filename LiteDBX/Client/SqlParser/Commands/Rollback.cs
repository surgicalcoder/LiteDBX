namespace LiteDbX;

// Phase 4: SQL-level ROLLBACK TRANSACTION is no longer supported in the async-only API.
// See Begin.cs for explanation. Use ILiteTransaction.Rollback() / DisposeAsync() instead.
