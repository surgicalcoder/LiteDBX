using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

public sealed class MigrationRunner
{
    private readonly ILiteDatabase _database;
    private readonly List<MigrationDefinition> _migrations = new();
    private string _journalCollection = "__migrations";
    private string _idMappingCollection = "__migration_id_mappings";
    private bool _includeSystemCollections;

    public MigrationRunner(ILiteDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public MigrationRunner UseJournal(string collectionName)
    {
        _journalCollection = string.IsNullOrWhiteSpace(collectionName) ? throw new ArgumentNullException(nameof(collectionName)) : collectionName;
        return this;
    }

    public MigrationRunner UseIdMappingCollection(string collectionName)
    {
        _idMappingCollection = string.IsNullOrWhiteSpace(collectionName) ? throw new ArgumentNullException(nameof(collectionName)) : collectionName;
        return this;
    }

    public MigrationRunner IncludeSystemCollections(bool include = true)
    {
        _includeSystemCollections = include;
        return this;
    }

    public MigrationRunner Migration(string name, Action<MigrationBuilder> configure)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var builder = new MigrationBuilder();
        configure(builder);
        _migrations.Add(builder.Build(name));
        return this;
    }

    public async ValueTask<MigrationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var selector = new MigrationCollectionSelector(_journalCollection, _idMappingCollection, _includeSystemCollections);
        var history = new MigrationHistoryStore(_database, _journalCollection);
        var results = new List<MigrationExecutionResult>(_migrations.Count);

        foreach (var migration in _migrations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await history.IsAppliedAsync(migration.Name, cancellationToken).ConfigureAwait(false))
            {
                results.Add(new MigrationExecutionResult(migration.Name, runId: string.Empty, wasApplied: false, selectors: Array.Empty<CollectionSelectorResult>(), documentsScanned: 0, documentsModified: 0, generatedIdMappings: 0));
                continue;
            }

            var runId = Guid.NewGuid().ToString("N");
            var selectorResults = new List<CollectionSelectorResult>();
            var scanned = 0;
            var modified = 0;

            foreach (var definition in migration.Collections)
            {
                var matchedCollections = await selector.ResolveAsync(_database, definition.Selector, cancellationToken).ConfigureAwait(false);
                var collectionResults = new List<CollectionMigrationResult>(matchedCollections.Count);

                foreach (var collectionName in matchedCollections)
                {
                    var collection = _database.GetCollection(collectionName);
                    var collectionScanned = 0;
                    var collectionModified = 0;

                    await foreach (var document in collection.Query().ToDocuments(cancellationToken).ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        collectionScanned++;
                        scanned++;

                        var changed = false;

                        foreach (var operation in definition.Operations)
                        {
                            changed |= operation.Apply(document, collectionName, migration.Name);
                        }

                        if (!changed)
                        {
                            continue;
                        }

                        var updated = await collection.Update(document, cancellationToken).ConfigureAwait(false);

                        if (updated)
                        {
                            collectionModified++;
                            modified++;
                        }
                    }

                    collectionResults.Add(new CollectionMigrationResult(collectionName, collectionScanned, collectionModified));
                }

                selectorResults.Add(new CollectionSelectorResult(definition.Selector, matchedCollections, new ReadOnlyCollection<CollectionMigrationResult>(collectionResults)));
            }

            var result = new MigrationExecutionResult(
                migration.Name,
                runId,
                wasApplied: true,
                selectors: new ReadOnlyCollection<CollectionSelectorResult>(selectorResults),
                documentsScanned: scanned,
                documentsModified: modified,
                generatedIdMappings: 0);

            await history.MarkAppliedAsync(migration.Name, result, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new MigrationReport(new ReadOnlyCollection<MigrationExecutionResult>(results));
    }
}

