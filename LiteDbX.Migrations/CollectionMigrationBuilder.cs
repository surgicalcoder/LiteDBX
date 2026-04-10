using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteDbX.Migrations;

public readonly struct BsonDocumentMutationContext
{
    public BsonDocumentMutationContext(string collectionName, string migrationName)
    {
        CollectionName = collectionName;
        MigrationName = migrationName;
    }

    public string CollectionName { get; }

    public string MigrationName { get; }
}

public delegate bool BsonDocumentPredicate(BsonDocument document, BsonDocumentMutationContext context);

public delegate BsonDocument BsonDocumentMutator(BsonDocument document, BsonDocumentMutationContext context);

public sealed class CollectionMigrationBuilder
{
    private readonly List<IDocumentMigrationOperation> _operations = new();
    private ConvertIdOperation _convertId;

    public CollectionMigrationBuilder RemoveFieldWhen(string path, BsonPredicate when, bool pruneEmptyParents = false)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new RemoveFieldWhenOperation(path, when, pruneEmptyParents));
        return this;
    }

    public CollectionMigrationBuilder RemoveDocumentWhen(BsonDocumentPredicate predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        _operations.Add(new RemoveDocumentWhenOperation(predicate));
        return this;
    }

    public CollectionMigrationBuilder AddFieldWhen(string path, BsonValueFactory factory, BsonPredicate when)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new AddFieldWhenOperation(path, factory, when));
        return this;
    }

    public CollectionMigrationBuilder AddFieldWhen(string path, BsonValue value, BsonPredicate when)
    {
        if (when == null) throw new ArgumentNullException(nameof(when));
        return AddFieldWhen(path, _ => value ?? BsonValue.Null, when);
    }

    public CollectionMigrationBuilder SetDefaultWhenMissing(string path, BsonValueFactory factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        return AddFieldWhen(path, factory, BsonPredicates.Missing);
    }

    public CollectionMigrationBuilder SetDefaultWhenMissing(string path, BsonValue value)
    {
        return AddFieldWhen(path, value, BsonPredicates.Missing);
    }

    public ReferenceRepairBuilder RepairReference(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        return new ReferenceRepairBuilder(this, path);
    }

    public CollectionMigrationBuilder ModifyFieldWhen(string path, BsonValueMutator mutator, BsonPredicate when)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new ModifyFieldWhenOperation(path, mutator, when));
        return this;
    }

    public CollectionMigrationBuilder ModifyDocumentWhen(BsonDocumentPredicate predicate, BsonDocumentMutator mutator)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));

        _operations.Add(new ModifyDocumentWhenOperation(predicate, mutator));
        return this;
    }

    public CollectionMigrationBuilder SetFieldWhen(string path, BsonValueFactory factory, BsonPredicate when)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new SetFieldWhenOperation(path, factory, when));
        return this;
    }

    public CollectionMigrationBuilder SetFieldWhen(string path, BsonValue value, BsonPredicate when)
    {
        if (when == null) throw new ArgumentNullException(nameof(when));
        return SetFieldWhen(path, _ => value ?? BsonValue.Null, when);
    }

    public CollectionMigrationBuilder RenameField(string sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));

        _operations.Add(FieldTransferOperation.CreateRename(sourcePath, targetPath));
        return this;
    }

    public CollectionMigrationBuilder CopyField(string sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));

        _operations.Add(FieldTransferOperation.CreateCopy(sourcePath, targetPath));
        return this;
    }

    public CollectionMigrationBuilder MoveField(string sourcePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentNullException(nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentNullException(nameof(targetPath));

        _operations.Add(FieldTransferOperation.CreateMove(sourcePath, targetPath));
        return this;
    }

    public FieldConversionBuilder ConvertField(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        return new FieldConversionBuilder(this, path);
    }

    public IdConversionBuilder ConvertId()
    {
        _convertId ??= new ConvertIdOperation(InvalidObjectIdPolicy.Fail);
        return new IdConversionBuilder(this, _convertId);
    }

    internal CollectionMigrationPlan Build()
        => new(new ReadOnlyCollection<IDocumentMigrationOperation>(_operations), _convertId);

    internal void AddOperation(IDocumentMigrationOperation operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        _operations.Add(operation);
    }
}

internal sealed class CollectionMigrationPlan
{
    public CollectionMigrationPlan(IReadOnlyList<IDocumentMigrationOperation> operations, ConvertIdOperation convertId)
    {
        Operations = operations ?? throw new ArgumentNullException(nameof(operations));
        ConvertId = convertId;
    }

