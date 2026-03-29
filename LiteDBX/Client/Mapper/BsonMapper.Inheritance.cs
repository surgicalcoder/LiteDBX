using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace LiteDbX;

public partial class BsonMapper
{
    private readonly ConcurrentDictionary<Type, InheritedTypeConventionSet> _inheritedTypeConventions = new();

    /// <summary>
    /// Configure inheritance-aware member conventions for a base type.
    /// </summary>
    public InheritedEntityBuilder<TBase> Inheritance<TBase>()
        => new(this);

    internal MemberInfo GetMemberFromExpression<TBase, TMember>(Expression<Func<TBase, TMember>> member)
    {
        if (member == null)
        {
            throw new ArgumentNullException(nameof(member));
        }

        var memberInfo = member.GetMemberInfo();

        if (memberInfo == null)
        {
            throw new ArgumentException($"Expression '{member}' must target a direct field or property.", nameof(member));
        }

        if (memberInfo.DeclaringType == null || !memberInfo.DeclaringType.IsAssignableFrom(typeof(TBase)))
        {
            throw new ArgumentException($"Member '{memberInfo.Name}' must belong to type '{typeof(TBase).FullName}' or one of its base types.", nameof(member));
        }

        return memberInfo;
    }

    internal MemberInfo GetInheritedDefaultIdMember(Type baseType)
    {
        var member = GetIdMember(GetTypeMembers(baseType));

        if (member == null)
        {
            throw new LiteException(LiteException.MAPPING_ERROR, $"Type '{baseType.FullName}' does not contain a conventional id member.");
        }

        return member;
    }

