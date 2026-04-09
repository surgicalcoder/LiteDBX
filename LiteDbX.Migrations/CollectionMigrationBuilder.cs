using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace LiteDbX.Migrations;

public sealed class CollectionMigrationBuilder
{
    private readonly List<IDocumentMigrationOperation> _operations = new();

    public CollectionMigrationBuilder RemoveFieldWhen(string path, BsonPredicate when, bool pruneEmptyParents = false)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new RemoveFieldWhenOperation(path, when, pruneEmptyParents));
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

    public CollectionMigrationBuilder ModifyFieldWhen(string path, BsonValueMutator mutator, BsonPredicate when)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
        if (mutator == null) throw new ArgumentNullException(nameof(mutator));
        if (when == null) throw new ArgumentNullException(nameof(when));

        _operations.Add(new ModifyFieldWhenOperation(path, mutator, when));
        return this;
    }

    public FieldConversionBuilder ConvertField(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

        return new FieldConversionBuilder(this, path);
    }

    internal IReadOnlyList<IDocumentMigrationOperation> Build() => new ReadOnlyCollection<IDocumentMigrationOperation>(_operations);

    internal void AddOperation(IDocumentMigrationOperation operation)
    {
        if (operation == null) throw new ArgumentNullException(nameof(operation));
        _operations.Add(operation);
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

internal interface IDocumentMigrationOperation
{
    bool Apply(BsonDocument document, string collectionName, string migrationName);
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

    public bool Apply(BsonDocument document, string collectionName, string migrationName)
    {
        var context = BsonPathNavigator.CreateContext(document, _path, collectionName, migrationName);

        if (!_when(context) || !context.Exists)
        {
            return false;
        }

        return BsonPathNavigator.TryRemove(document, _path, _pruneEmptyParents);
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

    public bool Apply(BsonDocument document, string collectionName, string migrationName)
    {
        var context = BsonPathNavigator.CreateContext(document, _path, collectionName, migrationName);

        if (!_when(context) || context.Exists)
        {
            return false;
        }

        var newValue = _factory(context) ?? BsonValue.Null;

        return BsonPathNavigator.TryAdd(document, _path, newValue, overwrite: false);
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

    public bool Apply(BsonDocument document, string collectionName, string migrationName)
    {
        var context = BsonPathNavigator.CreateContext(document, _path, collectionName, migrationName);

        if (!_when(context) || !context.Exists)
        {
            return false;
        }

        var newValue = _mutator(context) ?? BsonValue.Null;

        return BsonPathNavigator.TryReplace(document, _path, newValue);
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

    public bool Apply(BsonDocument document, string collectionName, string migrationName)
    {
        var context = BsonPathNavigator.CreateContext(document, _path, collectionName, migrationName);

        if (!context.Exists || context.Value.IsObjectId)
        {
            return false;
        }

        if (!context.Value.IsString)
        {
            return false;
        }

        if (TryParseObjectId(context.Value.AsString, out var objectId))
        {
            return BsonPathNavigator.TryReplace(document, _path, new BsonValue(objectId));
        }

        switch (InvalidPolicy)
        {
            case InvalidObjectIdPolicy.Fail:
                throw new InvalidOperationException($"Invalid ObjectId string at '{_path}' in collection '{collectionName}' for migration '{migrationName}'.");
            case InvalidObjectIdPolicy.SkipDocument:
            case InvalidObjectIdPolicy.LeaveUnchanged:
                return false;
            case InvalidObjectIdPolicy.RemoveField:
                return BsonPathNavigator.TryRemove(document, _path, pruneEmptyParents: false);
            case InvalidObjectIdPolicy.GenerateNewId:
                return BsonPathNavigator.TryReplace(document, _path, new BsonValue(ObjectId.NewObjectId()));
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

