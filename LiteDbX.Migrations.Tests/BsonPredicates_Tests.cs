using System;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Migrations.Tests;

public class BsonPredicates_Tests
{
    [Fact]
    public void TrimmedEmptyString_ShouldMatchTrimmedWhitespaceOnly()
    {
        var context = CreateContext(new BsonValue("  "));
        var nonEmpty = CreateContext(new BsonValue("  value  "));

        BsonPredicates.TrimmedEmptyString(context).Should().BeTrue();
        BsonPredicates.TrimmedEmptyString(nonEmpty).Should().BeFalse();
    }

    [Fact]
    public void MinValue_And_MaxValue_ShouldMatchBsonSentinels()
    {
        BsonPredicates.MinValue(CreateContext(BsonValue.MinValue)).Should().BeTrue();
        BsonPredicates.MaxValue(CreateContext(BsonValue.MaxValue)).Should().BeTrue();
        BsonPredicates.MinValue(CreateContext(new BsonValue(0))).Should().BeFalse();
        BsonPredicates.MaxValue(CreateContext(new BsonValue("x"))).Should().BeFalse();
    }

    [Fact]
    public void UselessValue_ShouldBeConservative()
    {
        BsonPredicates.UselessValue(CreateContext(BsonValue.Null)).Should().BeTrue();
        BsonPredicates.UselessValue(CreateContext(new BsonArray())).Should().BeTrue();
        BsonPredicates.UselessValue(CreateContext(new BsonValue(Guid.Empty))).Should().BeTrue();
        BsonPredicates.UselessValue(CreateContext(new BsonValue(ObjectId.Empty))).Should().BeTrue();
        BsonPredicates.UselessValue(CreateContext(new BsonValue(0))).Should().BeFalse();
        BsonPredicates.UselessValue(CreateContext(new BsonValue(false))).Should().BeFalse();
    }

    [Fact]
    public void UselessValueAggressive_ShouldIncludeZeroAndFalse()
    {
        BsonPredicates.UselessValueAggressive(CreateContext(new BsonValue(0))).Should().BeTrue();
        BsonPredicates.UselessValueAggressive(CreateContext(new BsonValue(false))).Should().BeTrue();
        BsonPredicates.UselessValueAggressive(CreateContext(new BsonValue("keep"))).Should().BeFalse();
    }

    [Fact]
    public void AnyOf_And_AllOf_ShouldComposePredicates()
    {
        var whitespace = CreateContext(new BsonValue("   "));
        var word = CreateContext(new BsonValue("value"));

        BsonPredicates.AnyOf(BsonPredicates.Null, BsonPredicates.TrimmedEmptyString)(whitespace).Should().BeTrue();
        BsonPredicates.AnyOf(BsonPredicates.Null, BsonPredicates.TrimmedEmptyString)(word).Should().BeFalse();
        BsonPredicates.AllOf(BsonPredicates.IsString, BsonPredicates.TrimmedEmptyString)(whitespace).Should().BeTrue();
        BsonPredicates.AllOf(BsonPredicates.IsString, BsonPredicates.TrimmedEmptyString)(word).Should().BeFalse();
    }

    private static BsonPredicateContext CreateContext(BsonValue value)
        => new(new BsonDocument(), "Value", true, value, "tests", "predicates");
}

