using System;

namespace LiteDbX.Migrations;

public static class BsonPathNavigator
{
    public static bool TryGet(BsonDocument document, string path, out BsonDocument parent, out string fieldName, out BsonValue value)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        parent = document;
        value = BsonValue.Null;

        var segments = path.Split('.');

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (segment.Length == 0)
            {
                fieldName = string.Empty;
                parent = null;
                return false;
            }

            if (!parent.TryGetValue(segment, out var child) || child.IsNull || !child.IsDocument)
            {
                fieldName = segments[segments.Length - 1];
                parent = null;
                return false;
            }

            parent = child.AsDocument;
        }

        fieldName = segments[segments.Length - 1];

        if (fieldName.Length == 0)
        {
            parent = null;
            return false;
        }

        if (parent.TryGetValue(fieldName, out value))
        {
            return true;
        }

        value = BsonValue.Null;
        return false;
    }

    public static BsonPredicateContext CreateContext(BsonDocument document, string path, string collection, string migrationName)
    {
        return TryGet(document, path, out _, out _, out var value)
            ? new BsonPredicateContext(document, path, true, value, collection, migrationName)
            : BsonPredicateContext.Missing(document, path, collection, migrationName);
    }

    public static bool TryAdd(BsonDocument document, string path, BsonValue value, bool overwrite)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out _);

        if (parent == null)
        {
            return false;
        }

        if (exists && overwrite == false)
        {
            return false;
        }

        parent[fieldName] = value ?? BsonValue.Null;
        return true;
    }

    public static bool TryReplace(BsonDocument document, string path, BsonValue value)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out var current);

        if (!exists || parent == null)
        {
            return false;
        }

        value ??= BsonValue.Null;

        if (current == value)
        {
            return false;
        }

        parent[fieldName] = value;
        return true;
    }

    public static bool TryRemove(BsonDocument document, string path, bool pruneEmptyParents)
    {
        var exists = TryGet(document, path, out var parent, out var fieldName, out _);

        if (!exists || parent == null)
        {
            return false;
        }

        if (!parent.Remove(fieldName))
        {
            return false;
        }

        if (pruneEmptyParents)
        {
            PruneEmptyParents(document, path);
        }

        return true;
    }

    private static void PruneEmptyParents(BsonDocument document, string path)
    {
        var segments = path.Split('.');

        for (var depth = segments.Length - 1; depth > 0; depth--)
        {
            var parentPath = string.Join(".", segments, 0, depth);

            if (!TryGet(document, parentPath, out var parent, out var fieldName, out var value) || !value.IsDocument)
            {
                continue;
            }

            if (value.AsDocument.Count > 0)
            {
                break;
            }

            parent?.Remove(fieldName);
        }
    }
}