    public IReadOnlyList<IDocumentMigrationOperation> Operations { get; }

    public ConvertIdOperation ConvertId { get; }

    public bool RequiresRebuild => ConvertId != null;
}

public sealed class ReferenceRepairBuilder
{
    private readonly CollectionMigrationBuilder _parent;
    private readonly string _path;
    private string _sourceCollection;
    private string _migrationName;
    private string _referenceCollectionPath;

    internal ReferenceRepairBuilder(CollectionMigrationBuilder parent, string path)
    {
        _parent = parent;
        _path = path;
    }

    public ReferenceRepairBuilder FromCollection(string sourceCollection)
    {
        _sourceCollection = string.IsNullOrWhiteSpace(sourceCollection) ? throw new ArgumentNullException(nameof(sourceCollection)) : sourceCollection;
        return this;
    }

    public ReferenceRepairBuilder FromMigration(string migrationName)
    {
        _migrationName = string.IsNullOrWhiteSpace(migrationName) ? throw new ArgumentNullException(nameof(migrationName)) : migrationName;
        return this;
    }

    public ReferenceRepairBuilder WhenReferenceCollectionIs(string referenceCollectionPath)
    {
        _referenceCollectionPath = string.IsNullOrWhiteSpace(referenceCollectionPath) ? throw new ArgumentNullException(nameof(referenceCollectionPath)) : referenceCollectionPath;
        return this;
    }

    public CollectionMigrationBuilder Apply()
    {
        if (string.IsNullOrWhiteSpace(_sourceCollection))
        {
            throw new InvalidOperationException("RepairReference(...).FromCollection(...) must be specified before Apply().");
        }

        _parent.AddOperation(new RepairReferenceOperation(_path, _sourceCollection, _migrationName, _referenceCollectionPath));
        return _parent;
    }
}

public sealed class FieldConversionBuilder
{
    private readonly CollectionMigrationBuilder _parent;
    private readonly string _path;
    private ConvertStringToObjectIdOperation _operation;

    internal FieldConversionBuilder(CollectionMigrationBuilder parent, string path)
    {
        _parent = parent;
        _path = path;
    }

    public FieldConversionBuilder FromStringToObjectId()
    {
        _operation ??= new ConvertStringToObjectIdOperation(_path, InvalidObjectIdPolicy.LeaveUnchanged);
        _parent.AddOperation(_operation);
        return this;
    }

    public CollectionMigrationBuilder OnInvalidString(InvalidObjectIdPolicy policy)
    {
        _operation ??= new ConvertStringToObjectIdOperation(_path, policy);
        _operation.InvalidPolicy = policy;
        return _parent;
    }
}

public sealed class IdConversionBuilder
{
    private readonly CollectionMigrationBuilder _parent;
    private readonly ConvertIdOperation _operation;

    internal IdConversionBuilder(CollectionMigrationBuilder parent, ConvertIdOperation operation)
    {
        _parent = parent;
        _operation = operation;
    }

    public IdConversionBuilder FromStringToObjectId()
    {
        return this;
    }

    public CollectionMigrationBuilder OnInvalidString(InvalidObjectIdPolicy policy)
    {
        _operation.InvalidPolicy = policy;
        return _parent;
    }
}

internal interface IDocumentMigrationOperation
{
    DocumentOperationStage Stage { get; }

    DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context);
}

internal enum DocumentOperationStage
{
    PreId,
    PostId
}

internal enum DocumentOperationKind
{
    NoChange,
    Updated,
    DeleteDocument
}

internal readonly struct DocumentOperationResult
{
    private DocumentOperationResult(DocumentOperationKind kind, BsonDocument document)
    {
        Kind = kind;
        Document = document;
    }

    public DocumentOperationKind Kind { get; }

    public BsonDocument Document { get; }

    public static DocumentOperationResult NoChange() => new(DocumentOperationKind.NoChange, null);

    public static DocumentOperationResult Updated(BsonDocument document = null) => new(DocumentOperationKind.Updated, document);

    public static DocumentOperationResult DeleteDocument() => new(DocumentOperationKind.DeleteDocument, null);
}

internal sealed class RemoveFieldWhenOperation : IDocumentMigrationOperation
{
    private readonly string _path;
    private readonly BsonPredicate _when;
    private readonly bool _pruneEmptyParents;

