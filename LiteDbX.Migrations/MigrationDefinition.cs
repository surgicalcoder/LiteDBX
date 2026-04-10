using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace LiteDbX.Migrations;

internal sealed class MigrationDefinition
{
    public MigrationDefinition(string name, IReadOnlyList<CollectionMigrationDefinition> collections)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentNullException(nameof(name)) : name;
        Collections = collections ?? throw new ArgumentNullException(nameof(collections));
    }

    public string Name { get; }

    public IReadOnlyList<CollectionMigrationDefinition> Collections { get; }
}

internal sealed class CollectionMigrationDefinition
{
    public CollectionMigrationDefinition(string selector, CollectionMigrationPlan plan)
    {
        Selector = string.IsNullOrWhiteSpace(selector) ? throw new ArgumentNullException(nameof(selector)) : selector;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public string Selector { get; }

    public CollectionMigrationPlan Plan { get; }
}

public sealed class MigrationReport
{
    internal MigrationReport(IReadOnlyList<MigrationExecutionResult> migrations)
    {
        Migrations = migrations;
    }

    public IReadOnlyList<MigrationExecutionResult> Migrations { get; }
}

public sealed class MigrationExecutionResult
{
    internal MigrationExecutionResult(string name, string runId, bool wasApplied, bool isDryRun, IReadOnlyList<CollectionSelectorResult> selectors, int documentsScanned, int documentsModified, int documentsRemoved, int generatedIdMappings, int repairedReferences, int invalidValueCount)
    {
        Name = name;
        RunId = runId;
        WasApplied = wasApplied;
        IsDryRun = isDryRun;
        Selectors = selectors;
        DocumentsScanned = documentsScanned;
        DocumentsModified = documentsModified;
        DocumentsRemoved = documentsRemoved;
        GeneratedIdMappings = generatedIdMappings;
        RepairedReferences = repairedReferences;
        InvalidValueCount = invalidValueCount;
    }

    public string Name { get; }
    public string RunId { get; }
    public bool WasApplied { get; }
    public bool IsDryRun { get; }
    public bool WasSkipped => !WasApplied && !IsDryRun;
    public IReadOnlyList<CollectionSelectorResult> Selectors { get; }
    public int DocumentsScanned { get; }
    public int DocumentsModified { get; }
    public int DocumentsRemoved { get; }
    public int GeneratedIdMappings { get; }
    public int RepairedReferences { get; }
    public int InvalidValueCount { get; }
}

public sealed class CollectionSelectorResult
{
    internal CollectionSelectorResult(string selector, IReadOnlyList<string> matchedCollections, IReadOnlyList<CollectionMigrationResult> collections)
    {
        Selector = selector;
        MatchedCollections = matchedCollections;
        Collections = collections;
    }

    public string Selector { get; }
    public IReadOnlyList<string> MatchedCollections { get; }
    public IReadOnlyList<CollectionMigrationResult> Collections { get; }
    public bool IsUnmatched => MatchedCollections.Count == 0;
}

public sealed class CollectionMigrationResult
{
    internal CollectionMigrationResult(string collectionName, int documentsScanned, int documentsModified, int documentsRemoved, int generatedIdMappings, int repairedReferences, int invalidValueCount, IReadOnlyList<InvalidValueSample> invalidValueSamples, RebuildValidationSummary rebuildValidation, string backupCollectionName, BackupDisposition backupDisposition)
    {
        CollectionName = collectionName;
        DocumentsScanned = documentsScanned;
        DocumentsModified = documentsModified;
        DocumentsRemoved = documentsRemoved;
        GeneratedIdMappings = generatedIdMappings;
        RepairedReferences = repairedReferences;
        InvalidValueCount = invalidValueCount;
        InvalidValueSamples = invalidValueSamples ?? Array.Empty<InvalidValueSample>();
        RebuildValidation = rebuildValidation;
        BackupCollectionName = backupCollectionName;
        BackupDisposition = backupDisposition;
    }

