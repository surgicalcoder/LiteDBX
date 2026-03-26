using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using static LiteDbX.Constants;

namespace LiteDbX;

/// <summary>
/// Class that converts your entity class to/from BsonDocument
/// If you prefer use a new instance of BsonMapper (not Global), be sure cache this instance for better performance
/// Serialization rules:
/// - Classes must be "public" with a public constructor (without parameters)
/// - Properties must have public getter (can be read-only)
/// - Entity class must have Id property, [ClassName]Id property or [BsonId] attribute
/// - No circular references
/// - Fields are not valid
/// - IList, Array supports
/// - IDictionary supports (Key must be a simple datatype - converted by ChangeType)
/// </summary>
public partial class BsonMapper
{
    public BsonMapper(Func<Type, object> customTypeInstantiator = null, ITypeNameBinder typeNameBinder = null)
    {
        SerializeNullValues = false;
        TrimWhitespace = true;
        EmptyStringToNull = true;
        EnumAsInteger = false;
        ResolveFieldName = s => s;
        ResolveMember = (t, mi, mm) => { };
        ResolveCollectionName = t => Reflection.IsEnumerable(t) ? Reflection.GetListItemType(t).Name : t.Name;
        IncludeFields = false;
        MaxDepth = 20;

        _typeInstantiator = customTypeInstantiator ?? (t => null);
        _typeNameBinder = typeNameBinder ?? DefaultTypeNameBinder.Instance;

        #region Register CustomTypes

        RegisterType(uri => uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString(), bson => new Uri(bson.AsString));
        RegisterType<DateTimeOffset>(value => new BsonValue(value.UtcDateTime), bson => bson.AsDateTime.ToUniversalTime());
        RegisterType(value => new BsonValue(value.Ticks), bson => new TimeSpan(bson.AsInt64));
        RegisterType(
            r => r.Options == RegexOptions.None ? new BsonValue(r.ToString()) : new BsonDocument { { "p", r.ToString() }, { "o", (int)r.Options } },
            value => value.IsString ? new Regex(value) : new Regex(value.AsDocument["p"].AsString, (RegexOptions)value.AsDocument["o"].AsInt32)
        );

        #endregion
    }

    /// <summary>
    /// Map your entity class to BsonDocument using fluent API
    /// </summary>
    public EntityBuilder<T> Entity<T>()
    {
        return new EntityBuilder<T>(this, _typeNameBinder);
    }

    #region Properties

    /// <summary>
    /// Map serializer/deserialize for custom types
    /// </summary>
    private readonly ConcurrentDictionary<Type, Func<object, BsonMapper, BsonValue>> _customSerializer = new();

    private readonly ConcurrentDictionary<Type, Func<BsonValue, BsonMapper, object>> _customDeserializer = new();

    private readonly ConcurrentDictionary<Type, OpenGenericTypeRegistration> _openGenericTypes = new();

    private readonly ConcurrentDictionary<Type, Func<object, BsonMapper, BsonValue>> _resolvedOpenGenericSerializer = new();

    private readonly ConcurrentDictionary<Type, Func<BsonValue, BsonMapper, object>> _resolvedOpenGenericDeserializer = new();

    /// <summary>
    /// Type instantiator function to support IoC
    /// </summary>
    private readonly Func<Type, object> _typeInstantiator;

    /// <summary>
    /// Type name binder to control how type names are serialized to BSON documents
    /// </summary>
    private readonly ITypeNameBinder _typeNameBinder;

    /// <summary>
    /// Global instance used when no BsonMapper are passed in LiteDatabase ctor
    /// </summary>
    public static BsonMapper Global = new();

    /// <summary>
    /// A resolver name for field
    /// </summary>
    public Func<string, string> ResolveFieldName;

    /// <summary>
    /// Indicate that mapper do not serialize null values (default false)
    /// </summary>
    public bool SerializeNullValues { get; set; }

    /// <summary>
    /// Apply .Trim() in strings when serialize (default true)
    /// </summary>
    public bool TrimWhitespace { get; set; }

    /// <summary>
    /// Convert EmptyString to Null (default true)
    /// </summary>
    public bool EmptyStringToNull { get; set; }

    /// <summary>
    /// Get/Set if enum must be converted into Integer value. If false, enum will be converted into String value.
    /// MUST BE "true" to support LINQ expressions (default false)
    /// </summary>
    public bool EnumAsInteger { get; set; }

    /// <summary>
    /// Get/Set that mapper must include fields (default: false)
    /// </summary>
    public bool IncludeFields { get; set; }

    /// <summary>
    /// Get/Set that mapper must include non public (private, protected and internal) (default: false)
    /// </summary>
    public bool IncludeNonPublic { get; set; }

