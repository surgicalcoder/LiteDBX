using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class OpenGenericType_Tests
{
    [Fact]
    public void Open_Generic_Type_Can_Serialize_Item_Using_Existing_Mapper()
    {
        var mapper = new BsonMapper();
        RegisterItemOnlyWrapper(
            mapper,
            (itemType, item, m) => m.Serialize(itemType, item),
            (itemType, bson, m) => m.Deserialize(itemType, bson));

        var wrapper = new GenericRef<PersonPayload>
        {
            Id = "ref-1",
            Item = new PersonPayload { Name = "John", Age = 39 }
        };

        var bson = mapper.Serialize(wrapper);

        bson.IsDocument.Should().BeTrue();
        bson["Name"].AsString.Should().Be("John");
        bson["Age"].AsInt32.Should().Be(39);
        bson.AsDocument.ContainsKey(nameof(GenericRef<PersonPayload>.Id)).Should().BeFalse();

        var roundTrip = mapper.Deserialize<GenericRef<PersonPayload>>(bson);

        roundTrip.Id.Should().BeNull();
        roundTrip.Item.Should().NotBeNull();
        roundTrip.Item.Name.Should().Be("John");
        roundTrip.Item.Age.Should().Be(39);
    }

    [Fact]
    public void Open_Generic_Type_Can_Serialize_Item_Without_Type_Metadata()
    {
        var mapper = new BsonMapper();
        RegisterItemOnlyWrapper(
            mapper,
            (_, item, m) => item == null ? BsonValue.Null : m.Serialize(item.GetType(), item),
            (itemType, bson, m) => m.Deserialize(itemType, bson));

        var wrapper = new GenericRef<AnimalPayload>
        {
            Id = "ref-2",
            Item = new DogPayload { Name = "Rex", Breed = "Labrador" }
        };

        var bson = mapper.Serialize(wrapper);

        bson.IsDocument.Should().BeTrue();
        bson.AsDocument.ContainsKey("_type").Should().BeFalse();
        bson["Name"].AsString.Should().Be("Rex");
        bson["Breed"].AsString.Should().Be("Labrador");

        var roundTrip = mapper.Deserialize<GenericRef<AnimalPayload>>(bson);

        roundTrip.Item.Should().BeOfType<AnimalPayload>();
        roundTrip.Item.Name.Should().Be("Rex");
    }

    [Fact]
    public void Open_Generic_Type_Can_Use_Custom_Item_Mapping()
    {
        var mapper = new BsonMapper();
        RegisterItemOnlyWrapper(
            mapper,
            (_, item, _) => item == null ? BsonValue.Null : new BsonValue(((PersonPayload)item).Name.ToUpperInvariant()),
            (_, bson, _) => new PersonPayload { Name = bson.AsString, Age = 0 });

        var wrapper = new GenericRef<PersonPayload>
        {
            Id = "ref-3",
            Item = new PersonPayload { Name = "maria", Age = 31 }
        };

        var bson = mapper.Serialize(wrapper);
        var roundTrip = mapper.Deserialize<GenericRef<PersonPayload>>(bson);

        bson.IsString.Should().BeTrue();
        bson.AsString.Should().Be("MARIA");
        roundTrip.Item.Should().NotBeNull();
        roundTrip.Item.Name.Should().Be("MARIA");
        roundTrip.Item.Age.Should().Be(0);
    }

    [Fact]
    public void Exact_Closed_Type_Registration_Overrides_Open_Generic_Registration()
    {
        var mapper = new BsonMapper();
        RegisterItemOnlyWrapper(
            mapper,
            (itemType, item, m) => m.Serialize(itemType, item),
            (itemType, bson, m) => m.Deserialize(itemType, bson));

        mapper.RegisterType(typeof(GenericRef<PersonPayload>),
            (obj, _) => new BsonDocument { [nameof(GenericRef<PersonPayload>.Id)] = ((GenericRef<PersonPayload>)obj).Id },
            (bson, _) => new GenericRef<PersonPayload> { Id = bson.AsDocument[nameof(GenericRef<PersonPayload>.Id)].AsString });

        var wrapper = new GenericRef<PersonPayload>
        {
            Id = "override",
            Item = new PersonPayload { Name = "John", Age = 39 }
        };

        var bson = mapper.Serialize(wrapper);
        var roundTrip = mapper.Deserialize<GenericRef<PersonPayload>>(bson);

        bson.IsDocument.Should().BeTrue();
        bson.AsDocument.ContainsKey(nameof(GenericRef<PersonPayload>.Id)).Should().BeTrue();
        bson.AsDocument.ContainsKey(nameof(GenericRef<PersonPayload>.Item)).Should().BeFalse();
        roundTrip.Id.Should().Be("override");
        roundTrip.Item.Should().BeNull();
    }

    [Fact]
    public void Open_Generic_Type_Factories_Are_Cached_Per_Closed_Type()
    {
        var mapper = new BsonMapper();
        var serializerFactoryCalls = 0;
        var deserializerFactoryCalls = 0;

        mapper.RegisterOpenGenericType(
            typeof(GenericRef<>),
            closedType =>
            {
                serializerFactoryCalls++;
                return CreateItemSerializer(closedType, (itemType, item, m) => m.Serialize(itemType, item));
            },
            closedType =>
            {
                deserializerFactoryCalls++;
                return CreateItemDeserializer(closedType, (itemType, bson, m) => m.Deserialize(itemType, bson));
            });

        var personWrapper = new GenericRef<PersonPayload> { Item = new PersonPayload { Name = "Ana", Age = 22 } };
        var stringWrapper = new GenericRef<string> { Item = "value" };

        var personBson1 = mapper.Serialize(personWrapper);
        var personBson2 = mapper.Serialize(personWrapper);
        var stringBson1 = mapper.Serialize(stringWrapper);
        var stringBson2 = mapper.Serialize(stringWrapper);

        mapper.Deserialize<GenericRef<PersonPayload>>(personBson1);
        mapper.Deserialize<GenericRef<PersonPayload>>(personBson2);
        mapper.Deserialize<GenericRef<string>>(stringBson1);
        mapper.Deserialize<GenericRef<string>>(stringBson2);

        serializerFactoryCalls.Should().Be(2);
        deserializerFactoryCalls.Should().Be(2);
    }

    [Fact]
    public async Task Open_Generic_Type_Round_Trips_Through_Database()
    {
        var mapper = new BsonMapper();
        RegisterItemOnlyWrapper(
            mapper,
            (itemType, item, m) => m.Serialize(itemType, item),
            (itemType, bson, m) => m.Deserialize(itemType, bson));

        await using var db = await LiteDatabase.Open(new MemoryStream(), mapper, new MemoryStream());
        var collection = db.GetCollection<WrapperHolder>("wrappers");
        var rawCollection = db.GetCollection("wrappers");

        var entity = new WrapperHolder
        {
            Id = 1,
            Reference = new GenericRef<PersonPayload>
            {
                Id = "ignored",
                Item = new PersonPayload { Name = "Ana", Age = 25 }
            }
        };

        await collection.Insert(entity);

        var raw = await rawCollection.FindById(1);
        var roundTrip = await collection.FindById(1);

        Assert.NotNull(raw);
        raw["Reference"].IsDocument.Should().BeTrue();
        raw["Reference"]["Name"].AsString.Should().Be("Ana");
        raw["Reference"].AsDocument.ContainsKey(nameof(GenericRef<PersonPayload>.Id)).Should().BeFalse();

        roundTrip.Should().NotBeNull();
        roundTrip.Reference.Should().NotBeNull();
        roundTrip.Reference.Id.Should().BeNull();
        roundTrip.Reference.Item.Should().NotBeNull();
        roundTrip.Reference.Item.Name.Should().Be("Ana");
        roundTrip.Reference.Item.Age.Should().Be(25);
    }

    private static void RegisterItemOnlyWrapper(
        BsonMapper mapper,
        Func<Type, object, BsonMapper, BsonValue> serializeItem,
        Func<Type, BsonValue, BsonMapper, object> deserializeItem)
    {
        mapper.RegisterOpenGenericType(
            typeof(GenericRef<>),
            closedType => CreateItemSerializer(closedType, serializeItem),
            closedType => CreateItemDeserializer(closedType, deserializeItem));
    }

    private static Func<object, BsonMapper, BsonValue> CreateItemSerializer(
        Type closedType,
        Func<Type, object, BsonMapper, BsonValue> serializeItem)
    {
        var itemType = closedType.GetGenericArguments()[0];
        var itemProperty = closedType.GetProperty(nameof(GenericRef<object>.Item))
            ?? throw new InvalidOperationException($"Item property was not found on {closedType.FullName}.");

        return (obj, mapper) => serializeItem(itemType, itemProperty.GetValue(obj), mapper);
    }

    private static Func<BsonValue, BsonMapper, object> CreateItemDeserializer(
        Type closedType,
        Func<Type, BsonValue, BsonMapper, object> deserializeItem)
    {
        var itemType = closedType.GetGenericArguments()[0];
        var itemProperty = closedType.GetProperty(nameof(GenericRef<object>.Item))
            ?? throw new InvalidOperationException($"Item property was not found on {closedType.FullName}.");

        return (bson, mapper) =>
        {
            var wrapper = Activator.CreateInstance(closedType);
            itemProperty.SetValue(wrapper, deserializeItem(itemType, bson, mapper));
            return wrapper;
        };
    }

    public class GenericRef<T>
    {
        public string Id { get; set; }
        public T Item { get; set; }
    }

    public class WrapperHolder
    {
        public int Id { get; set; }
        public GenericRef<PersonPayload> Reference { get; set; }
    }

    public class PersonPayload
    {
        public string Name { get; set; }
        public int Age { get; set; }
    }

    public class AnimalPayload
    {
        public string Name { get; set; }
    }

    public class DogPayload : AnimalPayload
    {
        public string Breed { get; set; }
    }
}

