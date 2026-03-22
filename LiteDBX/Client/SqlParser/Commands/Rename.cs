using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// RENAME COLLECTION {collection} TO {newName}
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseRename(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("RENAME");
        _tokenizer.ReadToken().Expect("COLLECTION");

        var collection = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
        _tokenizer.ReadToken().Expect("TO");
        var newName = _tokenizer.ReadToken().Expect(TokenType.Word).Value;
        _tokenizer.ReadToken().Expect(TokenType.EOF, TokenType.SemiColon);

        var result = await _engine.RenameCollection(collection, newName, cancellationToken).ConfigureAwait(false);
        return new BsonDataReader(result);
    }
}