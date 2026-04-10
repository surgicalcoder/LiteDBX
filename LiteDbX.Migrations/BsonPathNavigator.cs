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

    internal static bool HasWildcard(string path)
    {
        return TryParsePath(path, out var segments) && HasPattern(segments);
    }

    internal static bool HasRecursive(string path)
    {
        return TryParsePath(path, out var segments) && HasRecursive(segments);
    }

    internal static bool CanBindWildcardSiblingPath(string primaryPath, string siblingPath)
    {
        if (!TryParsePath(primaryPath, out var primarySegments) || !TryParsePath(siblingPath, out var siblingSegments))
        {
            return false;
        }

        if (!HasArrayWildcard(primarySegments) || !HasArrayWildcard(siblingSegments) || HasRecursive(primarySegments) || HasRecursive(siblingSegments) || primarySegments.Length != siblingSegments.Length || primarySegments.Length == 0)
        {
            return false;
        }

        for (var i = 0; i < primarySegments.Length - 1; i++)
        {
            if (!SegmentsMatchExactly(primarySegments[i], siblingSegments[i]))
            {
                return false;
            }
        }

        var primaryLeaf = primarySegments[primarySegments.Length - 1];
        var siblingLeaf = siblingSegments[siblingSegments.Length - 1];

        if (primaryLeaf.Kind != siblingLeaf.Kind)
        {
            return false;
        }

        return primaryLeaf.Kind != BsonPathSegmentKind.ArrayIndex || primaryLeaf.ArrayIndex == siblingLeaf.ArrayIndex;
    }

    internal static bool TryBindPath(string templatePath, string concretePath, out string boundPath)
    {
        boundPath = null;

        if (!TryParsePath(templatePath, out var templateSegments) || !TryParsePath(concretePath, out var concreteSegments) || templateSegments.Length != concreteSegments.Length)
        {
            return false;
        }

        var currentPath = string.Empty;

        for (var i = 0; i < templateSegments.Length; i++)
        {
            var templateSegment = templateSegments[i];
            var concreteSegment = concreteSegments[i];
            var isLeaf = i == templateSegments.Length - 1;

            switch (templateSegment.Kind)
            {
                case BsonPathSegmentKind.Property:
                    if (concreteSegment.Kind != BsonPathSegmentKind.Property)
                    {
                        return false;
                    }

                    if (!string.Equals(templateSegment.PropertyName, concreteSegment.PropertyName, StringComparison.OrdinalIgnoreCase) && !isLeaf)
                    {
                        return false;
                    }

                    currentPath = AppendSegment(currentPath, templateSegment);
                    break;
                case BsonPathSegmentKind.ArrayIndex:
                    if (concreteSegment.Kind != BsonPathSegmentKind.ArrayIndex || templateSegment.ArrayIndex != concreteSegment.ArrayIndex)
                    {
                        return false;
                    }

                    currentPath = AppendArrayIndex(currentPath, templateSegment.ArrayIndex);
                    break;
                case BsonPathSegmentKind.ArrayWildcard:
                    if (concreteSegment.Kind != BsonPathSegmentKind.ArrayIndex)
                    {
                        return false;
                    }

                    currentPath = AppendArrayIndex(currentPath, concreteSegment.ArrayIndex);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        boundPath = currentPath;
        return true;
    }

    internal static IReadOnlyList<BsonPredicateContext> CreateContexts(BsonDocument document, string path, bool includeLeafWhenMissing, string collection, string migrationName)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        if (!TryParsePath(path, out var segments))
        {
            return Array.Empty<BsonPredicateContext>();
        }

        if (!HasPattern(segments))
        {
            return new[]
            {
                CreateContext(document, path, collection, migrationName)
            };
        }

        var contexts = new List<BsonPredicateContext>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CreateContexts(document, document, segments, 0, string.Empty, includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
        return contexts;
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
            if (!SegmentsOverlap(candidateAncestor[i], candidateDescendant[i]))
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
                return segments.Length > 0 && ValidateSegments(segments);
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
        if (string.Equals(token, "**", StringComparison.Ordinal))
        {
            segments.Add(BsonPathSegment.ForRecursiveDescent());
            return true;
        }

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

            if (position < token.Length && token[position] == '*')
            {
                position++;

                if (position >= token.Length || token[position] != ']')
                {
                    return false;
                }

                segments.Add(BsonPathSegment.ForArrayWildcard());
                position++;
                continue;
            }

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

            if (segment.Kind == BsonPathSegmentKind.ArrayWildcard || segment.Kind == BsonPathSegmentKind.RecursiveDescent)
            {
                return false;
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

        if (leaf.Kind == BsonPathSegmentKind.ArrayWildcard || leaf.Kind == BsonPathSegmentKind.RecursiveDescent)
        {
            return false;
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

    private static void CreateContexts(BsonDocument root, BsonValue current, IReadOnlyList<BsonPathSegment> segments, int index, string currentPath, bool includeLeafWhenMissing, string collection, string migrationName, ISet<string> seenPaths, ICollection<BsonPredicateContext> contexts)
    {
        var segment = segments[index];
        var isLeaf = index == segments.Count - 1;

        if (segment.Kind == BsonPathSegmentKind.RecursiveDescent)
        {
            CreateContexts(root, current, segments, index + 1, currentPath, includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);

            if (current == null || current.IsNull)
            {
                return;
            }

            if (current.IsDocument)
            {
                foreach (var element in current.AsDocument)
                {
                    CreateContexts(root, element.Value, segments, index, AppendPropertyName(currentPath, element.Key), includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
                }

                return;
            }

            if (current.IsArray)
            {
                var recursiveArray = current.AsArray;

                for (var i = 0; i < recursiveArray.Count; i++)
                {
                    CreateContexts(root, recursiveArray[i], segments, index, AppendArrayIndex(currentPath, i), includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
                }
            }

            return;
        }

        if (segment.Kind == BsonPathSegmentKind.Property)
        {
            if (current == null || current.IsNull || !current.IsDocument)
            {
                return;
            }

            var path = AppendSegment(currentPath, segment);
            var document = current.AsDocument;

            if (isLeaf)
            {
                if (document.TryGetValue(segment.PropertyName, out var value))
                {
                    AddContext(path, new BsonPredicateContext(root, path, true, value, collection, migrationName), seenPaths, contexts);
                }
                else if (includeLeafWhenMissing)
                {
                    AddContext(path, BsonPredicateContext.Missing(root, path, collection, migrationName), seenPaths, contexts);
                }

                return;
            }

            if (document.TryGetValue(segment.PropertyName, out var child) && !child.IsNull)
            {
                CreateContexts(root, child, segments, index + 1, path, includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
            }

            return;
        }

        if (current == null || current.IsNull || !current.IsArray)
        {
            return;
        }

        var array = current.AsArray;

        if (segment.Kind == BsonPathSegmentKind.ArrayIndex)
        {
            if (segment.ArrayIndex < 0 || segment.ArrayIndex >= array.Count)
            {
                return;
            }

            var path = AppendArrayIndex(currentPath, segment.ArrayIndex);
            var value = array[segment.ArrayIndex];

            if (isLeaf)
            {
                AddContext(path, new BsonPredicateContext(root, path, true, value, collection, migrationName), seenPaths, contexts);
                return;
            }

            CreateContexts(root, value, segments, index + 1, path, includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
            return;
        }

        for (var i = 0; i < array.Count; i++)
        {
            var path = AppendArrayIndex(currentPath, i);
            var value = array[i];

            if (isLeaf)
            {
                AddContext(path, new BsonPredicateContext(root, path, true, value, collection, migrationName), seenPaths, contexts);
            }
            else
            {
                CreateContexts(root, value, segments, index + 1, path, includeLeafWhenMissing, collection, migrationName, seenPaths, contexts);
            }
        }
    }

    private static void AddContext(string path, BsonPredicateContext context, ISet<string> seenPaths, ICollection<BsonPredicateContext> contexts)
    {
        if (seenPaths.Add(path))
        {
            contexts.Add(context);
        }
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

    private static bool HasPattern(IReadOnlyList<BsonPathSegment> segments)
    {
        return HasArrayWildcard(segments) || HasRecursive(segments);
    }

    private static bool HasArrayWildcard(IReadOnlyList<BsonPathSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind == BsonPathSegmentKind.ArrayWildcard)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRecursive(IReadOnlyList<BsonPathSegment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind == BsonPathSegmentKind.RecursiveDescent)
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsOverlap(BsonPathSegment left, BsonPathSegment right)
    {
        if (left.Kind == BsonPathSegmentKind.RecursiveDescent || right.Kind == BsonPathSegmentKind.RecursiveDescent)
        {
            return left.Kind == right.Kind;
        }

        if (left.Kind == BsonPathSegmentKind.Property || right.Kind == BsonPathSegmentKind.Property)
        {
            return left.Kind == right.Kind && string.Equals(left.PropertyName, right.PropertyName, StringComparison.OrdinalIgnoreCase);
        }

        if (left.Kind == BsonPathSegmentKind.ArrayWildcard || right.Kind == BsonPathSegmentKind.ArrayWildcard)
        {
            return true;
        }

        return left.ArrayIndex == right.ArrayIndex;
    }

    private static bool SegmentsMatchExactly(BsonPathSegment left, BsonPathSegment right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        switch (left.Kind)
        {
            case BsonPathSegmentKind.Property:
                return string.Equals(left.PropertyName, right.PropertyName, StringComparison.OrdinalIgnoreCase);
            case BsonPathSegmentKind.ArrayIndex:
                return left.ArrayIndex == right.ArrayIndex;
            case BsonPathSegmentKind.ArrayWildcard:
                return true;
            case BsonPathSegmentKind.RecursiveDescent:
                return true;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool ValidateSegments(IReadOnlyList<BsonPathSegment> segments)
    {
        var recursiveCount = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Kind != BsonPathSegmentKind.RecursiveDescent)
            {
                continue;
            }

            recursiveCount++;

            if (recursiveCount > 1 || i == segments.Count - 1)
            {
                return false;
            }
        }

        return true;
    }

    private static string AppendSegment(string path, BsonPathSegment segment)
    {
        return segment.Kind == BsonPathSegmentKind.Property
            ? AppendPropertyName(path, segment.PropertyName)
            : AppendArrayIndex(path, segment.ArrayIndex);
    }

    private static string AppendPropertyName(string path, string propertyName)
    {
        return path.Length == 0 ? propertyName : path + "." + propertyName;
    }

    private static string AppendArrayIndex(string path, int index)
    {
        return path + "[" + index + "]";
    }


    private enum BsonPathSegmentKind
    {
        Property,
        ArrayIndex,
        ArrayWildcard,
        RecursiveDescent
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

        public static BsonPathSegment ForArrayWildcard() => new(BsonPathSegmentKind.ArrayWildcard, null, -1);

        public static BsonPathSegment ForRecursiveDescent() => new(BsonPathSegmentKind.RecursiveDescent, null, -1);

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

