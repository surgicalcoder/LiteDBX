using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

/// <summary>
/// Implement a simple system collection with input data only (to use Output must inherit this class)
/// </summary>
internal class SystemCollection
{
    private readonly Func<CancellationToken, IAsyncEnumerable<BsonDocument>> _input;

    public SystemCollection(string name)
    {
        if (!name.StartsWith("$"))
        {
            throw new ArgumentException("System collection name must starts with $");
        }

        Name = name;
    }

    public SystemCollection(string name, Func<CancellationToken, IAsyncEnumerable<BsonDocument>> input)
        : this(name)
    {
        _input = input;
    }

    /// <summary>
    /// Get system collection name (must starts with $)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Get input data source factory
    /// </summary>
    public virtual IAsyncEnumerable<BsonDocument> Input(BsonValue options, CancellationToken cancellationToken = default)
    {
        return _input(cancellationToken);
    }

    /// <summary>
    /// Get output data source factory (must implement in inherit class)
    /// </summary>
    public virtual int Output(IEnumerable<BsonDocument> source, BsonValue options)
    {
        throw new LiteException(0, $"{Name} do not support as output collection");
    }

    /// <summary>
    /// Static helper to read options arg as plain value or as document fields
    /// </summary>
    protected static BsonValue GetOption(BsonValue options, string key)
    {
        return GetOption(options, key, null);
    }

    /// <summary>
    /// Static helper to read options arg as plain value or as document fields
    /// </summary>
    protected static BsonValue GetOption(BsonValue options, string key, BsonValue defaultValue)
    {
        if (options != null && options.IsDocument)
        {
            if (options.AsDocument.TryGetValue(key, out var value))
            {
                if (defaultValue == null || value.Type == defaultValue.Type)
                {
                    return value;
                }

                throw new LiteException(0, $"Parameter `{key}` expect {defaultValue.Type} value type");
            }

            return defaultValue;
        }

        return defaultValue == null ? options : defaultValue;
    }

    /// <summary>
    /// Wrap a synchronous document sequence in an async-compatible source contract.
    /// </summary>
    protected static async IAsyncEnumerable<BsonDocument> ToAsyncEnumerable(
        IEnumerable<BsonDocument> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var doc in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return doc;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Wrap an async BSON reader as an async sequence of documents.
    /// Scalar values are projected using <paramref name="projector"/>.
    /// </summary>
    protected static async IAsyncEnumerable<BsonDocument> ToAsyncEnumerable(
        IBsonDataReader reader,
        Func<BsonValue, BsonDocument> projector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using (reader)
        {
            while (await reader.Read(cancellationToken).ConfigureAwait(false))
            {
                yield return projector(reader.Current);
            }
        }
    }
}