    public RemoveFieldWhenOperation(string path, BsonPredicate when, bool pruneEmptyParents)
    {
        _path = path;
        _when = when;
        _pruneEmptyParents = pruneEmptyParents;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasWildcard(_path))
        {
            var predicateContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: false, context.CollectionName, context.MigrationName);
            var changed = false;

            for (var i = predicateContexts.Count - 1; i >= 0; i--)
            {
                var wildcardContext = predicateContexts[i];

                if (!_when(wildcardContext) || !wildcardContext.Exists)
                {
                    continue;
                }

                changed |= BsonPathNavigator.TryRemove(document, wildcardContext.Path, _pruneEmptyParents);
            }

            return changed ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        var predicateContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!_when(predicateContext) || !predicateContext.Exists)
        {
            return DocumentOperationResult.NoChange();
        }

        return BsonPathNavigator.TryRemove(document, _path, _pruneEmptyParents)
            ? DocumentOperationResult.Updated()
            : DocumentOperationResult.NoChange();
    }
}

internal sealed class RemoveDocumentWhenOperation : IDocumentMigrationOperation
{
    private readonly BsonDocumentPredicate _predicate;

    public RemoveDocumentWhenOperation(BsonDocumentPredicate predicate)
    {
        _predicate = predicate;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PreId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        return _predicate(document, context.ToPublicContext())
            ? DocumentOperationResult.DeleteDocument()
            : DocumentOperationResult.NoChange();
    }
}

internal sealed class AddFieldWhenOperation : IDocumentMigrationOperation
{
    private readonly string _path;
    private readonly BsonValueFactory _factory;
    private readonly BsonPredicate _when;

    public AddFieldWhenOperation(string path, BsonValueFactory factory, BsonPredicate when)
    {
        _path = path;
        _factory = factory;
        _when = when;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasRecursive(_path))
        {
            throw new InvalidOperationException("Recursive paths are not supported for AddFieldWhen/SetDefaultWhenMissing in this V2 slice.");
        }

        if (BsonPathNavigator.HasWildcard(_path))
        {
            var predicateContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: true, context.CollectionName, context.MigrationName);
            var changed = false;

            for (var i = 0; i < predicateContexts.Count; i++)
            {
                var wildcardContext = predicateContexts[i];

                if (!_when(wildcardContext) || wildcardContext.Exists)
                {
                    continue;
                }

                var wildcardValue = _factory(wildcardContext) ?? BsonValue.Null;
                changed |= BsonPathNavigator.TryAdd(document, wildcardContext.Path, wildcardValue, overwrite: false);
            }

            return changed ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        var predicateContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!_when(predicateContext) || predicateContext.Exists)
        {
            return DocumentOperationResult.NoChange();
        }

        var newValue = _factory(predicateContext) ?? BsonValue.Null;

        return BsonPathNavigator.TryAdd(document, _path, newValue, overwrite: false)
            ? DocumentOperationResult.Updated()
            : DocumentOperationResult.NoChange();
    }
}

internal sealed class ModifyFieldWhenOperation : IDocumentMigrationOperation
{
    private readonly string _path;
    private readonly BsonValueMutator _mutator;
    private readonly BsonPredicate _when;

    public ModifyFieldWhenOperation(string path, BsonValueMutator mutator, BsonPredicate when)
    {
        _path = path;
        _mutator = mutator;
        _when = when;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasWildcard(_path))
        {
            var predicateContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: false, context.CollectionName, context.MigrationName);
            var changed = false;

            for (var i = 0; i < predicateContexts.Count; i++)
            {
                var wildcardContext = predicateContexts[i];

                if (!_when(wildcardContext) || !wildcardContext.Exists)
                {
                    continue;
                }

                var wildcardValue = _mutator(wildcardContext) ?? BsonValue.Null;
                changed |= BsonPathNavigator.TryReplace(document, wildcardContext.Path, wildcardValue);
            }

            return changed ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        var predicateContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!_when(predicateContext) || !predicateContext.Exists)
        {
            return DocumentOperationResult.NoChange();
        }

        var newValue = _mutator(predicateContext) ?? BsonValue.Null;

        return BsonPathNavigator.TryReplace(document, _path, newValue)
            ? DocumentOperationResult.Updated()
            : DocumentOperationResult.NoChange();
    }
}

internal sealed class ModifyDocumentWhenOperation : IDocumentMigrationOperation
{
    private readonly BsonDocumentPredicate _predicate;
    private readonly BsonDocumentMutator _mutator;

