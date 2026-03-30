using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX;

/// <summary>
/// Async terminal extensions for provider-backed LiteDbX <see cref="IQueryable{T}"/> queries.
///
/// These extensions preserve the Phase 1 contract: synchronous LINQ composition with async-only execution.
/// Each terminal lowers the provider-backed query into a fresh native <see cref="Query"/> /
/// <see cref="LiteQueryable{T}"/> and then delegates to the existing LiteDbX query engine path.
/// </summary>
public static class LiteDbXQueryableAsyncExtensions
{
    public static IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.ToAsyncEnumerable).ToEnumerable(cancellationToken);

    public static ValueTask<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.ToList).ToList(cancellationToken);

    public static ValueTask<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.ToArray).ToArray(cancellationToken);

    public static ValueTask<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.First).First(cancellationToken);

    public static ValueTask<T> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.FirstOrDefault).FirstOrDefault(cancellationToken);

    public static ValueTask<T> SingleAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.Single).Single(cancellationToken);

    public static ValueTask<T> SingleOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.SingleOrDefault).SingleOrDefault(cancellationToken);

    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.Any).Exists(cancellationToken);

    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.Count).Count(cancellationToken);

    public static ValueTask<long> LongCountAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.LongCount).LongCount(cancellationToken);

    /// <summary>
    /// Execute the translated provider-backed query in explain-plan mode and return the native execution plan document.
    /// This preserves query-plan visibility without requiring callers to abandon the LINQ surface prematurely.
    /// </summary>
    public static ValueTask<BsonDocument> GetPlanAsync<T>(this IQueryable<T> source, CancellationToken cancellationToken = default)
        => LowerToNativeQueryable(source, LiteDbXQueryTerminalKind.GetPlan).GetPlan(cancellationToken);

    private static LiteQueryable<T> LowerToNativeQueryable<T>(IQueryable<T> source, LiteDbXQueryTerminalKind terminalKind)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        if (source.Provider is not LiteDbXQueryProvider provider)
        {
            throw new ArgumentException("The supplied IQueryable is not backed by the LiteDbX LINQ provider.", nameof(source));
        }

        var state = provider.Translate(source.Expression).WithTerminal(terminalKind, typeof(T));

        return provider.LowerToNativeQueryable<T>(state);
    }
}

