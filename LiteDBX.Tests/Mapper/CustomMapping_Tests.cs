using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class CustomMappingCtor_Tests
{
    [Fact]
    public void Custom_Ctor_With_Custom_Id()
    {
        var mapper = new BsonMapper();

        mapper.Entity<UserWithCustomId>()
              .Id(u => u.Key, false);

        var doc = new BsonDocument { ["_id"] = 10, ["name"] = "John" };

        var user = mapper.ToObject<UserWithCustomId>(doc);

        user.Key.Should().Be(10); //     Expected user.Key to be 10, but found 0.
        user.Name.Should().Be("John");
    }

    [Fact]
    public void Custom_Id_In_Interface()
    {
        var mapper = new BsonMapper();

        var obj = new ConcreteClass { CustomId = "myid", Name = "myname" };
        var doc = mapper.Serialize(obj) as BsonDocument;
        Assert.NotNull(doc);
        doc!["_id"].Should().NotBeNull();
        doc["_id"].Should().Be("myid");
        doc["CustomName"].Should().NotBe(BsonValue.Null);
        doc["CustomName"].Should().Be("myname");
        doc["Name"].Should().Be(BsonValue.Null);
        doc.Keys.ExpectCount(2);
    }

    [Fact]
    public void Key_Attribute_Is_Mapped_As_Id_With_AutoId_Disabled()
    {
        var mapper = new BsonMapper();

        var entityMapper = mapper.GetEntityMapper(typeof(UserWithKeyAttribute));

        entityMapper.Id.Should().NotBeNull();
        entityMapper.Id.MemberName.Should().Be(nameof(UserWithKeyAttribute.Key));
        entityMapper.Id.FieldName.Should().Be("_id");
        entityMapper.Id.AutoId.Should().BeFalse();
    }

    [Fact]
    public void Key_Attribute_Serializes_And_Deserializes_As_Id()
    {
        var mapper = new BsonMapper();

        var doc = mapper.Serialize(new UserWithKeyAttribute(42, "John")) as BsonDocument;

        Assert.NotNull(doc);
        doc!["_id"].Should().Be(42);
        doc["Name"].Should().Be("John");
        doc.Keys.ExpectCount(2);

        var hydrated = mapper.ToObject<UserWithKeyAttribute>(new BsonDocument { ["_id"] = 7, ["Name"] = "Jane" });

        hydrated.Key.Should().Be(7);
        hydrated.Name.Should().Be("Jane");
    }

    [Fact]
    public void NotMapped_Attribute_Is_Ignored_During_Mapping_And_Serialization()
    {
        var mapper = new BsonMapper();

        var entityMapper = mapper.GetEntityMapper(typeof(ClassWithNotMappedAttribute));
        entityMapper.Members.Should().NotContain(x => x.MemberName == nameof(ClassWithNotMappedAttribute.Ignore));

        var doc = mapper.Serialize(new ClassWithNotMappedAttribute { Id = 1, Keep = "K", Ignore = "I" }) as BsonDocument;

        Assert.NotNull(doc);
        doc!["_id"].Should().Be(1);
        doc["Keep"].Should().Be("K");
        doc.ContainsKey("Ignore").Should().BeFalse();
        doc.Keys.ExpectCount(2);
    }

    [Fact]
    public void NotMapped_Attribute_On_Inherited_Property_Is_Ignored()
    {
        var mapper = new BsonMapper();

        var doc = mapper.Serialize(new DerivedClassWithInheritedNotMapped { Id = 9, Keep = "keep", Hidden = "secret" }) as BsonDocument;

        Assert.NotNull(doc);
        doc!["_id"].Should().Be(9);
        doc["Keep"].Should().Be("keep");
        doc.ContainsKey("Hidden").Should().BeFalse();
        doc.Keys.ExpectCount(2);
    }

    public class UserWithCustomId
    {
        public UserWithCustomId(int key, string name)
        {
            Key = key;
            Name = name;
        }

        public int Key { get; }
        public string Name { get; }
    }

    public abstract class BaseClass
    {
        [BsonId]
        public string CustomId { get; set; }

        [BsonField("CustomName")]
        public string Name { get; set; }
    }

    public class ConcreteClass : BaseClass { }

    public class UserWithKeyAttribute
    {
        public UserWithKeyAttribute(int key, string name)
        {
            Key = key;
            Name = name;
        }

        [Key]
        public int Key { get; }

        public string Name { get; }
    }

    public class ClassWithNotMappedAttribute
    {
        public int Id { get; set; }

        public string Keep { get; set; }

        [NotMapped]
        public string Ignore { get; set; }
    }

    public abstract class BaseClassWithNotMapped
    {
        public int Id { get; set; }

        [NotMapped]
        public string Hidden { get; set; }
    }

    public class DerivedClassWithInheritedNotMapped : BaseClassWithNotMapped
    {
        public string Keep { get; set; }
    }
}