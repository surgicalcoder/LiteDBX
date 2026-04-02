using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace LiteDbX.Engine;

internal class SysFile : SystemCollection
{
    private readonly Dictionary<string, SystemCollection> _formats = new(StringComparer.OrdinalIgnoreCase)
    {
        ["json"] = new SysFileJson(),
        ["csv"] = new SysFileCsv()
    };

    public SysFile() : base("$file") { }

    public override IAsyncEnumerable<BsonDocument> Input(BsonValue options, CancellationToken cancellationToken = default)
    {
        var format = GetFormat(options);

        if (_formats.TryGetValue(format, out var factory))
        {
            return factory.Input(options, cancellationToken);
        }

        throw new LiteException(0, $"Unknow file format in $file: `{format}`");
    }

    public override int Output(IEnumerable<BsonDocument> source, BsonValue options)
    {
        var format = GetFormat(options);

        if (_formats.TryGetValue(format, out var factory))
        {
            return factory.Output(source, options);
        }

        throw new LiteException(0, $"Unknow file format in $file: `{format}`");
    }

    private string GetFormat(BsonValue options)
    {
        var filename = GetOption(options, "filename")?.AsString ?? throw new LiteException(0, "Collection $file requires string as 'filename' or a document field 'filename'");
        var format = GetOption(options, "format", Path.GetExtension(filename)).AsString;

        return format.StartsWith(".") ? format.Substring(1) : format;
    }
}