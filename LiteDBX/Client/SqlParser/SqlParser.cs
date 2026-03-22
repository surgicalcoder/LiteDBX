using System;
using System.Threading;
using System.Threading.Tasks;
using LiteDbX.Engine;
using static LiteDbX.Constants;

namespace LiteDbX;

/// <summary>
/// Internal class to parse and execute sql-like commands.
///
/// Phase 4: <see cref="Execute"/> now returns <c>ValueTask&lt;IBsonDataReader&gt;</c> and accepts
/// a <see cref="CancellationToken"/>. The collation is pre-resolved by the caller (e.g.
/// <see cref="LiteDatabase"/>) so that the lazy sync Pragma call is replaced by an async await
/// before SqlParser construction.
/// </summary>
internal partial class SqlParser
{
    private readonly Collation _collation;
    private readonly ILiteEngine _engine;
    private readonly BsonDocument _parameters;
    private readonly Tokenizer _tokenizer;

    public SqlParser(ILiteEngine engine, Tokenizer tokenizer, BsonDocument parameters, Collation collation)
    {
        _engine = engine;
        _tokenizer = tokenizer;
        _parameters = parameters ?? new BsonDocument();
        _collation = collation;
    }

    public async ValueTask<IBsonDataReader> Execute(CancellationToken cancellationToken = default)
    {
        var ahead = _tokenizer.LookAhead().Expect(TokenType.Word);

        LOG($"executing `{ahead.Value.ToUpper()}`", "SQL");

        switch (ahead.Value.ToUpper())
        {
            case "SELECT":
            case "EXPLAIN":
                return ParseSelect(cancellationToken);

            case "INSERT":  return await ParseInsert(cancellationToken).ConfigureAwait(false);
            case "DELETE":  return await ParseDelete(cancellationToken).ConfigureAwait(false);
            case "UPDATE":  return await ParseUpdate(cancellationToken).ConfigureAwait(false);
            case "DROP":    return await ParseDrop(cancellationToken).ConfigureAwait(false);
            case "RENAME":  return await ParseRename(cancellationToken).ConfigureAwait(false);
            case "CREATE":  return await ParseCreate(cancellationToken).ConfigureAwait(false);

            case "CHECKPOINT": return await ParseCheckpoint(cancellationToken).ConfigureAwait(false);
            case "REBUILD":    return await ParseRebuild(cancellationToken).ConfigureAwait(false);

            case "BEGIN":
            case "COMMIT":
            case "ROLLBACK":
                throw new NotSupportedException(
                    $"SQL-level {ahead.Value.ToUpper()} TRANSACTION is not supported in the async-only API. " +
                    "Use ILiteDatabase.BeginTransaction() / ILiteTransaction.Commit() / ILiteTransaction.Rollback() instead.");

            case "PRAGMA":  return await ParsePragma(cancellationToken).ConfigureAwait(false);

            default: throw LiteException.UnexpectedToken(ahead);
        }
    }
}