    public ModifyDocumentWhenOperation(BsonDocumentPredicate predicate, BsonDocumentMutator mutator)
    {
        _predicate = predicate;
        _mutator = mutator;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PreId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (!_predicate(document, context.ToPublicContext()))
        {
            return DocumentOperationResult.NoChange();
        }

        var replacement = _mutator(BsonPathNavigator.CloneValue(document).AsDocument, context.ToPublicContext());

        if (replacement == null)
        {
            throw new InvalidOperationException($"ModifyDocumentWhen returned null for migration '{context.MigrationName}' in collection '{context.CollectionName}'. Use RemoveDocumentWhen for deletion.");
        }

        if (!replacement.TryGetValue("_id", out var replacementId) || replacementId != document["_id"])
        {
            throw new InvalidOperationException($"ModifyDocumentWhen must preserve '_id' for migration '{context.MigrationName}' in collection '{context.CollectionName}'. Use ConvertId() for identity changes.");
        }

        return DocumentOperationResult.Updated(replacement);
    }
}

internal sealed class SetFieldWhenOperation : IDocumentMigrationOperation
{
    private readonly string _path;
    private readonly BsonValueFactory _factory;
    private readonly BsonPredicate _when;

    public SetFieldWhenOperation(string path, BsonValueFactory factory, BsonPredicate when)
    {
        _path = path;
        _factory = factory;
        _when = when;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasRecursive(_path))
        {
            throw new InvalidOperationException("Recursive paths are not supported for SetFieldWhen in this V2 slice.");
        }

        if (BsonPathNavigator.HasWildcard(_path))
        {
            var predicateContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: true, context.CollectionName, context.MigrationName);
            var changed = false;

            for (var i = 0; i < predicateContexts.Count; i++)
            {
                var wildcardContext = predicateContexts[i];

                if (!_when(wildcardContext))
                {
                    continue;
                }

                var wildcardValue = _factory(wildcardContext) ?? BsonValue.Null;
                changed |= BsonPathNavigator.TryAdd(document, wildcardContext.Path, wildcardValue, overwrite: true);
            }

            return changed ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        var predicateContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!_when(predicateContext))
        {
            return DocumentOperationResult.NoChange();
        }

        var newValue = _factory(predicateContext) ?? BsonValue.Null;

        return BsonPathNavigator.TryAdd(document, _path, newValue, overwrite: true)
            ? DocumentOperationResult.Updated()
            : DocumentOperationResult.NoChange();
    }
}

internal enum FieldTransferKind
{
    Copy,
    Rename,
    Move
}

internal sealed class FieldTransferOperation : IDocumentMigrationOperation
{
    private readonly string _sourcePath;
    private readonly string _targetPath;
    private readonly FieldTransferKind _kind;

    private FieldTransferOperation(string sourcePath, string targetPath, FieldTransferKind kind)
    {
        if (BsonPathNavigator.HasWildcard(sourcePath) || BsonPathNavigator.HasWildcard(targetPath))
        {
            throw new ArgumentException("Wildcard/recursive paths are not supported for RenameField/CopyField/MoveField in this V2 slice.");
        }

        if (BsonPathNavigator.PathsConflict(sourcePath, targetPath))
        {
            throw new ArgumentException($"Source path '{sourcePath}' and target path '{targetPath}' overlap and cannot be used together.");
        }

        _sourcePath = sourcePath;
        _targetPath = targetPath;
        _kind = kind;
    }

    public static FieldTransferOperation CreateCopy(string sourcePath, string targetPath) => new(sourcePath, targetPath, FieldTransferKind.Copy);

    public static FieldTransferOperation CreateRename(string sourcePath, string targetPath) => new(sourcePath, targetPath, FieldTransferKind.Rename);

    public static FieldTransferOperation CreateMove(string sourcePath, string targetPath) => new(sourcePath, targetPath, FieldTransferKind.Move);

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (!BsonPathNavigator.TryGet(document, _sourcePath, out _, out _, out var value))
        {
            return DocumentOperationResult.NoChange();
        }

        if (BsonPathNavigator.TryGet(document, _targetPath, out _, out _, out _))
        {
            return DocumentOperationResult.NoChange();
        }

        var clonedValue = BsonPathNavigator.CloneValue(value);

        if (!BsonPathNavigator.TryAdd(document, _targetPath, clonedValue, overwrite: false))
        {
            return DocumentOperationResult.NoChange();
        }

