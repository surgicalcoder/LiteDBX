using System;
using System.Collections.Generic;

namespace LiteDbX.Migrations;

public static class BsonPathNavigator
{
    internal static string DescribeFailure(BsonPathResolutionFailure failure)
    {
        return failure switch
        {
            BsonPathResolutionFailure.None => "No path resolution failure occurred.",
            BsonPathResolutionFailure.InvalidPath => "The supplied path is invalid.",
            BsonPathResolutionFailure.MissingIntermediate => "An intermediate path segment is missing.",
            BsonPathResolutionFailure.MissingLeaf => "The target field or array element is missing.",
            BsonPathResolutionFailure.ExpectedDocument => "A document segment was expected but a different value was encountered.",
            BsonPathResolutionFailure.ExpectedArray => "An array segment was expected but a different value was encountered.",
            BsonPathResolutionFailure.UnsupportedPattern => "Wildcard or recursive paths are not supported for this direct navigation operation.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        };
    }

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

    internal static IReadOnlyList<BsonPredicateContext> CreateCleanupContexts(BsonDocument document, CleanupScope scope, string collection, string migrationName)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));

        var contexts = new List<BsonPredicateContext>();

        foreach (var element in document)
        {
            if (string.Equals(element.Key, "_id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var path = element.Key;

            if (scope == CleanupScope.TopLevel)
            {
                contexts.Add(new BsonPredicateContext(document, path, true, element.Value, collection, migrationName));
                continue;
            }

            CreateCleanupContexts(document, element.Value, path, collection, migrationName, contexts);
        }

        return contexts;
    }

    public static bool TryGet(BsonDocument document, string path, out BsonDocument parent, out string fieldName, out BsonValue value)
        => TryGet(document, path, out parent, out fieldName, out value, out _);

    internal static bool TryGet(BsonDocument document, string path, out BsonDocument parent, out string fieldName, out BsonValue value, out BsonPathResolutionFailure failure)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        parent = null;
        fieldName = string.Empty;
        value = BsonValue.Null;
        failure = BsonPathResolutionFailure.None;

        if (!TryParsePath(path, out var segments))
        {
            failure = BsonPathResolutionFailure.InvalidPath;
            return false;
        }

        if (!TryResolveTarget(document, segments, segments.Length, createParents: false, out var target, out failure))
        {
            return false;
        }

        parent = target.DocumentParent;
        fieldName = target.FieldName;
        value = target.Value;
        return target.Exists;
    }

    public static BsonPredicateContext CreateContext(BsonDocument document, string path, string collection, string migrationName)
        => CreateContext(document, path, collection, migrationName, out _);

    internal static BsonPredicateContext CreateContext(BsonDocument document, string path, string collection, string migrationName, out BsonPathResolutionFailure failure)
    {
        return TryGet(document, path, out _, out _, out var value, out failure)
            ? new BsonPredicateContext(document, path, true, value, collection, migrationName)
            : BsonPredicateContext.Missing(document, path, collection, migrationName);
    }

    public static bool TryAdd(BsonDocument document, string path, BsonValue value, bool overwrite)
        => TryAdd(document, path, value, overwrite ? FieldWriteMode.Overwrite : FieldWriteMode.MissingOnly, createParents: false);

    internal static bool TryAdd(BsonDocument document, string path, BsonValue value, bool overwrite, out BsonPathResolutionFailure failure)
        => TryAdd(document, path, value, overwrite ? FieldWriteMode.Overwrite : FieldWriteMode.MissingOnly, createParents: false, out failure);

    public static bool TryAdd(BsonDocument document, string path, BsonValue value, FieldWriteMode writeMode, bool createParents)
        => TryAdd(document, path, value, writeMode, createParents, out _);

    internal static bool TryAdd(BsonDocument document, string path, BsonValue value, FieldWriteMode writeMode, bool createParents, out BsonPathResolutionFailure failure)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        failure = BsonPathResolutionFailure.None;

        if (!TryParsePath(path, out var segments))
        {
            failure = BsonPathResolutionFailure.InvalidPath;
            return false;
        }

        if (!TryResolveTarget(document, segments, segments.Length, createParents, out var target, out failure) || !target.CanWrite)
        {
            return false;
        }

        value ??= BsonValue.Null;

        if (!CanWrite(target, writeMode))
        {
            return false;
        }

        if (target.Exists && target.Value == value)
        {
            return false;
        }

        return SetValue(target, value);
    }

    public static bool TryReplace(BsonDocument document, string path, BsonValue value)
        => TryReplace(document, path, value, out _);

    internal static bool TryReplace(BsonDocument document, string path, BsonValue value, out BsonPathResolutionFailure failure)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        failure = BsonPathResolutionFailure.None;

        if (!TryParsePath(path, out var segments))
        {
            failure = BsonPathResolutionFailure.InvalidPath;
            return false;
        }

        if (!TryResolveTarget(document, segments, segments.Length, createParents: false, out var target, out failure) || !target.Exists)
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
        => TryRemove(document, path, pruneEmptyParents, out _);

    internal static bool TryRemove(BsonDocument document, string path, bool pruneEmptyParents, out BsonPathResolutionFailure failure)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        failure = BsonPathResolutionFailure.None;

        if (!TryParsePath(path, out var segments))
        {
            failure = BsonPathResolutionFailure.InvalidPath;
            return false;
        }

        if (!TryResolveTarget(document, segments, segments.Length, createParents: false, out var target, out failure) || !target.Exists)
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
            if (!TryResolveTarget(document, segments, depth, createParents: false, out var target) || !target.Exists || !IsEmptyContainer(target.Value))
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

    private static bool TryResolveTarget(BsonDocument document, IReadOnlyList<BsonPathSegment> segments, int segmentCount, bool createParents, out BsonPathTarget target)
        => TryResolveTarget(document, segments, segmentCount, createParents, out target, out _);

    private static bool TryResolveTarget(BsonDocument document, IReadOnlyList<BsonPathSegment> segments, int segmentCount, bool createParents, out BsonPathTarget target, out BsonPathResolutionFailure failure)
    {
        target = default;
        failure = BsonPathResolutionFailure.None;

        if (segments == null || segmentCount <= 0 || segmentCount > segments.Count)
        {
            failure = BsonPathResolutionFailure.InvalidPath;
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
                    failure = current == null || current.IsNull
                        ? BsonPathResolutionFailure.MissingIntermediate
                        : BsonPathResolutionFailure.ExpectedDocument;
                    return false;
                }

                var parent = current.AsDocument;

                if (!parent.TryGetValue(segment.PropertyName, out current) || current.IsNull)
                {
                    if (!createParents)
                    {
                        failure = BsonPathResolutionFailure.MissingIntermediate;
                        return false;
                    }

                    if (!TryCreateIntermediateValue(segments[i + 1], out current))
                    {
                        return false;
                    }

                    parent[segment.PropertyName] = current;
                    continue;
                }

                if (!CanTraverseValue(current, segments[i + 1]))
                {
                    failure = segments[i + 1].Kind == BsonPathSegmentKind.ArrayIndex
                        ? BsonPathResolutionFailure.ExpectedArray
                        : BsonPathResolutionFailure.ExpectedDocument;
                    return false;
                }

                continue;
            }

            if (segment.Kind == BsonPathSegmentKind.ArrayWildcard || segment.Kind == BsonPathSegmentKind.RecursiveDescent)
            {
                failure = BsonPathResolutionFailure.UnsupportedPattern;
                return false;
            }

            if (current == null || current.IsNull || !current.IsArray)
            {
                failure = current == null || current.IsNull
                    ? BsonPathResolutionFailure.MissingIntermediate
                    : BsonPathResolutionFailure.ExpectedArray;
                return false;
            }

            var array = current.AsArray;

            if (segment.ArrayIndex < 0)
            {
                return false;
            }

            if (segment.ArrayIndex >= array.Count)
            {
                if (!createParents)
                {
                    failure = BsonPathResolutionFailure.MissingIntermediate;
                    return false;
                }

                EnsureArrayIndex(array, segment.ArrayIndex);
            }

            current = array[segment.ArrayIndex];

            if ((current == null || current.IsNull) && createParents)
            {
                if (!TryCreateIntermediateValue(segments[i + 1], out current))
                {
                    return false;
                }

                array[segment.ArrayIndex] = current;
                continue;
            }

            if (!CanTraverseValue(current, segments[i + 1]))
            {
                failure = segments[i + 1].Kind == BsonPathSegmentKind.ArrayIndex
                    ? BsonPathResolutionFailure.ExpectedArray
                    : BsonPathResolutionFailure.ExpectedDocument;
                return false;
            }
        }

        var leaf = segments[segmentCount - 1];

        if (leaf.Kind == BsonPathSegmentKind.Property)
        {
            if (current == null || current.IsNull || !current.IsDocument)
            {
                failure = current == null || current.IsNull
                    ? BsonPathResolutionFailure.MissingIntermediate
                    : BsonPathResolutionFailure.ExpectedDocument;
                return false;
            }

            var parent = current.AsDocument;

            if (parent.TryGetValue(leaf.PropertyName, out var value))
            {
                target = BsonPathTarget.ForProperty(parent, leaf.PropertyName, true, value);
                return true;
            }

            target = BsonPathTarget.ForProperty(parent, leaf.PropertyName, false, BsonValue.Null);
            failure = BsonPathResolutionFailure.MissingLeaf;
            return true;
        }

        if (leaf.Kind == BsonPathSegmentKind.ArrayWildcard || leaf.Kind == BsonPathSegmentKind.RecursiveDescent)
        {
            failure = BsonPathResolutionFailure.UnsupportedPattern;
            return false;
        }

        if (current == null || current.IsNull || !current.IsArray)
        {
            failure = current == null || current.IsNull
                ? BsonPathResolutionFailure.MissingIntermediate
                : BsonPathResolutionFailure.ExpectedArray;
            return false;
        }

        var arrayParent = current.AsArray;

        if (leaf.ArrayIndex < 0)
        {
            return false;
        }

        var exists = leaf.ArrayIndex < arrayParent.Count;

        if (!exists && !createParents)
        {
            failure = BsonPathResolutionFailure.MissingLeaf;
            return false;
        }

        var currentValue = exists ? arrayParent[leaf.ArrayIndex] : BsonValue.Null;
        target = BsonPathTarget.ForArrayIndex(arrayParent, leaf.ArrayIndex, exists, canWrite: true, currentValue);
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
                if (includeLeafWhenMissing && current != null && current.IsNull && TryBuildRemainingPath(currentPath, segments, index, out var missingPath))
                {
                    AddContext(missingPath, BsonPredicateContext.Missing(root, missingPath, collection, migrationName), seenPaths, contexts);
                }

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
            else if (includeLeafWhenMissing && TryBuildRemainingPath(currentPath, segments, index, out var missingPath))
            {
                AddContext(missingPath, BsonPredicateContext.Missing(root, missingPath, collection, migrationName), seenPaths, contexts);
            }

            return;
        }

        if (current == null || current.IsNull || !current.IsArray)
        {
            if (includeLeafWhenMissing && current != null && current.IsNull && TryBuildRemainingPath(currentPath, segments, index, out var missingPath))
            {
                AddContext(missingPath, BsonPredicateContext.Missing(root, missingPath, collection, migrationName), seenPaths, contexts);
            }

            return;
        }

        var array = current.AsArray;

        if (segment.Kind == BsonPathSegmentKind.ArrayIndex)
        {
            if (segment.ArrayIndex < 0 || segment.ArrayIndex >= array.Count)
            {
                if (includeLeafWhenMissing && TryBuildRemainingPath(currentPath, segments, index, out var missingPath))
                {
                    AddContext(missingPath, BsonPredicateContext.Missing(root, missingPath, collection, migrationName), seenPaths, contexts);
                }

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
            if (target.ArrayParent == null || target.ArrayIndex < 0)
            {
                return false;
            }

            EnsureArrayIndex(target.ArrayParent, target.ArrayIndex);
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

    private static void CreateCleanupContexts(BsonDocument root, BsonValue current, string currentPath, string collection, string migrationName, ICollection<BsonPredicateContext> contexts)
    {
        if (current != null && !current.IsNull)
        {
            if (current.IsDocument)
            {
                foreach (var element in current.AsDocument)
                {
                    CreateCleanupContexts(root, element.Value, AppendPropertyName(currentPath, element.Key), collection, migrationName, contexts);
                }
            }
            else if (current.IsArray)
            {
                var array = current.AsArray;

                for (var i = array.Count - 1; i >= 0; i--)
                {
                    CreateCleanupContexts(root, array[i], AppendArrayIndex(currentPath, i), collection, migrationName, contexts);
                }
            }
        }

        contexts.Add(new BsonPredicateContext(root, currentPath, true, current ?? BsonValue.Null, collection, migrationName));
    }

    private static bool TryBuildRemainingPath(string currentPath, IReadOnlyList<BsonPathSegment> segments, int startIndex, out string path)
    {
        path = currentPath;

        for (var i = startIndex; i < segments.Count; i++)
        {
            var segment = segments[i];

            switch (segment.Kind)
            {
                case BsonPathSegmentKind.Property:
                    path = AppendPropertyName(path, segment.PropertyName);
                    break;
                case BsonPathSegmentKind.ArrayIndex:
                    path = AppendArrayIndex(path, segment.ArrayIndex);
                    break;
                default:
                    path = null;
                    return false;
            }
        }

        return path.Length > 0;
    }

    private static bool TryCreateIntermediateValue(BsonPathSegment nextSegment, out BsonValue value)
    {
        switch (nextSegment.Kind)
        {
            case BsonPathSegmentKind.Property:
                value = new BsonDocument();
                return true;
            case BsonPathSegmentKind.ArrayIndex:
                value = new BsonArray();
                return true;
            default:
                value = BsonValue.Null;
                return false;
        }
    }

    private static bool CanTraverseValue(BsonValue value, BsonPathSegment nextSegment)
    {
        if (value == null || value.IsNull)
        {
            return false;
        }

        return nextSegment.Kind switch
        {
            BsonPathSegmentKind.Property => value.IsDocument,
            BsonPathSegmentKind.ArrayIndex => value.IsArray,
            _ => false
        };
    }

    private static void EnsureArrayIndex(BsonArray array, int index)
    {
        while (array.Count <= index)
        {
            array.Add(BsonValue.Null);
        }
    }

    private static bool CanWrite(BsonPathTarget target, FieldWriteMode writeMode)
    {
        return writeMode switch
        {
            FieldWriteMode.MissingOnly => !target.Exists,
            FieldWriteMode.ExistingOnly => target.Exists,
            FieldWriteMode.NullOrMissing => !target.Exists || target.Value.IsNull,
            FieldWriteMode.Overwrite => true,
            _ => throw new ArgumentOutOfRangeException(nameof(writeMode))
        };
    }


    internal enum BsonPathResolutionFailure
    {
        None,
        InvalidPath,
        MissingIntermediate,
        MissingLeaf,
        ExpectedDocument,
        ExpectedArray,
        UnsupportedPattern
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
        private BsonPathTarget(BsonDocument documentParent, string fieldName, BsonArray arrayParent, int arrayIndex, bool exists, bool canWrite, BsonValue value)
        {
            DocumentParent = documentParent;
            FieldName = fieldName ?? string.Empty;
            ArrayParent = arrayParent;
            ArrayIndex = arrayIndex;
            Exists = exists;
            CanWrite = canWrite;
            Value = value ?? BsonValue.Null;
        }

        public BsonDocument DocumentParent { get; }

        public string FieldName { get; }

        public BsonArray ArrayParent { get; }

        public int ArrayIndex { get; }

        public bool Exists { get; }

        public bool CanWrite { get; }

        public BsonValue Value { get; }

        public bool IsArrayIndex => ArrayParent != null;

        public static BsonPathTarget ForProperty(BsonDocument documentParent, string fieldName, bool exists, BsonValue value)
            => new(documentParent, fieldName, null, -1, exists, documentParent != null, value);

        public static BsonPathTarget ForArrayIndex(BsonArray arrayParent, int arrayIndex, bool exists, bool canWrite, BsonValue value)
            => new(null, string.Empty, arrayParent, arrayIndex, exists, canWrite, value);
    }
}

