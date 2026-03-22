namespace LiteDbX;

// Phase 4: SQL-level BEGIN TRANSACTION is no longer supported in the async-only API.
// The SqlParser.Execute switch throws NotSupportedException for BEGIN / COMMIT / ROLLBACK.
// Use ILiteDatabase.BeginTransaction() and ILiteTransaction.Commit() / DisposeAsync() instead.
// This file is intentionally otherwise empty; retained to document the removal decision.

