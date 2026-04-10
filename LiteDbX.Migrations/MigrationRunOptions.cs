namespace LiteDbX.Migrations;

public sealed class MigrationRunOptions
{
    private bool _dryRun;
    private BackupRetentionPolicy _backupRetentionPolicy = BackupRetentionPolicy.KeepAll;
    private bool _strictPathResolution;
    private System.Action<MigrationProgress> _progressCallback;

    internal bool HasDryRunSetting { get; private set; }

    internal bool HasBackupRetentionPolicySetting { get; private set; }

    internal bool HasStrictPathResolutionSetting { get; private set; }

    internal bool HasProgressCallbackSetting { get; private set; }

    public bool DryRun
    {
        get => _dryRun;
        set
        {
            _dryRun = value;
            HasDryRunSetting = true;
        }
    }

    public BackupRetentionPolicy BackupRetentionPolicy
    {
        get => _backupRetentionPolicy;
        set
        {
            _backupRetentionPolicy = value;
            HasBackupRetentionPolicySetting = true;
        }
    }

    public bool StrictPathResolution
    {
        get => _strictPathResolution;
        set
        {
            _strictPathResolution = value;
            HasStrictPathResolutionSetting = true;
        }
    }

    public System.Action<MigrationProgress> ProgressCallback
    {
        get => _progressCallback;
        set
        {
            _progressCallback = value;
            HasProgressCallbackSetting = true;
        }
    }

    internal MigrationRunOptions Clone()
    {
        var clone = new MigrationRunOptions();

        if (HasDryRunSetting)
        {
            clone.DryRun = DryRun;
        }

        if (HasBackupRetentionPolicySetting)
        {
            clone.BackupRetentionPolicy = BackupRetentionPolicy;
        }

        if (HasStrictPathResolutionSetting)
        {
            clone.StrictPathResolution = StrictPathResolution;
        }

        if (HasProgressCallbackSetting)
        {
            clone.ProgressCallback = ProgressCallback;
        }

        return clone;
    }

    internal static MigrationRunOptions Merge(MigrationRunOptions defaults, MigrationRunOptions overrides)
    {
        var merged = defaults?.Clone() ?? new MigrationRunOptions();

        if (overrides == null)
        {
            return merged;
        }

        if (overrides.HasDryRunSetting)
        {
            merged.DryRun = overrides.DryRun;
        }

        if (overrides.HasBackupRetentionPolicySetting)
        {
            merged.BackupRetentionPolicy = overrides.BackupRetentionPolicy;
        }

        if (overrides.HasStrictPathResolutionSetting)
        {
            merged.StrictPathResolution = overrides.StrictPathResolution;
        }

        if (overrides.HasProgressCallbackSetting)
        {
            merged.ProgressCallback = overrides.ProgressCallback;
        }

        return merged;
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

