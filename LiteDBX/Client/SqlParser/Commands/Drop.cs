using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// DROP INDEX {collection}.{indexName}
    /// DROP COLLECTION {collection}
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseDrop(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("DROP");
        var token = _tokenizer.ReadToken().Expect(TokenType.Word);

        if (token.Is("INDEX"))
        {
            var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
            _tokenizer.ReadToken().Expect(TokenType.Period);
            var name = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
            _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

            var result = await _engine.DropIndex(collection, name, cancellationToken).ConfigureAwait(false);
            return new BsonDataReader(result);
        }

        if (token.Is("COLLECTION"))
        {
            var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
            _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

            var result = await _engine.DropCollection(collection, cancellationToken).ConfigureAwait(false);
            return new BsonDataReader(result);
        }

        throw LiteException.UnexpectedToken(token, "INDEX|COLLECTION");
    }
}