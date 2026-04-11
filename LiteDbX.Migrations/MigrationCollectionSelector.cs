using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDbX.Migrations;

public sealed class MigrationCollectionSelector
{
    private readonly string _journalCollection;
    private readonly string _idMappingCollection;
    private readonly bool _includeSystemCollections;

    public MigrationCollectionSelector(string journalCollection, string idMappingCollection, bool includeSystemCollections)
    {
        _journalCollection = string.IsNullOrWhiteSpace(journalCollection) ? "__migrations" : journalCollection;
        _idMappingCollection = string.IsNullOrWhiteSpace(idMappingCollection) ? "__migration_id_mappings" : idMappingCollection;
        _includeSystemCollections = includeSystemCollections;
    }

    public async ValueTask<IReadOnlyList<string>> ResolveAsync(ILiteDatabase database, string selector, CancellationToken cancellationToken = default)
    {
        if (database == null) throw new ArgumentNullException(nameof(database));
        if (string.IsNullOrWhiteSpace(selector)) throw new ArgumentNullException(nameof(selector));

        var all = new List<string>();

        await foreach (var name in database.GetCollectionNames(cancellationToken).ConfigureAwait(false))
        {
            if (_includeSystemCollections == false && ShouldSkip(name))
            {
                continue;
            }

            if (IsMatch(selector, name))
            {
                all.Add(name);
            }
        }

        all.Sort(StringComparer.OrdinalIgnoreCase);

        return all;
    }

    public bool IsMatch(string selector, string collectionName)
    {
        if (selector == null) throw new ArgumentNullException(nameof(selector));
        if (collectionName == null) throw new ArgumentNullException(nameof(collectionName));

        if (selector == "*")
        {
            return true;
        }

        if (selector.IndexOf('*') < 0)
        {
            return string.Equals(selector, collectionName, StringComparison.OrdinalIgnoreCase);
        }

        var pattern = "^" + Regex.Escape(selector).Replace("\\*", ".*") + "$";

        return Regex.IsMatch(collectionName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private bool ShouldSkip(string name)
    {
        return name.StartsWith("$", StringComparison.Ordinal) ||
               name.IndexOf("__backup__", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("__migrating__", StringComparison.OrdinalIgnoreCase) >= 0 ||
               string.Equals(name, _journalCollection, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, _idMappingCollection, StringComparison.OrdinalIgnoreCase);
    }
}

