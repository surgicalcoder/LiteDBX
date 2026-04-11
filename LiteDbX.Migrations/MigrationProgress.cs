namespace LiteDbX.Migrations;

public enum MigrationProgressStage
{
    MigrationStarted,
    CollectionStarted,
    CollectionCompleted,
    MigrationCompleted
}

public sealed class MigrationProgress
{
    internal MigrationProgress(MigrationProgressStage stage, string migrationName, string selector, string collectionName, bool isDryRun, int completedCollections, int totalCollections, int documentsScanned, int documentsModified, int documentsRemoved, int documentsInserted)
    {
        Stage = stage;
        MigrationName = migrationName;
        Selector = selector;
        CollectionName = collectionName;
        IsDryRun = isDryRun;
        CompletedCollections = completedCollections;
        TotalCollections = totalCollections;
        DocumentsScanned = documentsScanned;
        DocumentsModified = documentsModified;
        DocumentsRemoved = documentsRemoved;
        DocumentsInserted = documentsInserted;
    }

    public MigrationProgressStage Stage { get; }

    public string MigrationName { get; }

    public string Selector { get; }

    public string CollectionName { get; }

    public bool IsDryRun { get; }

    public int CompletedCollections { get; }

    public int TotalCollections { get; }

    public int DocumentsScanned { get; }

    public int DocumentsModified { get; }

    public int DocumentsRemoved { get; }

    public int DocumentsInserted { get; }
}

