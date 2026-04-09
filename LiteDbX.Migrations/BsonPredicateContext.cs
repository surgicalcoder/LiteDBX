namespace LiteDbX.Migrations;

public readonly struct BsonPredicateContext
{
    public BsonPredicateContext(BsonDocument root, string path, bool exists, BsonValue value, string collection, string migrationName)
    {
        Root = root;
        Path = path;
        Exists = exists;
        Value = value;
        Collection = collection;
        MigrationName = migrationName;
    }

    public BsonDocument Root { get; }

    public string Path { get; }

    public bool Exists { get; }

    public BsonValue Value { get; }

    public string Collection { get; }

    public string MigrationName { get; }

    public static BsonPredicateContext Missing(BsonDocument root, string path, string collection, string migrationName)
        => new BsonPredicateContext(root, path, false, BsonValue.Null, collection, migrationName);
}

public delegate bool BsonPredicate(BsonPredicateContext context);

public delegate BsonValue BsonValueFactory(BsonPredicateContext context);

public delegate BsonValue BsonValueMutator(BsonPredicateContext context);

