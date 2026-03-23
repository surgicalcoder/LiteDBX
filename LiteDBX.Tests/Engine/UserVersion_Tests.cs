using FluentAssertions;
using System.Threading.Tasks;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class UserVersion_Tests
{
    [Fact]
    public async Task UserVersion_Get_Set()
    {
        using var file = new TempFile();

        await using (var db = new LiteDatabase(file.Filename))
        {
            db.UserVersion.Should().Be(0);
            db.UserVersion = 5;
            await db.Checkpoint();
        }

        await using (var db = new LiteDatabase(file.Filename))
        {
            db.UserVersion.Should().Be(5);
        }
    }
}