    /// <summary>
    /// Get/Set maximum depth for nested object (default 20)
    /// </summary>
    public int MaxDepth { get; set; }

    /// <summary>
    /// A custom callback to change MemberInfo behavior when converting to MemberMapper.
    /// Use mapper.ResolveMember(Type entity, MemberInfo property, MemberMapper documentMappedField)
    /// Set FieldName to null if you want remove from mapped document
    /// </summary>
    public Action<Type, MemberInfo, MemberMapper> ResolveMember;

    /// <summary>
    /// Custom resolve name collection based on Type
    /// </summary>
    public Func<Type, string> ResolveCollectionName;

    #endregion

    #region Register CustomType

    /// <summary>
    /// Register a custom type serializer/deserialize function
    /// </summary>
    public void RegisterType<T>(Func<T, BsonValue> serialize, Func<BsonValue, T> deserialize)
        => RegisterType(typeof(T), o => serialize((T)o), b => deserialize(b));

    /// <summary>
    /// Register a custom type serializer/deserialize function with access to the current mapper instance.
    /// </summary>
    public void RegisterType<T>(Func<T, BsonMapper, BsonValue> serialize, Func<BsonValue, BsonMapper, T> deserialize)
        => RegisterType(typeof(T), (o, m) => serialize((T)o, m), (b, m) => deserialize(b, m));

    /// <summary>
    /// Register a custom type serializer/deserialize function
    /// </summary>
    public void RegisterType(Type type, Func<object, BsonValue> serialize, Func<BsonValue, object> deserialize)
        => RegisterType(type, (o, _) => serialize(o), (b, _) => deserialize(b));