        if (_kind == FieldTransferKind.Copy)
        {
            return DocumentOperationResult.Updated();
        }

        var removed = BsonPathNavigator.TryRemove(document, _sourcePath, pruneEmptyParents: false);

        if (removed)
        {
            return DocumentOperationResult.Updated();
        }

        BsonPathNavigator.TryRemove(document, _targetPath, pruneEmptyParents: false);
        return DocumentOperationResult.NoChange();
    }
}

internal interface IIdRemapAwareOperation
{
    string SourceCollection { get; }
    string SourceMigrationName { get; }
}

internal sealed class RepairReferenceOperation : IDocumentMigrationOperation, IIdRemapAwareOperation
{
    private readonly string _path;
    private readonly string _referenceCollectionPath;

    public RepairReferenceOperation(string path, string sourceCollection, string sourceMigrationName, string referenceCollectionPath)
    {
        var targetHasRecursive = BsonPathNavigator.HasRecursive(path);
        var referenceHasRecursive = referenceCollectionPath != null && BsonPathNavigator.HasRecursive(referenceCollectionPath);

        if (targetHasRecursive || referenceHasRecursive)
        {
            throw new ArgumentException("Recursive paths are not supported for RepairReference in this V2 slice.");
        }

        var targetHasWildcard = BsonPathNavigator.HasWildcard(path);
        var referenceHasWildcard = referenceCollectionPath != null && BsonPathNavigator.HasWildcard(referenceCollectionPath);

        if (targetHasWildcard || referenceHasWildcard)
        {
            if (!targetHasWildcard || !referenceHasWildcard || referenceCollectionPath == null)
            {
                throw new ArgumentException("Wildcard RepairReference requires a paired wildcard collection-guard path in this V2 slice.");
            }

            if (!BsonPathNavigator.CanBindWildcardSiblingPath(path, referenceCollectionPath))
            {
                throw new ArgumentException("Wildcard RepairReference paths must share the same wildcard topology and parent path in this V2 slice.");
            }
        }

        _path = path;
        SourceCollection = sourceCollection;
        SourceMigrationName = sourceMigrationName;
        _referenceCollectionPath = referenceCollectionPath;
    }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public string SourceCollection { get; }

    public string SourceMigrationName { get; }

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasWildcard(_path))
        {
            var targetContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: false, context.CollectionName, context.MigrationName);
            var anyChanged = false;

            for (var i = 0; i < targetContexts.Count; i++)
            {
                var wildcardTargetContext = targetContexts[i];

                if (!wildcardTargetContext.Exists || !wildcardTargetContext.Value.IsString)
                {
                    continue;
                }

                if (!BsonPathNavigator.TryBindPath(_referenceCollectionPath, wildcardTargetContext.Path, out var boundReferencePath))
                {
                    continue;
                }

                var refContext = BsonPathNavigator.CreateContext(document, boundReferencePath, context.CollectionName, context.MigrationName);

                if (!refContext.Exists || !refContext.Value.IsString || !string.Equals(refContext.Value.AsString, SourceCollection, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!context.TryGetRemappedObjectId(SourceCollection, SourceMigrationName, wildcardTargetContext.Value.AsString, BsonType.String, out var remappedObjectId))
                {
                    continue;
                }

                if (!BsonPathNavigator.TryReplace(document, wildcardTargetContext.Path, new BsonValue(new ObjectId(remappedObjectId))))
                {
                    continue;
                }

                context.IncrementRepairedReferenceCount();
                anyChanged = true;
            }

            return anyChanged ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        if (_referenceCollectionPath != null)
        {
            var refContext = BsonPathNavigator.CreateContext(document, _referenceCollectionPath, context.CollectionName, context.MigrationName);

            if (!refContext.Exists || !refContext.Value.IsString || !string.Equals(refContext.Value.AsString, SourceCollection, StringComparison.OrdinalIgnoreCase))
            {
                return DocumentOperationResult.NoChange();
            }
        }

        var targetContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!targetContext.Exists || !targetContext.Value.IsString)
        {
            return DocumentOperationResult.NoChange();
        }

        if (!context.TryGetRemappedObjectId(SourceCollection, SourceMigrationName, targetContext.Value.AsString, BsonType.String, out var objectId))
        {
            return DocumentOperationResult.NoChange();
        }

        var changed = BsonPathNavigator.TryReplace(document, _path, new BsonValue(new ObjectId(objectId)));

        if (changed)
        {
            context.IncrementRepairedReferenceCount();
            return DocumentOperationResult.Updated();
        }

        return DocumentOperationResult.NoChange();
    }
}

