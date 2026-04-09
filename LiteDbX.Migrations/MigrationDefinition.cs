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
    public CollectionMigrationDefinition(string selector, IReadOnlyList<IDocumentMigrationOperation> operations)
    {
        Selector = string.IsNullOrWhiteSpace(selector) ? throw new ArgumentNullException(nameof(selector)) : selector;
        Operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    public string Selector { get; }

    public IReadOnlyList<IDocumentMigrationOperation> Operations { get; }
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
    internal MigrationExecutionResult(string name, string runId, bool wasApplied, IReadOnlyList<CollectionSelectorResult> selectors, int documentsScanned, int documentsModified, int generatedIdMappings)
    {
        Name = name;
        RunId = runId;
        WasApplied = wasApplied;
        Selectors = selectors;
        DocumentsScanned = documentsScanned;
        DocumentsModified = documentsModified;
        GeneratedIdMappings = generatedIdMappings;
    }

    public string Name { get; }
    public string RunId { get; }
    public bool WasApplied { get; }
    public bool WasSkipped => !WasApplied;
    public IReadOnlyList<CollectionSelectorResult> Selectors { get; }
    public int DocumentsScanned { get; }
    public int DocumentsModified { get; }
    public int GeneratedIdMappings { get; }
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
    internal CollectionMigrationResult(string collectionName, int documentsScanned, int documentsModified)
    {
        CollectionName = collectionName;
        DocumentsScanned = documentsScanned;
        DocumentsModified = documentsModified;
    }

    public string CollectionName { get; }
    public int DocumentsScanned { get; }
    public int DocumentsModified { get; }
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

