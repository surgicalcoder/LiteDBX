using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// DELETE {collection} WHERE {whereExpr}
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseDelete(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("DELETE");

        var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;

        BsonExpression where = null;

        if (_tokenizer.LookAhead().Is("WHERE"))
        {
            // read WHERE
            _tokenizer.ReadToken();

            where = BsonExpression.Create(_tokenizer, BsonExpressionParserMode.Full, _parameters);
        }

        _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

        var result = await _engine.DeleteMany(collection, where, cancellationToken).ConfigureAwait(false);

        return new BsonDataReader(result);
    }
}