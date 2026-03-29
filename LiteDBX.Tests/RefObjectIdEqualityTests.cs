using System;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests;

public class RefObjectIdEqualityTests
{
    private const string ScopeId = "68fdd5525324ff2610c43640";

    [Fact]
    public void GetExpression_Ref_String_Comparisons_Use_Stored_ObjectId_Field()
    {
        var mapper = new RefObjectIdMapper();

        AssertExpression(mapper.GetExpression<ScopedChildEntity, bool>(entity => entity.Scope == ScopeId), " = ");
        AssertExpression(mapper.GetExpression<ScopedChildEntity, bool>(entity => ScopeId == entity.Scope), " = ");
        AssertExpression(mapper.GetExpression<ScopedChildEntity, bool>(entity => entity.Scope.Id == ScopeId), " = ");
        AssertExpression(mapper.GetExpression<ScopedChildEntity, bool>(entity => entity.Scope != ScopeId), " != ");
    }

    [Fact]
    public void GetExpression_Boolean_Constant_And_Ref_Comparison_Does_Not_Create_Bare_Parameter_Predicate()
    {
        var mapper = new RefObjectIdMapper();
        var always = true;

        var expression = mapper.GetExpression<ScopedChildEntity, bool>(entity => always && entity.Scope == ScopeId);
        var normalizedSource = expression.Source.Replace(" ", string.Empty);

        normalizedSource.Should().Contain("(@p0)=true");
        expression.Source.Should().Contain("$.Scope");
        expression.Parameters.Count.Should().Be(2);
        expression.Parameters["p0"].IsBoolean.Should().BeTrue();
        expression.Parameters["p0"].AsBoolean.Should().BeTrue();
        expression.Parameters["p1"].IsObjectId.Should().BeTrue();
        expression.Parameters["p1"].AsObjectId.ToString().Should().Be(ScopeId);
    }

    private static void AssertExpression(BsonExpression expression, string expectedOperator)
    {
        var normalizedSource = expression.Source.Replace(" ", string.Empty);
        var normalizedOperator = expectedOperator.Replace(" ", string.Empty);

        expression.Source.Should().Contain("$.Scope");
        expression.Source.Should().NotContain(".Id");
        normalizedSource.Should().Contain(normalizedOperator);
        expression.Parameters["p0"].IsObjectId.Should().BeTrue();
        expression.Parameters["p0"].AsObjectId.ToString().Should().Be(ScopeId);
    }

    private sealed class RefObjectIdMapper : BsonMapper
    {
        public RefObjectIdMapper()
        {
            Inheritance<TestEntity>()
                .Id(entity => entity.Id, BsonType.ObjectId, false);

            RegisterOpenGenericType(
                typeof(TestRef<>),
                serializeFactory: type =>
                {
                    var idProperty = type.GetProperty(nameof(TestRef<TestScope>.Id))
                                     ?? throw new InvalidOperationException($"Id property was not found on {type.FullName}.");

                    return (obj, _) =>
                    {
                        if (obj == null)
                        {
                            return BsonValue.Null;
                        }

                        var id = idProperty.GetValue(obj) as string;
                        return string.IsNullOrWhiteSpace(id)
                            ? BsonValue.Null
                            : new BsonValue(new ObjectId(id));
                    };
                },
                deserializeFactory: type =>
                {
                    var idProperty = type.GetProperty(nameof(TestRef<TestScope>.Id))
                                     ?? throw new InvalidOperationException($"Id property was not found on {type.FullName}.");

                    return (bson, mapper) =>
                    {
                        if (bson == null || bson.IsNull)
                        {
                            return null;
                        }

                        var instance = Activator.CreateInstance(type)
                                       ?? throw new InvalidOperationException($"Could not create instance of {type.FullName}.");

                        idProperty.SetValue(instance, mapper.Deserialize(idProperty.PropertyType, bson));
                        return instance;
                    };
                });
        }
    }

    private abstract class TestEntity
    {
        public string Id { get; set; }
    }

    private sealed class TestScope : TestEntity;

    private sealed class ScopedChildEntity : TestEntity
    {
        public TestRef<TestScope> Scope { get; set; }
    }

    private sealed class TestRef<T> where T : TestEntity, new()
    {
        public TestRef()
        {
        }

        public TestRef(string id)
        {
            Id = id;
        }

        public string Id { get; set; }

        public static implicit operator string(TestRef<T> value) => value?.Id;

        public static implicit operator TestRef<T>(string value) => value == null ? null : new TestRef<T>(value);
    }
}

