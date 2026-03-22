using LiteDbX.Engine;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// CHECKPOINT
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseCheckpoint(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect(Pragmas.CHECKPOINT);
        _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

        var result = await _engine.Checkpoint(cancellationToken).ConfigureAwait(false);
        return new BsonDataReader(result);
    }
}