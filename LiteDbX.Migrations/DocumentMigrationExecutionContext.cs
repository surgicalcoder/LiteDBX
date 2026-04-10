using System;
using System.Collections.Generic;

namespace LiteDbX.Migrations;

internal sealed class DocumentMigrationExecutionContext
{
    private const int MaxInvalidValueSamples = 5;

    private readonly Dictionary<string, Dictionary<string, ObjectId>> _remapLookup;
    private readonly List<InvalidValueSample> _invalidValueSamples = new();
    private readonly bool _captureInvalidValueSamples;
    private int _repairedReferences;
    private int _invalidValueCount;

    public DocumentMigrationExecutionContext(string collectionName, string migrationName, string runId, Dictionary<string, Dictionary<string, ObjectId>> remapLookup, bool captureInvalidValueSamples)
    {
        CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
        MigrationName = migrationName ?? throw new ArgumentNullException(nameof(migrationName));
        RunId = runId ?? throw new ArgumentNullException(nameof(runId));
        _remapLookup = remapLookup ?? new Dictionary<string, Dictionary<string, ObjectId>>(StringComparer.OrdinalIgnoreCase);
        _captureInvalidValueSamples = captureInvalidValueSamples;
    }

    public string CollectionName { get; }

    public string MigrationName { get; }

    public string RunId { get; }

    public int RepairedReferences => _repairedReferences;

    public int InvalidValueCount => _invalidValueCount;

    public IReadOnlyList<InvalidValueSample> InvalidValueSamples => _invalidValueSamples;

    public bool TryGetRemappedObjectId(string sourceCollection, string sourceMigrationName, string oldIdRaw, BsonType oldIdType, out ObjectId objectId)
    {
        objectId = null;

        if (string.IsNullOrWhiteSpace(sourceCollection) || string.IsNullOrEmpty(oldIdRaw))
        {
            return false;
        }

        if (!_remapLookup.TryGetValue(BuildLookupKey(sourceCollection, sourceMigrationName), out var mappings))
        {
            return false;
        }

        return mappings.TryGetValue(BuildIdKey(oldIdRaw, oldIdType), out objectId);
    }

    public BsonDocumentMutationContext ToPublicContext()
    {
        return new BsonDocumentMutationContext(CollectionName, MigrationName);
    }

    public void IncrementRepairedReferenceCount()
    {
        _repairedReferences++;
    }

    public void RecordInvalidValue(string path, BsonValue value, string reason)
    {
        _invalidValueCount++;

        if (!_captureInvalidValueSamples || _invalidValueSamples.Count >= MaxInvalidValueSamples)
        {
            return;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;

        foreach (var sample in _invalidValueSamples)
        {
            if (string.Equals(sample.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sample.RawValue, FormatRawValue(value), StringComparison.Ordinal) &&
                string.Equals(sample.Reason, reason, StringComparison.Ordinal))
            {
                return;
            }
        }

        _invalidValueSamples.Add(new InvalidValueSample(normalizedPath, FormatRawValue(value), value?.Type.ToString(), reason));
    }

    public static string BuildLookupKey(string sourceCollection, string sourceMigrationName)
    {
        return (sourceCollection ?? string.Empty) + "\u001f" + (sourceMigrationName ?? string.Empty);
    }

    public static string BuildIdKey(string oldIdRaw, BsonType oldIdType)
    {
        return oldIdType + "\u001f" + (oldIdRaw ?? string.Empty);
    }

    private static string FormatRawValue(BsonValue value)
    {
        if (value == null || value.IsNull)
        {
            return null;
        }

        return value.IsString ? value.AsString : value.RawValue?.ToString();
    }
}