internal sealed class ConvertStringToObjectIdOperation : IDocumentMigrationOperation
{
    private readonly string _path;

    public ConvertStringToObjectIdOperation(string path, InvalidObjectIdPolicy invalidPolicy)
    {
        _path = path;
        InvalidPolicy = invalidPolicy;
    }

    public InvalidObjectIdPolicy InvalidPolicy { get; set; }

    public DocumentOperationStage Stage => DocumentOperationStage.PostId;

    public DocumentOperationResult Apply(BsonDocument document, DocumentMigrationExecutionContext context)
    {
        if (BsonPathNavigator.HasWildcard(_path))
        {
            var predicateContexts = BsonPathNavigator.CreateContexts(document, _path, includeLeafWhenMissing: false, context.CollectionName, context.MigrationName);
            var changed = false;

            for (var i = 0; i < predicateContexts.Count; i++)
            {
                var wildcardContext = predicateContexts[i];

                if (!wildcardContext.Exists || wildcardContext.Value.IsObjectId || !wildcardContext.Value.IsString)
                {
                    continue;
                }

                if (TryParseObjectId(wildcardContext.Value.AsString, out var wildcardObjectId))
                {
                    changed |= BsonPathNavigator.TryReplace(document, wildcardContext.Path, new BsonValue(wildcardObjectId));
                    continue;
                }

                switch (InvalidPolicy)
                {
                    case InvalidObjectIdPolicy.Fail:
                        throw new InvalidOperationException($"Invalid ObjectId string at '{wildcardContext.Path}' in collection '{context.CollectionName}' for migration '{context.MigrationName}'.");
                    case InvalidObjectIdPolicy.SkipDocument:
                    case InvalidObjectIdPolicy.LeaveUnchanged:
                        break;
                    case InvalidObjectIdPolicy.RemoveField:
                        changed |= BsonPathNavigator.TryRemove(document, wildcardContext.Path, pruneEmptyParents: false);
                        break;
                    case InvalidObjectIdPolicy.GenerateNewId:
                        changed |= BsonPathNavigator.TryReplace(document, wildcardContext.Path, new BsonValue(ObjectId.NewObjectId()));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return changed ? DocumentOperationResult.Updated() : DocumentOperationResult.NoChange();
        }

        var predicateContext = BsonPathNavigator.CreateContext(document, _path, context.CollectionName, context.MigrationName);

        if (!predicateContext.Exists || predicateContext.Value.IsObjectId)
        {
            return DocumentOperationResult.NoChange();
        }

        if (!predicateContext.Value.IsString)
        {
            return DocumentOperationResult.NoChange();
        }

        if (TryParseObjectId(predicateContext.Value.AsString, out var objectId))
        {
            return BsonPathNavigator.TryReplace(document, _path, new BsonValue(objectId))
                ? DocumentOperationResult.Updated()
                : DocumentOperationResult.NoChange();
        }

        switch (InvalidPolicy)
        {
            case InvalidObjectIdPolicy.Fail:
                throw new InvalidOperationException($"Invalid ObjectId string at '{_path}' in collection '{context.CollectionName}' for migration '{context.MigrationName}'.");
            case InvalidObjectIdPolicy.SkipDocument:
            case InvalidObjectIdPolicy.LeaveUnchanged:
                return DocumentOperationResult.NoChange();
            case InvalidObjectIdPolicy.RemoveField:
                return BsonPathNavigator.TryRemove(document, _path, pruneEmptyParents: false)
                    ? DocumentOperationResult.Updated()
                    : DocumentOperationResult.NoChange();
            case InvalidObjectIdPolicy.GenerateNewId:
                return BsonPathNavigator.TryReplace(document, _path, new BsonValue(ObjectId.NewObjectId()))
                    ? DocumentOperationResult.Updated()
                    : DocumentOperationResult.NoChange();
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static bool TryParseObjectId(string value, out ObjectId objectId)
    {
        try
        {
            objectId = new ObjectId(value);
            return true;
        }
        catch
        {
            objectId = null;
            return false;
        }
    }
}

internal sealed class ConvertIdOperation
{
    public ConvertIdOperation(InvalidObjectIdPolicy invalidPolicy)
    {
        InvalidPolicy = invalidPolicy;
    }

    public InvalidObjectIdPolicy InvalidPolicy { get; set; }
}

