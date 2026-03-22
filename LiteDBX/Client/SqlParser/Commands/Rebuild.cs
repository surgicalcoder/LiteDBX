using LiteDbX.Engine;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

internal partial class SqlParser
{
    /// <summary>
    /// SHRINK
    /// </summary>
    private async ValueTask<IBsonDataReader> ParseRebuild(CancellationToken cancellationToken)
    {
        _tokenizer.ReadToken().Expect("REBUILD");

        RebuildOptions options = null;

        // read <eol> or ;
        var next = _tokenizer.LookAhead();

        if (next.Type == TokenType.EOF || next.Type == TokenType.SemiColon)
        {
            _tokenizer.ReadToken();
        }
        else
        {
            var reader = new JsonReader(_tokenizer);
            var json = reader.Deserialize();

            if (!json.IsDocument) throw LiteException.UnexpectedToken(next);

            options = new RebuildOptions();

            if (json["password"].IsString) options.Password = json["password"];
            if (json["collation"].IsString) options.Collation = new Collation(json["collation"].AsString);
        }

        var diff = await _engine.Rebuild(options, cancellationToken).ConfigureAwait(false);
        return new BsonDataReader((int)diff);
    }
}