    public string CollectionName { get; }
    public int DocumentsScanned { get; }
    public int DocumentsModified { get; }
    public int DocumentsRemoved { get; }
    public int GeneratedIdMappings { get; }
    public int RepairedReferences { get; }
    public int InvalidValueCount { get; }
    public IReadOnlyList<InvalidValueSample> InvalidValueSamples { get; }
    public RebuildValidationSummary RebuildValidation { get; }
    public string BackupCollectionName { get; }
    public BackupDisposition BackupDisposition { get; }
}

public sealed class RebuildValidationSummary
{
    internal RebuildValidationSummary(int sourceDocumentCount, int expectedTargetDocumentCount, int preparedTargetDocumentCount, int secondaryIndexesToReplayCount, IReadOnlyList<SecondaryIndexReplayPlan> secondaryIndexesToReplay, int duplicateTargetIdCount, IReadOnlyList<DuplicateTargetIdSample> duplicateTargetIdSamples)
    {
        SourceDocumentCount = sourceDocumentCount;
        ExpectedTargetDocumentCount = expectedTargetDocumentCount;
        PreparedTargetDocumentCount = preparedTargetDocumentCount;
        SecondaryIndexesToReplayCount = secondaryIndexesToReplayCount;
        SecondaryIndexesToReplay = secondaryIndexesToReplay ?? Array.Empty<SecondaryIndexReplayPlan>();
        DuplicateTargetIdCount = duplicateTargetIdCount;
        DuplicateTargetIdSamples = duplicateTargetIdSamples ?? Array.Empty<DuplicateTargetIdSample>();
    }

    public int SourceDocumentCount { get; }
    public int ExpectedTargetDocumentCount { get; }
    public int PreparedTargetDocumentCount { get; }
    public int SecondaryIndexesToReplayCount { get; }
    public IReadOnlyList<SecondaryIndexReplayPlan> SecondaryIndexesToReplay { get; }
    public int DuplicateTargetIdCount { get; }
    public IReadOnlyList<DuplicateTargetIdSample> DuplicateTargetIdSamples { get; }
}

public sealed class SecondaryIndexReplayPlan
{
    internal SecondaryIndexReplayPlan(string name, string expression, bool unique)
    {
        Name = name;
        Expression = expression;
        Unique = unique;
    }

    public string Name { get; }
    public string Expression { get; }
    public bool Unique { get; }
}

public sealed class DuplicateTargetIdSample
{
    internal DuplicateTargetIdSample(string targetId, string firstSourceIdRaw, string firstSourceIdType, long firstSourceDocumentOrdinal, string duplicateSourceIdRaw, string duplicateSourceIdType, long duplicateSourceDocumentOrdinal)
    {
        TargetId = targetId;
        FirstSourceIdRaw = firstSourceIdRaw;
        FirstSourceIdType = firstSourceIdType;
        FirstSourceDocumentOrdinal = firstSourceDocumentOrdinal;
        DuplicateSourceIdRaw = duplicateSourceIdRaw;
        DuplicateSourceIdType = duplicateSourceIdType;
        DuplicateSourceDocumentOrdinal = duplicateSourceDocumentOrdinal;
    }

    public string TargetId { get; }
    public string FirstSourceIdRaw { get; }
    public string FirstSourceIdType { get; }
    public long FirstSourceDocumentOrdinal { get; }
    public string DuplicateSourceIdRaw { get; }
    public string DuplicateSourceIdType { get; }
    public long DuplicateSourceDocumentOrdinal { get; }
}

public sealed class InvalidValueSample
{
    internal InvalidValueSample(string path, string rawValue, string valueType, string reason)
    {
        Path = path;
        RawValue = rawValue;
        ValueType = valueType;
        Reason = reason;
    }

    public string Path { get; }
    public string RawValue { get; }
    public string ValueType { get; }
    public string Reason { get; }
}

public sealed class MigrationBuilder
{
    private readonly List<CollectionMigrationDefinition> _collections = new();

    public MigrationBuilder ForCollection(string selector, Action<CollectionMigrationBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentNullException(nameof(selector));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var builder = new CollectionMigrationBuilder();
        configure(builder);

        _collections.Add(new CollectionMigrationDefinition(selector, builder.Build()));

        return this;
    }

    internal MigrationDefinition Build(string name)
    {
        return new MigrationDefinition(name, new ReadOnlyCollection<CollectionMigrationDefinition>(_collections.ToList()));
    }
}

