using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Rebuild_Tests
{
    [Fact]
    public async Task Rebuild_After_DropCollection()
    {
        using var file = new TempFile();
        await using var db = await LiteDatabase.Open(file.Filename);
        var col = db.GetCollection<Zip>("zip");

        await col.Insert(DataGen.Zip());
        await db.DropCollection("zip");
        await db.Checkpoint();

        var size = file.Size;
        var r = await db.Rebuild();

        // only header page
        Assert.Equal(8192, size - r);
    }

    [Fact]
    public async Task Rebuild_Large_Files()
    {
        async Task DoTest(ILiteDatabase db, ILiteCollection<Zip> col)
        {
            Assert.Equal(1, await col.Count());
            Assert.Equal(99, db.UserVersion);
        }

        using var file = new TempFile();

        await using (var db = await LiteDatabase.Open(file.Filename))
        {
            var col = db.GetCollection<Zip>();
            db.UserVersion = 99;
            await col.EnsureIndex("city");

            var inserted = await col.Insert(DataGen.Zip()); // 29.353 docs
            var deleted  = await col.DeleteMany(x => x.Id != "01001"); // delete 29.352

            Assert.Equal(29353, inserted);
            Assert.Equal(29352, deleted);
            Assert.Equal(1, await col.Count());

            await db.Checkpoint();
            Assert.True(file.Size > 5 * 1024 * 1024);

            var reduced = await db.Rebuild();
            Assert.True(file.Size < 50 * 1024);

            await DoTest(db, col);
        }

        // re-open and rebuild again
        await using (var db = await LiteDatabase.Open(file.Filename))
        {
            var col = db.GetCollection<Zip>();
            await DoTest(db, col);
            await db.Rebuild();
            await DoTest(db, col);
        }
    }

    [Fact(Skip = "Not supported yet")]
    public void Rebuild_Change_Culture_Error()
    {
        // deferred — collation rebuild not yet implemented
    }
}