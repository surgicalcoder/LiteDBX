using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// UPDATE - update documents - if used with {key} = {exprValue} will merge current document with this fields
    /// if used with { key: value } will replace current document with new document
    /// UPDATE {collection}
    /// SET [{key} = {exprValue}, {key} = {exprValue} | { newDoc }]
    /// [ WHERE {whereExpr} ]
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseUpdate(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("UPDATE");

        var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
        _tokenizer.ReadToken().Expect("SET");

        var transform = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.UpdateDocument, _parameters);

        // optional where
        BsonExpression where = null;
        var token = _tokenizer.LookAhead();

        if (token.Is("WHERE"))
        {
            // read WHERE
            _tokenizer.ReadToken();

            where = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);
        }

        // read eof
        _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

        var result = await _engine.UpdateMany(collection, transform, where, cancellationToken).ConfigureAwait(false);

        return new BsonDataReader(result);
    }
}