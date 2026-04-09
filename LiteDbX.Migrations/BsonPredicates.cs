using System;
using System.Linq;

namespace LiteDbX.Migrations;

public static class BsonPredicates
{
    public static readonly BsonPredicate Always = _ => true;
    public static readonly BsonPredicate Missing = context => context.Exists == false;
    public static readonly BsonPredicate Null = context => context.Exists && context.Value.IsNull;
    public static readonly BsonPredicate NullOrMissing = context => context.Exists == false || context.Value.IsNull;
    public static readonly BsonPredicate EmptyArray = context => context.Exists && context.Value.IsArray && context.Value.AsArray.Count == 0;
    public static readonly BsonPredicate EmptyDocument = context => context.Exists && context.Value.IsDocument && context.Value.AsDocument.Count == 0;
    public static readonly BsonPredicate EmptyString = context => context.Exists && context.Value.IsString && context.Value.AsString.Length == 0;
    public static readonly BsonPredicate WhiteSpaceString = context => context.Exists && context.Value.IsString && string.IsNullOrWhiteSpace(context.Value.AsString);
    public static readonly BsonPredicate NullOrWhiteSpaceString = Or(NullOrMissing, WhiteSpaceString);
    public static readonly BsonPredicate IsString = context => context.Exists && context.Value.IsString;
    public static readonly BsonPredicate IsArray = context => context.Exists && context.Value.IsArray;
    public static readonly BsonPredicate IsDocument = context => context.Exists && context.Value.IsDocument;
    public static readonly BsonPredicate IsObjectId = context => context.Exists && context.Value.IsObjectId;
    public static readonly BsonPredicate IsGuid = context => context.Exists && context.Value.IsGuid;
    public static readonly BsonPredicate IsBoolean = context => context.Exists && context.Value.IsBoolean;
    public static readonly BsonPredicate IsNumber = context => context.Exists && context.Value.IsNumber;
    public static readonly BsonPredicate ZeroNumber = context => context.Exists && context.Value.IsNumber && context.Value.CompareTo(new BsonValue(0)) == 0;
    public static readonly BsonPredicate FalseBoolean = context => context.Exists && context.Value.IsBoolean && context.Value.AsBoolean == false;
    public static readonly BsonPredicate EmptyBinary = context => context.Exists && context.Value.IsBinary && context.Value.AsBinary.Length == 0;
    public static readonly BsonPredicate EmptyGuid = context => context.Exists && context.Value.IsGuid && context.Value.AsGuid == Guid.Empty;
    public static readonly BsonPredicate EmptyObjectId = context => context.Exists && context.Value.IsObjectId && context.Value.AsObjectId == ObjectId.Empty;
    public static readonly BsonPredicate NullLike = Or(NullOrMissing, EmptyString, WhiteSpaceString);
    public static readonly BsonPredicate StructurallyEmpty = Or(EmptyArray, EmptyDocument, EmptyBinary);

    public static BsonPredicate Default(BsonValue value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));

        return context => context.Exists && context.Value == value;
    }

    public static BsonPredicate AnyOfDefaults(params BsonValue[] values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        if (values.Length == 0) return _ => false;

        return context => context.Exists && values.Any(x => x == context.Value);
    }

    public static BsonPredicate And(params BsonPredicate[] predicates)
    {
        if (predicates == null) throw new ArgumentNullException(nameof(predicates));

        return context => predicates.All(predicate => predicate(context));
    }

    public static BsonPredicate Or(params BsonPredicate[] predicates)
    {
        if (predicates == null) throw new ArgumentNullException(nameof(predicates));

        return context => predicates.Any(predicate => predicate(context));
    }

    public static BsonPredicate Not(BsonPredicate predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        return context => !predicate(context);
    }
}

