using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// PRAGMA [DB_PARAM] = VALUE
    /// PRAGMA [DB_PARAM]
    /// </summary>
    private async ValueTask<IBsonDataReader> ParsePragma(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("PRAGMA");

        var name = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
        var eof = _tokenizer.LookAhead();

        if (eof.Type == TokenType.EOF || eof.Type == TokenType.SemiColon)
        {
            _tokenizer.ReadToken();
            var result = await _engine.Pragma(name, cancellationToken).ConfigureAwait(false);
            return new BsonDataReader(result);
        }

        if (eof.Type == TokenType.Equals)
        {
            _tokenizer.ReadToken().Expect(TokenType.Equals);
            var value = new JsonReader(_tokenizer).Deserialize();
            _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

            var result = await _engine.Pragma(name, value, cancellationToken).ConfigureAwait(false);
            return new BsonDataReader(result);
        }

        throw LiteException.UnexpectedToken(eof);
    }
}