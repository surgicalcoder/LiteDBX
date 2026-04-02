using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Engine;

public partial class LiteEngine
{
    /// <summary>
    /// Recover a corrupt datafile using the async rebuild process.
    /// Used by the explicit <c>LiteEngine.Open(...)</c> lifecycle.
    /// </summary>
    private async ValueTask Recovery(Collation collation, CancellationToken cancellationToken)
    {
        var rebuilder = new RebuildService(_settings);
        var options = new RebuildOptions
        {
            Collation = collation,
            Password = _settings.Password,
            IncludeErrorReport = true
        };

        await rebuilder.RebuildAsync(options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Recover a corrupt datafile using the rebuild process.
    /// Called only from <see cref="Open"/> when <c>AutoRebuild = true</c>.
    ///
    /// This sync overload is retained only for the legacy constructor-based startup path.
    ///
    /// Uses <see cref="RebuildService.Rebuild"/> (internal sync overload) rather than
    /// <see cref="RebuildService.RebuildAsync"/>. Do not call this from the explicit
    /// <c>LiteEngine.Open(...)</c> lifecycle.
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