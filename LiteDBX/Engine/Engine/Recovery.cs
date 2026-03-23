namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Recover a corrupt datafile using the rebuild process.
    /// Called only from <see cref="Open"/> when <c>AutoRebuild = true</c>.
    ///
    /// Phase 6 deferred: <c>Recovery()</c> is called synchronously from the
    /// <c>Open()</c> / constructor path and therefore cannot be made async without
    /// introducing a static async factory method (<c>LiteEngine.OpenAsync()</c>).
    /// That factory is deferred to Phase 7.
    ///
    /// Uses <see cref="RebuildService.Rebuild"/> (internal sync overload) rather than
    /// <see cref="RebuildService.RebuildAsync"/>. Do not change this to the async path
    /// until the constructor is replaced with an async factory.
    /// </summary>
    private void Recovery(Collation collation)
    {
        // run build service
        var rebuilder = new RebuildService(_settings);
        var options = new RebuildOptions
        {
            Collation = collation,
            Password = _settings.Password,
            IncludeErrorReport = true
        };

        // run rebuild process
        rebuilder.Rebuild(options);
    }
}