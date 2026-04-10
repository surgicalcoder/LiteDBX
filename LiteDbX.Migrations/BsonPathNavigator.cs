using System;
using System.Collections.Generic;

namespace LiteDbX.Migrations;

public static class BsonPathNavigator
{
    public static bool PathsConflict(string sourcePath, string targetPath)
    {
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!TryParsePath(sourcePath, out var sourceSegments) || !TryParsePath(targetPath, out var targetSegments))
        {
            return IsAncestorOrDescendant(sourcePath, targetPath) || IsAncestorOrDescendant(targetPath, sourcePath);
        }

        return IsAncestorOrDescendant(sourceSegments, targetSegments) || IsAncestorOrDescendant(targetSegments, sourceSegments);
    }

    public static bool TryGet(BsonDocument document, string path, out BsonDocument parent, out string fieldName, out BsonValue value)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        parent = null;
        fieldName = string.Empty;
        value = BsonValue.Null;

        if (!TryParsePath(path, out var segments) || !TryResolveTarget(document, segments, segments.Length, out var target))
        {
            return false;
        }

        parent = target.DocumentParent;
        fieldName = target.FieldName;
        value = target.Value;
        return target.Exists;
    }

    public static BsonPredicateContext CreateContext(BsonDocument document, string path, string collection, string migrationName)
    {
        return TryGet(document, path, out _, out _, out var value)
            ? new BsonPredicateContext(document, path, true, value, collection, migrationName)
            : BsonPredicateContext.Missing(document, path, collection, migrationName);
    }

    public static bool TryAdd(BsonDocument document, string path, BsonValue value, bool overwrite)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        if (!TryParsePath(path, out var segments) || !TryResolveTarget(document, segments, segments.Length, out var target) || !target.CanWrite)
        {
            return false;
        }

        if (target.Exists && overwrite == false)
        {
            return false;
        }

        return SetValue(target, value ?? BsonValue.Null);
    }

    public static bool TryReplace(BsonDocument document, string path, BsonValue value)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        if (!TryParsePath(path, out var segments) || !TryResolveTarget(document, segments, segments.Length, out var target) || !target.Exists)
        {
            return false;
        }

        value ??= BsonValue.Null;

        if (target.Value == value)
        {
            return false;
        }

        return SetValue(target, value);
    }

    public static bool TryRemove(BsonDocument document, string path, bool pruneEmptyParents)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        if (!TryParsePath(path, out var segments) || !TryResolveTarget(document, segments, segments.Length, out var target) || !target.Exists)
        {
            return false;
        }

        if (!RemoveValue(target))
        {
            return false;
        }

        if (pruneEmptyParents)
        {
            PruneEmptyParents(document, segments);
        }

        return true;
    }

    public static BsonValue CloneValue(BsonValue value)
    {
        if (value == null || value.IsNull)
        {
            return BsonValue.Null;
        }

        if (value.IsDocument)
        {
            var clone = new BsonDocument();

            foreach (var element in value.AsDocument)
            {
                clone[element.Key] = CloneValue(element.Value);
            }

            return clone;
        }

        if (value.IsArray)
        {
            var clone = new BsonArray();

            foreach (var item in value.AsArray)
            {
                clone.Add(CloneValue(item));
            }

            return clone;
        }

        if (value.IsBinary)
        {
            var bytes = value.AsBinary;
            var copy = new byte[bytes.Length];
            Array.Copy(bytes, copy, bytes.Length);
            return new BsonValue(copy);
        }

        switch (value.Type)
        {
            case BsonType.Int32:
                return new BsonValue(value.AsInt32);
            case BsonType.Int64:
                return new BsonValue(value.AsInt64);
            case BsonType.Double:
                return new BsonValue(value.AsDouble);
            case BsonType.Decimal:
                return new BsonValue(value.AsDecimal);
            case BsonType.String:
                return new BsonValue(value.AsString);
            case BsonType.ObjectId:
                return new BsonValue(new ObjectId(value.AsObjectId));
            case BsonType.Guid:
                return new BsonValue(value.AsGuid);
            case BsonType.Boolean:
                return new BsonValue(value.AsBoolean);
            case BsonType.DateTime:
                return new BsonValue(value.AsDateTime);
            case BsonType.Null:
                return BsonValue.Null;
            case BsonType.MinValue:
                return BsonValue.MinValue;
            case BsonType.MaxValue:
                return BsonValue.MaxValue;
            default:
                throw new NotSupportedException($"Unsupported BSON type '{value.Type}' for cloning.");
        }
    }

    private static void PruneEmptyParents(BsonDocument document, IReadOnlyList<BsonPathSegment> segments)
    {
        for (var depth = segments.Count - 1; depth > 0; depth--)
        {
            if (!TryResolveTarget(document, segments, depth, out var target) || !target.Exists || !IsEmptyContainer(target.Value))
            {
                continue;
            }

            if (!RemoveValue(target))
            {
                break;
            }
        }
    }

    private static bool IsAncestorOrDescendant(string candidateAncestor, string candidateDescendant)
    {
        if (candidateAncestor.Length >= candidateDescendant.Length)
        {
            return false;
        }

        return candidateDescendant.StartsWith(candidateAncestor + ".", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAncestorOrDescendant(IReadOnlyList<BsonPathSegment> candidateAncestor, IReadOnlyList<BsonPathSegment> candidateDescendant)
    {
        if (candidateAncestor.Count >= candidateDescendant.Count)
        {
            return false;
        }

        for (var i = 0; i < candidateAncestor.Count; i++)
        {
            if (!candidateAncestor[i].Equals(candidateDescendant[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParsePath(string path, out BsonPathSegment[] segments)
    {
        segments = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var parsed = new List<BsonPathSegment>();
        var position = 0;

        while (position < path.Length)
        {
            var nextSeparator = path.IndexOf('.', position);
            var tokenLength = (nextSeparator < 0 ? path.Length : nextSeparator) - position;

            if (tokenLength <= 0 || !TryParseToken(path.Substring(position, tokenLength), parsed))
            {
                return false;
            }

            if (nextSeparator < 0)
            {
                segments = parsed.ToArray();
                return segments.Length > 0;
            }

            position = nextSeparator + 1;

            if (position >= path.Length)
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryParseToken(string token, ICollection<BsonPathSegment> segments)
    {
        var firstBracket = token.IndexOf('[');

        if (firstBracket < 0)
        {
            if (token.Length == 0)
            {
                return false;
            }

            segments.Add(BsonPathSegment.ForProperty(token));
            return true;
        }

        var propertyName = token.Substring(0, firstBracket);

        if (propertyName.Length == 0)
        {
            return false;
        }

        segments.Add(BsonPathSegment.ForProperty(propertyName));

        var position = firstBracket;

        while (position < token.Length)
        {
            if (token[position] != '[')
            {
                return false;
            }

            position++;

            if (position >= token.Length || !char.IsDigit(token[position]))
            {
                return false;
            }

            var start = position;

            while (position < token.Length && char.IsDigit(token[position]))
            {
                position++;
            }

            if (position >= token.Length || token[position] != ']')
            {
                return false;
            }

            if (!int.TryParse(token.Substring(start, position - start), out var index))
            {
                return false;
            }

            segments.Add(BsonPathSegment.ForArrayIndex(index));
            position++;
        }

        return true;
    }

    private static bool TryResolveTarget(BsonDocument document, IReadOnlyList<BsonPathSegment> segments, int segmentCount, out BsonPathTarget target)
    {
        target = default;

        if (segments == null || segmentCount <= 0 || segmentCount > segments.Count)
        {
            return false;
        }

        BsonValue current = document;

        for (var i = 0; i < segmentCount - 1; i++)
        {
            var segment = segments[i];

            if (segment.Kind == BsonPathSegmentKind.Property)
            {
                if (current == null || current.IsNull || !current.IsDocument)
                {
                    return false;
                }

                var parent = current.AsDocument;

                if (!parent.TryGetValue(segment.PropertyName, out current) || current.IsNull)
                {
                    return false;
                }

                continue;
            }

            if (current == null || current.IsNull || !current.IsArray)
            {
                return false;
            }

            var array = current.AsArray;

            if (segment.ArrayIndex < 0 || segment.ArrayIndex >= array.Count)
            {
                return false;
            }

            current = array[segment.ArrayIndex];
        }

        var leaf = segments[segmentCount - 1];

        if (leaf.Kind == BsonPathSegmentKind.Property)
        {
            if (current == null || current.IsNull || !current.IsDocument)
            {
                return false;
            }

            var parent = current.AsDocument;

            if (parent.TryGetValue(leaf.PropertyName, out var value))
            {
                target = BsonPathTarget.ForProperty(parent, leaf.PropertyName, true, value);
                return true;
            }

            target = BsonPathTarget.ForProperty(parent, leaf.PropertyName, false, BsonValue.Null);
            return true;
        }

        if (current == null || current.IsNull || !current.IsArray)
        {
            return false;
        }

        var arrayParent = current.AsArray;
        var exists = leaf.ArrayIndex >= 0 && leaf.ArrayIndex < arrayParent.Count;
        var currentValue = exists ? arrayParent[leaf.ArrayIndex] : BsonValue.Null;
        target = BsonPathTarget.ForArrayIndex(arrayParent, leaf.ArrayIndex, exists, currentValue);
        return true;
    }

    private static bool SetValue(BsonPathTarget target, BsonValue value)
    {
        if (target.IsArrayIndex)
        {
            if (target.ArrayParent == null || target.ArrayIndex < 0 || target.ArrayIndex >= target.ArrayParent.Count)
            {
                return false;
            }

            target.ArrayParent[target.ArrayIndex] = value;
            return true;
        }

        if (target.DocumentParent == null || target.FieldName.Length == 0)
        {
            return false;
        }

        target.DocumentParent[target.FieldName] = value;
        return true;
    }

    private static bool RemoveValue(BsonPathTarget target)
    {
        if (target.IsArrayIndex)
        {
            if (target.ArrayParent == null || target.ArrayIndex < 0 || target.ArrayIndex >= target.ArrayParent.Count)
            {
                return false;
            }

            target.ArrayParent.RemoveAt(target.ArrayIndex);
            return true;
        }

        return target.DocumentParent != null && target.FieldName.Length > 0 && target.DocumentParent.Remove(target.FieldName);
    }

    private static bool IsEmptyContainer(BsonValue value)
    {
        if (value == null || value.IsNull)
        {
            return false;
        }

        if (value.IsDocument)
        {
            return value.AsDocument.Count == 0;
        }

        if (value.IsArray)
        {
            return value.AsArray.Count == 0;
        }

        return false;
    }

    private enum BsonPathSegmentKind
    {
        Property,
        ArrayIndex
    }

    private readonly struct BsonPathSegment : IEquatable<BsonPathSegment>
    {
        private BsonPathSegment(BsonPathSegmentKind kind, string propertyName, int arrayIndex)
        {
            Kind = kind;
            PropertyName = propertyName;
            ArrayIndex = arrayIndex;
        }

        public BsonPathSegmentKind Kind { get; }

        public string PropertyName { get; }

        public int ArrayIndex { get; }

        public static BsonPathSegment ForProperty(string propertyName) => new(BsonPathSegmentKind.Property, propertyName, -1);

        public static BsonPathSegment ForArrayIndex(int arrayIndex) => new(BsonPathSegmentKind.ArrayIndex, null, arrayIndex);

        public bool Equals(BsonPathSegment other)
        {
            if (Kind != other.Kind)
            {
                return false;
            }

            return Kind == BsonPathSegmentKind.Property
                ? string.Equals(PropertyName, other.PropertyName, StringComparison.OrdinalIgnoreCase)
                : ArrayIndex == other.ArrayIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is BsonPathSegment other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Kind == BsonPathSegmentKind.Property
                ? StringComparer.OrdinalIgnoreCase.GetHashCode(PropertyName)
                : ArrayIndex.GetHashCode();
        }
    }

    private readonly struct BsonPathTarget
    {
        private BsonPathTarget(BsonDocument documentParent, string fieldName, BsonArray arrayParent, int arrayIndex, bool exists, BsonValue value)
        {
            DocumentParent = documentParent;
            FieldName = fieldName ?? string.Empty;
            ArrayParent = arrayParent;
            ArrayIndex = arrayIndex;
            Exists = exists;
            Value = value ?? BsonValue.Null;
        }

        public BsonDocument DocumentParent { get; }

        public string FieldName { get; }

        public BsonArray ArrayParent { get; }

        public int ArrayIndex { get; }

        public bool Exists { get; }

        public BsonValue Value { get; }

        public bool IsArrayIndex => ArrayParent != null;

        public bool CanWrite => IsArrayIndex ? Exists : DocumentParent != null;

        public static BsonPathTarget ForProperty(BsonDocument documentParent, string fieldName, bool exists, BsonValue value)
            => new(documentParent, fieldName, null, -1, exists, value);

        public static BsonPathTarget ForArrayIndex(BsonArray arrayParent, int arrayIndex, bool exists, BsonValue value)
            => new(null, string.Empty, arrayParent, arrayIndex, exists, value);
    }
}

