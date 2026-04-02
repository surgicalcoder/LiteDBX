using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static LiteDbX.Constants;

namespace LiteDbX.Engine;

/// <summary>
/// <c>$query</c> system collection that allows a sub-query via SQL.
/// </summary>
internal class SysQuery : SystemCollection
{
    private readonly ILiteEngine _engine;

    public SysQuery(ILiteEngine engine) : base("$query")
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    /// <summary>
    /// Execute a nested SQL query and stream the result as system-collection documents.
    ///
    /// Options may be either a plain SQL string or a document with:
    /// - <c>query</c> / <c>sql</c>: SQL text
    /// - <c>params</c> / <c>parameters</c>: parameter document
    /// </summary>
    public override IAsyncEnumerable<BsonDocument> Input(BsonValue options, CancellationToken cancellationToken = default)
        => ExecuteQuery(options, cancellationToken);

    private async ValueTask<IBsonDataReader> CreateReader(BsonValue options, CancellationToken cancellationToken)
    {
        var query = ResolveQuery(options);
        var parameters = ResolveParameters(options);

        var collation = new Collation((await _engine.Pragma(Pragmas.COLLATION, cancellationToken).ConfigureAwait(false)).AsString);
        var sql = new SqlParser(_engine, new Tokenizer(query), parameters, collation);

        return await sql.Execute(cancellationToken).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<BsonDocument> ExecuteQuery(BsonValue options, CancellationToken cancellationToken)
    {
        var reader = await CreateReader(options, cancellationToken).ConfigureAwait(false);

        await foreach (var doc in ToAsyncEnumerable(
                           reader,
                           current => current.IsDocument ? current.AsDocument : new BsonDocument { ["expr"] = current },
                           cancellationToken).ConfigureAwait(false))
        {
            yield return doc;
        }
    }

    private static string ResolveQuery(BsonValue options)
    {
        if (options == null || options.IsNull)
        {
            throw new LiteException(0, "Collection $query requires a SQL string or a document field 'query'.");
        }

        if (options.IsString)
        {
            return options.AsString;
        }

        if (options.IsDocument)
        {
            var query = GetOption(options, "query") ?? GetOption(options, "sql");

            if (query != null && query.IsString)
            {
                return query.AsString;
            }
        }

        throw new LiteException(0, "Collection $query requires a SQL string or a document field 'query'.");
    }

    private static BsonDocument ResolveParameters(BsonValue options)
    {
        if (options?.IsDocument != true)
        {
            return null;
        }

        var parameters = GetOption(options, "params") ?? GetOption(options, "parameters");

        return parameters != null && parameters.IsDocument ? parameters.AsDocument : null;
    }
}