    /// <summary>
    /// Register a custom type serializer/deserialize function with access to the current mapper instance.
    /// </summary>
    public void RegisterType(Type type, Func<object, BsonMapper, BsonValue> serialize, Func<BsonValue, BsonMapper, object> deserialize)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));
        if (serialize == null) throw new ArgumentNullException(nameof(serialize));
        if (deserialize == null) throw new ArgumentNullException(nameof(deserialize));

        _customSerializer[type] = serialize;
        _customDeserializer[type] = deserialize;
    }

    /// <summary>
    /// Register a custom serializer/deserialize factory for an open generic type definition.
    /// </summary>
    public void RegisterOpenGenericType(
        Type openGenericType,
        Func<Type, Func<object, BsonMapper, BsonValue>> serializeFactory,
        Func<Type, Func<BsonValue, BsonMapper, object>> deserializeFactory)
    {
        if (openGenericType == null) throw new ArgumentNullException(nameof(openGenericType));
        if (serializeFactory == null) throw new ArgumentNullException(nameof(serializeFactory));
        if (deserializeFactory == null) throw new ArgumentNullException(nameof(deserializeFactory));
        if (!openGenericType.GetTypeInfo().IsGenericTypeDefinition)
        {
            throw new ArgumentException("Type must be an open generic type definition.", nameof(openGenericType));
        }

        _openGenericTypes[openGenericType] = new OpenGenericTypeRegistration(serializeFactory, deserializeFactory);

        foreach (var type in _resolvedOpenGenericSerializer.Keys)
        {
            if (type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == openGenericType)
            {
                _resolvedOpenGenericSerializer.TryRemove(type, out _);
            }
        }

        foreach (var type in _resolvedOpenGenericDeserializer.Keys)
        {
            if (type.GetTypeInfo().IsGenericType && type.GetGenericTypeDefinition() == openGenericType)
            {
                _resolvedOpenGenericDeserializer.TryRemove(type, out _);
            }
        }
    }

    private bool TryGetCustomSerializer(Type declaredType, Type runtimeType, out Func<object, BsonMapper, BsonValue> custom)
    {
        if (declaredType != null && _customSerializer.TryGetValue(declaredType, out custom))
        {
            return true;
        }

        if (runtimeType != null && _customSerializer.TryGetValue(runtimeType, out custom))
        {
            return true;
        }

        if (TryResolveOpenGenericSerializer(declaredType, out custom))
        {
            return true;
        }

        return TryResolveOpenGenericSerializer(runtimeType, out custom);
    }

    private bool TryGetCustomDeserializer(Type type, out Func<BsonValue, BsonMapper, object> custom)
    {
        if (_customDeserializer.TryGetValue(type, out custom))
        {
            return true;
        }

        return TryResolveOpenGenericDeserializer(type, out custom);
    }

    private bool TryResolveOpenGenericSerializer(Type type, out Func<object, BsonMapper, BsonValue> custom)
    {
        custom = null;

        if (type == null)
        {
            return false;
        }

        var typeInfo = type.GetTypeInfo();

        if (!typeInfo.IsGenericType || typeInfo.IsGenericTypeDefinition)
        {
            return false;
        }

        if (_resolvedOpenGenericSerializer.TryGetValue(type, out custom))
        {
            return true;
        }

        var openGenericType = type.GetGenericTypeDefinition();

        if (!_openGenericTypes.TryGetValue(openGenericType, out var registration))
        {
            return false;
        }

        custom = _resolvedOpenGenericSerializer.GetOrAdd(type, t => registration.CreateSerializer(t));

        return true;
    }

    private bool TryResolveOpenGenericDeserializer(Type type, out Func<BsonValue, BsonMapper, object> custom)
    {
        custom = null;

        if (type == null)
        {
            return false;
        }

        var typeInfo = type.GetTypeInfo();

        if (!typeInfo.IsGenericType || typeInfo.IsGenericTypeDefinition)
        {
            return false;
        }

        if (_resolvedOpenGenericDeserializer.TryGetValue(type, out custom))
        {
            return true;
        }

        var openGenericType = type.GetGenericTypeDefinition();

        if (!_openGenericTypes.TryGetValue(openGenericType, out var registration))
        {
            return false;
        }

        custom = _resolvedOpenGenericDeserializer.GetOrAdd(type, t => registration.CreateDeserializer(t));

        return true;
    }

    private sealed class OpenGenericTypeRegistration
    {
        private readonly Func<Type, Func<object, BsonMapper, BsonValue>> _serializeFactory;
        private readonly Func<Type, Func<BsonValue, BsonMapper, object>> _deserializeFactory;

        public OpenGenericTypeRegistration(
            Func<Type, Func<object, BsonMapper, BsonValue>> serializeFactory,
            Func<Type, Func<BsonValue, BsonMapper, object>> deserializeFactory)
        {
            _serializeFactory = serializeFactory;
            _deserializeFactory = deserializeFactory;
        }

        public Func<object, BsonMapper, BsonValue> CreateSerializer(Type closedType)
            => _serializeFactory(closedType) ?? throw new LiteException(LiteException.MAPPING_ERROR, $"Open generic serializer factory returned null for {closedType.FullName}.");

        public Func<BsonValue, BsonMapper, object> CreateDeserializer(Type closedType)
            => _deserializeFactory(closedType) ?? throw new LiteException(LiteException.MAPPING_ERROR, $"Open generic deserializer factory returned null for {closedType.FullName}.");
    }

    #endregion

    #region Get LinqVisitor processor

    /// <summary>
    /// Resolve LINQ expression into BsonExpression
    /// </summary>
    public BsonExpression GetExpression<T, K>(Expression<Func<T, K>> predicate)
    {
        var visitor = new LinqExpressionVisitor(this, predicate);

        var expr = visitor.Resolve(typeof(K) == typeof(bool));

        LOG($"`{predicate}` -> `{expr.Source}`", "LINQ");

        return expr;
    }

    /// <summary>
    /// Resolve LINQ expression into BsonExpression (for index only)
    /// </summary>
    public BsonExpression GetIndexExpression<T, K>(Expression<Func<T, K>> predicate)
    {
        var visitor = new LinqExpressionVisitor(this, predicate);

        var expr = visitor.Resolve(false);

        LOG($"`{predicate}` -> `{expr.Source}`", "LINQ");

        return expr;
    }

    #endregion

    #region Predefinded Property Resolvers

    /// <summary>
    /// Use lower camel case resolution for convert property names to field names
    /// </summary>
    public BsonMapper UseCamelCase()
    {
        ResolveFieldName = s => char.ToLower(s[0]) + s.Substring(1);

        return this;
    }

    private readonly Regex _lowerCaseDelimiter = new("(?!(^[A-Z]))([A-Z])", RegexOptions.Compiled);

    /// <summary>
    /// Uses lower camel case with delimiter to convert property names to field names
    /// </summary>
    public BsonMapper UseLowerCaseDelimiter(char delimiter = '_')
    {
        ResolveFieldName = s => _lowerCaseDelimiter.Replace(s, delimiter + "$2").ToLower();

        return this;
    }

    #endregion

    #region Register DbRef

    /// <summary>
    /// Register a property mapper as DbRef to serialize/deserialize only document reference _id
    /// </summary>
    internal static void RegisterDbRef(BsonMapper mapper, MemberMapper member, ITypeNameBinder typeNameBinder, string collection)
    {
        member.IsDbRef = true;

        if (member.IsEnumerable)
        {
            RegisterDbRefList(mapper, member, typeNameBinder, collection);
        }
        else
        {
            RegisterDbRefItem(mapper, member, typeNameBinder, collection);
        }
    }

    /// <summary>
    /// Register a property as a DbRef - implement a custom Serialize/Deserialize actions to convert entity to $id, $ref only
    /// </summary>
    private static void RegisterDbRefItem(BsonMapper mapper, MemberMapper member, ITypeNameBinder typeNameBinder, string collection)
    {
        // get entity
        var entity = mapper.GetEntityMapper(member.DataType);

        member.Serialize = (obj, m) =>
        {
            // supports null values when "SerializeNullValues = true"
            if (obj == null)
            {
                return BsonValue.Null;
            }

            entity.WaitForInitialization();

            var idField = entity.Id;

            // #768 if using DbRef with interface with no ID mapped
            if (idField == null)
            {
                throw new LiteException(0, "There is no _id field mapped in your type: " + member.DataType.FullName);
            }

            var id = idField.Getter(obj);

            var bsonDocument = new BsonDocument
            {
                ["$id"] = m.Serialize(id.GetType(), id, 0),
                ["$ref"] = collection
            };

            if (member.DataType != obj.GetType())
            {
                bsonDocument["$type"] = typeNameBinder.GetName(obj.GetType());
            }

            return bsonDocument;
        };

        member.Deserialize = (bson, m) =>
        {
            // if not a document (maybe BsonValue.null) returns null
            if (bson == null || !bson.IsDocument)
            {
                return null;
            }

            var doc = bson.AsDocument;
            var idRef = doc["$id"];
            var missing = doc["$missing"] == true;
            var included = !doc.ContainsKey("$ref");

            if (missing)
            {
                return null;
            }

            if (included)
            {
                doc["_id"] = idRef;

                if (doc.ContainsKey("$type"))
                {
                    doc["_type"] = bson["$type"];
                }

                return m.Deserialize(entity.ForType, doc);
            }

            return m.Deserialize(entity.ForType,
                doc.ContainsKey("$type") ? new BsonDocument { ["_id"] = idRef, ["_type"] = bson["$type"] } : new BsonDocument { ["_id"] = idRef }); // if has $id, deserialize object using only _id object
        };
    }

    /// <summary>
    /// Register a property as a DbRefList - implement a custom Serialize/Deserialize actions to convert entity to $id, $ref
    /// only
    /// </summary>
    private static void RegisterDbRefList(BsonMapper mapper, MemberMapper member, ITypeNameBinder typeNameBinder, string collection)
    {
        // get entity from list item type
        var entity = mapper.GetEntityMapper(member.UnderlyingType);

        member.Serialize = (list, m) =>
        {
            // supports null values when "SerializeNullValues = true"
            if (list == null)
            {
                return BsonValue.Null;
            }

            entity.WaitForInitialization();

            var result = new BsonArray();
            var idField = entity.Id;

            foreach (var item in (IEnumerable)list)
            {
                if (item == null)
                {
                    continue;
                }

                var id = idField.Getter(item);

                var bsonDocument = new BsonDocument
                {
                    ["$id"] = m.Serialize(id.GetType(), id, 0),
                    ["$ref"] = collection
                };

                if (member.UnderlyingType != item.GetType())
                {
                    bsonDocument["$type"] = typeNameBinder.GetName(item.GetType());
                }

                result.Add(bsonDocument);
            }

            return result;
        };

        member.Deserialize = (bson, m) =>
        {
            if (!bson.IsArray)
            {
                return null;
            }

            var array = bson.AsArray;

            if (array.Count == 0)
            {
                return m.Deserialize(member.DataType, array);
            }

            // copy array changing $id to _id
            var result = new BsonArray();

            foreach (var item in array)
            {
                if (!item.IsDocument)
                {
                    continue;
                }

                var doc = item.AsDocument;
                var idRef = doc["$id"];
                var missing = doc["$missing"] == true;
                var included = !doc.ContainsKey("$ref");

                // if referece document are missing, do not inlcude on output list
                if (missing)
                {
                    continue;
                }

                // if refId is null was included by "include" query, so "item" is full filled document
                if (included)
                {
                    item["_id"] = idRef;

                    if (item.AsDocument.ContainsKey("$type"))
                    {
                        item["_type"] = item["$type"];
                    }

                    result.Add(item);
                }
                else
                {
                    var bsonDocument = new BsonDocument { ["_id"] = idRef };

                    if (item.AsDocument.ContainsKey("$type"))
                    {
                        bsonDocument["_type"] = item["$type"];
                    }

                    result.Add(bsonDocument);
                }
            }

            return m.Deserialize(member.DataType, result);
        };
    }

    #endregion
}