    internal void RegisterInheritedIdConvention(Type baseType, MemberInfo member, BsonType storageType, bool autoId)
    {
        if (baseType == null) throw new ArgumentNullException(nameof(baseType));
        if (member == null) throw new ArgumentNullException(nameof(member));

        EnsureInheritanceConventionRegistrationAllowed(baseType);

        var memberType = GetMemberType(member);
        ValidateInheritedIdConfiguration(baseType, member, memberType, storageType, autoId);

        var set = _inheritedTypeConventions.GetOrAdd(baseType, static type => new InheritedTypeConventionSet(type));

        if (set.IdConvention != null)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"An inherited id convention is already registered for base type '{baseType.FullName}'.");
        }

        if (set.TryGetMemberRule(member.Name, out var rule) && (rule.Ignore || rule.HasSerializer))
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' cannot be configured as inherited id because it already has another inherited convention.");
        }

        set.IdConvention = new InheritedIdConvention(member, memberType, storageType, autoId);
    }

    internal void RegisterInheritedIgnoreConvention(Type baseType, MemberInfo member)
    {
        if (baseType == null) throw new ArgumentNullException(nameof(baseType));
        if (member == null) throw new ArgumentNullException(nameof(member));

        EnsureInheritanceConventionRegistrationAllowed(baseType);

        var set = _inheritedTypeConventions.GetOrAdd(baseType, static type => new InheritedTypeConventionSet(type));

        if (set.IdConvention?.Matches(member) == true)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' is already configured as inherited id and cannot also be ignored.");
        }

        var rule = set.GetOrAddRule(member);

        if (rule.HasSerializer)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' already has an inherited serializer and cannot also be ignored.");
        }

        if (rule.Ignore)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' is already configured to be ignored.");
        }

        rule.Ignore = true;
    }

    internal void RegisterInheritedSerializationConvention(Type baseType, MemberInfo member,
        Func<object, BsonMapper, BsonValue> serialize,
        Func<BsonValue, BsonMapper, object> deserialize)
    {
        if (baseType == null) throw new ArgumentNullException(nameof(baseType));
        if (member == null) throw new ArgumentNullException(nameof(member));
        if (serialize == null) throw new ArgumentNullException(nameof(serialize));
        if (deserialize == null) throw new ArgumentNullException(nameof(deserialize));

        EnsureInheritanceConventionRegistrationAllowed(baseType);

        var set = _inheritedTypeConventions.GetOrAdd(baseType, static type => new InheritedTypeConventionSet(type));

        if (set.IdConvention?.Matches(member) == true)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' is already configured as inherited id and cannot also have a custom serializer.");
        }

        var rule = set.GetOrAddRule(member);

        if (rule.Ignore)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' is configured to be ignored and cannot also have a custom serializer.");
        }

        if (rule.HasSerializer)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Member '{member.Name}' on base type '{baseType.FullName}' already has an inherited serializer configured.");
        }

        rule.Serialize = serialize;
        rule.Deserialize = deserialize;
    }

    internal InheritedIdConvention ResolveInheritedIdConvention(Type entityType, IEnumerable<MemberInfo> members)
    {
        InheritedIdConvention resolved = null;

        foreach (var set in GetApplicableInheritedTypeConventions(entityType))
        {
            var convention = set.IdConvention;

            if (convention == null)
            {
                continue;
            }

            if (members.Any(member => convention.Matches(member)))
            {
                resolved = convention;
            }
        }

        return resolved;
    }

    internal InheritedMemberRule ResolveInheritedMemberRule(Type entityType, MemberInfo member)
    {
        InheritedMemberRule resolved = null;

        foreach (var set in GetApplicableInheritedTypeConventions(entityType))
        {
            foreach (var rule in set.MemberRules.Values)
            {
                if (rule.Matches(member))
                {
                    resolved = rule;
                }
            }
        }

        return resolved;
    }

    internal BsonValue SerializeMemberValue(MemberMapper member, object value, int depth, bool preserveDirectStringValue = false)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));

        value = NormalizeValueForMemberSerialization(member, value);

        if (member.Serialize != null)
        {
            return member.Serialize(value, this);
        }

        if (member.StorageType.HasValue)
        {
            return SerializeToStorageType(member.DataType, value, member.StorageType.Value);
        }

        if (preserveDirectStringValue && value is string str)
        {
            return new BsonValue(str);
        }

        return Serialize(member.DataType, value, depth);
    }

    internal object DeserializeMemberValue(MemberMapper member, BsonValue value)
    {
        if (member == null) throw new ArgumentNullException(nameof(member));

        if (member.Deserialize != null)
        {
            return member.Deserialize(value, this);
        }

        return Deserialize(member.DataType, value);
    }

    internal bool TrySerializeExpressionValue(MemberMapper member, object value, out BsonValue bson, bool preserveDirectStringValue = false)
    {
        if (member == null)
        {
            bson = null;
            return false;
        }

        if (value == null)
        {
            bson = BsonValue.Null;
            return true;
        }

        var normalizedValue = NormalizeValueForMemberSerialization(member, value);
        var targetType = Reflection.IsNullable(member.DataType)
            ? Reflection.UnderlyingTypeOf(member.DataType)
            : member.DataType;

        if (normalizedValue != null && targetType.IsInstanceOfType(normalizedValue))
        {
            bson = SerializeMemberValue(member, normalizedValue, 0, preserveDirectStringValue);
            return true;
        }

        if (member.StorageType.HasValue)
        {
            try
            {
                bson = SerializeToStorageType(member.DataType, value, member.StorageType.Value);
                return true;
            }
            catch (Exception)
            {
                // fall back to default constant serialization when the raw value can't be coerced to the member's storage type
            }
        }

        bson = null;
        return false;
    }

    internal bool RequiresMemberAwareSerialization(MemberMapper member)
        => member != null &&
           (
               member.StorageType.HasValue ||
               member.Serialize != null ||
               member.Deserialize != null ||
               TryGetCustomSerializer(member.DataType, member.DataType, out _) ||
               TryGetCustomDeserializer(member.DataType, out _)
           );

    private object NormalizeValueForMemberSerialization(MemberMapper member, object value)
    {
        if (member == null || value == null)
        {
            return value;
        }

        var targetType = Reflection.IsNullable(member.DataType)
            ? Reflection.UnderlyingTypeOf(member.DataType)
            : member.DataType;

        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if (!TryExtractStringId(value, out var id) || string.IsNullOrWhiteSpace(id))
        {
            return value;
        }

        var stringConstructor = targetType.GetConstructor(new[] { typeof(string) });

        if (stringConstructor != null)
        {
            return stringConstructor.Invoke(new object[] { id });
        }

        var idProperty = targetType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);

        if (idProperty?.CanWrite == true && idProperty.PropertyType == typeof(string))
        {
            var instance = Activator.CreateInstance(targetType);

            if (instance != null)
            {
                idProperty.SetValue(instance, id);
                return instance;
            }
        }

        return value;
    }

    private static bool TryExtractStringId(object value, out string id)
    {
        switch (value)
        {
            case null:
                id = null;
                return false;

            case string stringValue:
                id = stringValue;
                return true;
        }

        var idProperty = value.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);

        if (idProperty?.CanRead == true && idProperty.PropertyType == typeof(string))
        {
            id = (string)idProperty.GetValue(value);
            return true;
        }

        id = null;
        return false;
    }

    internal BsonValue SerializeToStorageType(Type memberType, object value, BsonType storageType)
    {
        if (value == null)
        {
            return BsonValue.Null;
        }

        var targetType = Reflection.IsNullable(memberType)
            ? Reflection.UnderlyingTypeOf(memberType)
            : memberType;

        switch (storageType)
        {
            case BsonType.String:
                return new BsonValue(ConvertValueToString(value, targetType));

            case BsonType.ObjectId:
                return new BsonValue(ConvertValueToObjectId(value, targetType));

            case BsonType.Guid:
                return new BsonValue(ConvertValueToGuid(value, targetType));

            case BsonType.Int32:
                return new BsonValue(ConvertValueToInt32(value, targetType));

            case BsonType.Int64:
                return new BsonValue(ConvertValueToInt64(value, targetType));

            default:
                throw new LiteException(LiteException.MAPPING_ERROR,
                    $"Inherited id storage type '{storageType}' is not supported for automatic member conversion.");
        }
    }

    internal object DeserializeFromStorageType(Type memberType, BsonValue value, BsonType storageType)
    {
        if (value == null || value.IsNull)
        {
            return null;
        }

        var targetType = Reflection.IsNullable(memberType)
            ? Reflection.UnderlyingTypeOf(memberType)
            : memberType;

        switch (storageType)
        {
            case BsonType.String:
                return targetType == typeof(string) ? value.AsString : Deserialize(memberType, value);

            case BsonType.ObjectId:
                if (targetType == typeof(string)) return value.AsObjectId.ToString();
                if (targetType == typeof(ObjectId)) return value.AsObjectId;
                break;

            case BsonType.Guid:
                if (targetType == typeof(string)) return value.AsGuid.ToString();
                if (targetType == typeof(Guid)) return value.AsGuid;
                break;

            case BsonType.Int32:
                if (targetType == typeof(string)) return value.AsInt32.ToString();
                if (targetType == typeof(int)) return value.AsInt32;
                break;

            case BsonType.Int64:
                if (targetType == typeof(string)) return value.AsInt64.ToString();
                if (targetType == typeof(long)) return value.AsInt64;
                break;
        }

        return Deserialize(memberType, value);
    }

    private IEnumerable<InheritedTypeConventionSet> GetApplicableInheritedTypeConventions(Type entityType)
        => _inheritedTypeConventions.Values
            .Where(x => x.BaseType.IsAssignableFrom(entityType))
            .OrderByDescending(x => GetInheritanceDistance(entityType, x.BaseType));

    private void EnsureInheritanceConventionRegistrationAllowed(Type baseType)
    {
        var mappedType = _entities.Keys.FirstOrDefault(baseType.IsAssignableFrom);

        if (mappedType != null)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Inheritance conventions for base type '{baseType.FullName}' must be registered before type '{mappedType.FullName}' is mapped.");
        }
    }

    private void ValidateInheritedIdConfiguration(Type baseType, MemberInfo member, Type memberType, BsonType storageType, bool autoId)
    {
        var supportedStorageType = storageType == BsonType.String ||
                                   storageType == BsonType.ObjectId ||
                                   storageType == BsonType.Guid ||
                                   storageType == BsonType.Int32 ||
                                   storageType == BsonType.Int64;

        if (!supportedStorageType)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Inherited id storage type '{storageType}' is not supported for member '{member.Name}' on '{baseType.FullName}'.");
        }

        var targetType = Reflection.IsNullable(memberType)
            ? Reflection.UnderlyingTypeOf(memberType)
            : memberType;

        var compatible = targetType == typeof(string) ||
                         (targetType == typeof(ObjectId) && storageType == BsonType.ObjectId) ||
                         (targetType == typeof(Guid) && storageType == BsonType.Guid) ||
                         (targetType == typeof(int) && storageType == BsonType.Int32) ||
                         (targetType == typeof(long) && storageType == BsonType.Int64);

        if (!compatible)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Inherited id member '{member.Name}' on '{baseType.FullName}' with CLR type '{memberType.FullName}' cannot be stored as BSON '{storageType}'.");
        }

        if (autoId && storageType != BsonType.ObjectId && storageType != BsonType.Guid && storageType != BsonType.Int32 && storageType != BsonType.Int64)
        {
            throw new LiteException(LiteException.MAPPING_ERROR,
                $"Inherited id member '{member.Name}' on '{baseType.FullName}' cannot use AutoId with storage type '{storageType}'.");
        }
    }

    private static int GetInheritanceDistance(Type entityType, Type baseType)
    {
        var distance = 0;
        var current = entityType;

        while (current != null)
        {
            if (current == baseType)
            {
                return distance;
            }

            current = current.BaseType;
            distance++;
        }

        return int.MaxValue;
    }

    private static Type GetMemberType(MemberInfo member)
        => member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;

    private static string ConvertValueToString(object value, Type targetType)
    {
        if (value is string str)
        {
            return str;
        }

        if (targetType == typeof(ObjectId) && value is ObjectId objectId)
        {
            return objectId.ToString();
        }

        if (targetType == typeof(Guid) && value is Guid guid)
        {
            return guid.ToString();
        }

        return value.ToString();
    }

    private static ObjectId ConvertValueToObjectId(object value, Type targetType)
    {
        if (value is ObjectId objectId)
        {
            return objectId;
        }

        if (targetType == typeof(string) && value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return null;
            }

            try
            {
                return new ObjectId(str);
            }
            catch (Exception ex)
            {
                throw new LiteException(LiteException.MAPPING_ERROR, $"Value '{str}' is not a valid ObjectId string.", ex);
            }
        }

        throw new LiteException(LiteException.MAPPING_ERROR,
            $"Cannot convert CLR value of type '{value.GetType().FullName}' to BSON ObjectId.");
    }

    private static Guid ConvertValueToGuid(object value, Type targetType)
    {
        if (value is Guid guid)
        {
            return guid;
        }

        if (targetType == typeof(string) && value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return Guid.Empty;
            }

            if (Guid.TryParse(str, out var parsed))
            {
                return parsed;
            }

            throw new LiteException(LiteException.MAPPING_ERROR, $"Value '{str}' is not a valid Guid string.");
        }

        throw new LiteException(LiteException.MAPPING_ERROR,
            $"Cannot convert CLR value of type '{value.GetType().FullName}' to BSON Guid.");
    }

    private static int ConvertValueToInt32(object value, Type targetType)
    {
        if (targetType == typeof(string) && value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return 0;
            }

            if (int.TryParse(str, out var parsed))
            {
                return parsed;
            }

            throw new LiteException(LiteException.MAPPING_ERROR, $"Value '{str}' is not a valid Int32 string.");
        }

        return Convert.ToInt32(value);
    }

    private static long ConvertValueToInt64(object value, Type targetType)
    {
        if (targetType == typeof(string) && value is string str)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return 0L;
            }

            if (long.TryParse(str, out var parsed))
            {
                return parsed;
            }

            throw new LiteException(LiteException.MAPPING_ERROR, $"Value '{str}' is not a valid Int64 string.");
        }

        return Convert.ToInt64(value);
    }

    internal sealed class InheritedTypeConventionSet
    {
        public InheritedTypeConventionSet(Type baseType)
        {
            BaseType = baseType;
        }

        public Type BaseType { get; }

        public InheritedIdConvention IdConvention { get; set; }

        public Dictionary<string, InheritedMemberRule> MemberRules { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGetMemberRule(string memberName, out InheritedMemberRule rule)
            => MemberRules.TryGetValue(memberName, out rule);

        public InheritedMemberRule GetOrAddRule(MemberInfo member)
        {
            if (!MemberRules.TryGetValue(member.Name, out var rule))
            {
                rule = new InheritedMemberRule(member);
                MemberRules.Add(member.Name, rule);
            }

            return rule;
        }
    }

    internal sealed class InheritedIdConvention
    {
        public InheritedIdConvention(MemberInfo member, Type memberType, BsonType storageType, bool autoId)
        {
            Member = member;
            MemberType = memberType;
            StorageType = storageType;
            AutoId = autoId;
        }

        public MemberInfo Member { get; }

        public Type MemberType { get; }

        public BsonType StorageType { get; }

        public bool AutoId { get; }

        public bool Matches(MemberInfo member)
            => InheritedMemberRule.MemberMatches(member, Member);
    }

    internal sealed class InheritedMemberRule
    {
        public InheritedMemberRule(MemberInfo member)
        {
            Member = member;
        }

        public MemberInfo Member { get; }

        public bool Ignore { get; set; }

        public Func<object, BsonMapper, BsonValue> Serialize { get; set; }

        public Func<BsonValue, BsonMapper, object> Deserialize { get; set; }

        public bool HasSerializer => Serialize != null || Deserialize != null;

        public bool Matches(MemberInfo member)
            => MemberMatches(member, Member);

        public static bool MemberMatches(MemberInfo actual, MemberInfo configured)
        {
            if (actual == null || configured == null)
            {
                return false;
            }

            if (!string.Equals(actual.Name, configured.Name, StringComparison.OrdinalIgnoreCase) || actual.MemberType != configured.MemberType)
            {
                return false;
            }

            if (actual is PropertyInfo actualProperty && configured is PropertyInfo configuredProperty)
            {
                if (actualProperty.PropertyType != configuredProperty.PropertyType)
                {
                    return false;
                }

                var actualGetter = actualProperty.GetMethod;
                var configuredGetter = configuredProperty.GetMethod;

                if (actualGetter != null && configuredGetter != null && actualGetter.GetBaseDefinition() == configuredGetter.GetBaseDefinition())
                {
                    return true;
                }

                return actualProperty.DeclaringType == configuredProperty.DeclaringType;
            }

            if (actual is FieldInfo actualField && configured is FieldInfo configuredField)
            {
                return actualField.FieldType == configuredField.FieldType &&
                       actualField.DeclaringType == configuredField.DeclaringType;
            }

            return false;
        }
    }
}
