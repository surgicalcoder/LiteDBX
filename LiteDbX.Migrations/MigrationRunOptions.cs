namespace LiteDbX.Migrations;

public sealed class MigrationRunOptions
{
    public bool DryRun { get; set; }

    public BackupRetentionPolicy BackupRetentionPolicy { get; set; } = BackupRetentionPolicy.KeepAll;

    internal MigrationRunOptions Clone()
    {
        return new MigrationRunOptions
        {
            DryRun = DryRun,
            BackupRetentionPolicy = BackupRetentionPolicy
        };
    }
}

public enum BackupRetentionPolicy
{
    KeepAll,
    DeleteOnSuccess
}

public enum BackupDisposition
{
    None,
    Planned,
    Kept,
    Deleted
}

public sealed class BackupCleanupOptions
{
    public bool DryRun { get; set; }

    public int? KeepLatestCount { get; set; }
}

public sealed class BackupCleanupReport
{
    internal BackupCleanupReport(System.Collections.Generic.IReadOnlyList<BackupCleanupResult> results)
    {
        Results = results;
    }

    public System.Collections.Generic.IReadOnlyList<BackupCleanupResult> Results { get; }

    public int TotalBackupsFound => Results.Count;

    public int TotalDeleted => System.Linq.Enumerable.Count(Results, x => x.Disposition == BackupCleanupDisposition.Deleted);

    public int TotalPlanned => System.Linq.Enumerable.Count(Results, x => x.Disposition == BackupCleanupDisposition.Planned);

    public int TotalRetained => System.Linq.Enumerable.Count(Results, x => x.Disposition == BackupCleanupDisposition.Retained);
}

public sealed class BackupCleanupResult
{
    internal BackupCleanupResult(string sourceCollection, string backupCollection, string runIdSuffix, System.DateTime? appliedUtc, BackupCleanupDisposition disposition)
    {
        SourceCollection = sourceCollection;
        BackupCollection = backupCollection;
        RunIdSuffix = runIdSuffix;
        AppliedUtc = appliedUtc;
        Disposition = disposition;
    }

    public string SourceCollection { get; }

    public string BackupCollection { get; }

    public string RunIdSuffix { get; }

    public System.DateTime? AppliedUtc { get; }

    public BackupCleanupDisposition Disposition { get; }
}

public enum BackupCleanupDisposition
{
    None,
    Retained,
    Planned,
    Deleted
}

