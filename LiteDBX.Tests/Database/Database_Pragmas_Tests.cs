using System;
using System.Globalization;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Database_Pragmas_Tests
{
    [Fact]
    public async Task Database_Pragmas_Get_Set()
    {
        await using var db = await LiteDatabase.Open(":memory:");

        db.Timeout.TotalSeconds.Should().Be(60.0);
        db.UtcDate.Should().Be(false);
        db.Collation.SortOptions.Should().Be(CompareOptions.IgnoreCase);
        db.LimitSize.Should().Be(long.MaxValue);
        db.UserVersion.Should().Be(0);
        db.CheckpointSize.Should().Be(1000);

        // changing values
        db.Timeout = TimeSpan.FromSeconds(30);
        db.UtcDate = true;
        db.LimitSize = 1024 * 1024;
        db.UserVersion = 99;
        db.CheckpointSize = 0;

        // testing again
        db.Timeout.TotalSeconds.Should().Be(30);
        db.UtcDate.Should().Be(true);
        db.LimitSize.Should().Be(1024 * 1024);
        db.UserVersion.Should().Be(99);
        db.CheckpointSize.Should().Be(0);
    }
}