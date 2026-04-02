using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Upgrade_Tests
{
    [Fact]
    public async Task Migrage_From_V4()
    {
        // v5 upgrades only from v4!
        using (var tempFile = new TempFile("../../../Resources/v4.db"))
        {
            await using (var db = await LiteDatabase.Open($"filename={tempFile};upgrade=true"))
            {
                // convert and open database
                var col1 = db.GetCollection("col1");

                (await col1.Count()).Should().Be(3);
            }

            await using (var db = await LiteDatabase.Open($"filename={tempFile};upgrade=true"))
            {
                // database already converted
                var col1 = db.GetCollection("col1");

                (await col1.Count()).Should().Be(3);
            }
        }
    }

    [Fact]
    public async Task Migrage_From_V4_No_FileExtension()
    {
        // v5 upgrades only from v4!
        using (var tempFile = new TempFile("../../../Resources/v4.db"))
        {
            await using (var db = await LiteDatabase.Open($"filename={tempFile};upgrade=true"))
            {
                // convert and open database
                var col1 = db.GetCollection("col1");

                (await col1.Count()).Should().Be(3);
            }

            await using (var db = await LiteDatabase.Open($"filename={tempFile};upgrade=true"))
            {
                // database already converted
                var col1 = db.GetCollection("col1");

                (await col1.Count()).Should().Be(3);
            }
        }
    }
}