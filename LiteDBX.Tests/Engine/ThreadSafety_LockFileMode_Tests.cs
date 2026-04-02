using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_LockFileMode_Tests
{
    [Fact]
    public async Task Separate_Instances_Can_Alternate_Writes_Without_Corruption()
    {
        using var file = new TempFile();

        await using var first = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
        await using var second = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));

        first.Timeout = TimeSpan.FromSeconds(5);
        second.Timeout = TimeSpan.FromSeconds(5);

        var firstCol = first.GetCollection("items");
        var secondCol = second.GetCollection("items");

        for (var i = 1; i <= 10; i++)
        {
            await firstCol.Insert(new BsonDocument { ["_id"] = i, ["owner"] = "first" });
            await secondCol.Update(new BsonDocument { ["_id"] = i, ["owner"] = "second" });
        }

        var firstView = await firstCol.FindAll().ToListAsync();
        var secondView = await secondCol.FindAll().ToListAsync();

        firstView.Should().HaveCount(10);
        secondView.Should().HaveCount(10);
        firstView.Select(x => x["owner"].AsString).Should().OnlyContain(x => x == "second");
        secondView.Select(x => x["owner"].AsString).Should().OnlyContain(x => x == "second");
    }

    [Fact]
    public async Task Concurrent_Instances_Preserve_All_Writes()
    {
        using var file = new TempFile();

        var tasks = Enumerable.Range(0, 4)
            .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
            {
                await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
                db.Timeout = TimeSpan.FromSeconds(5);
                var col = db.GetCollection("items");

                for (var i = 1; i <= 5; i++)
                {
                    await col.Insert(new BsonDocument
                    {
                        ["_id"] = worker * 100 + i,
                        ["worker"] = worker
                    });
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        await using var verify = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
        var rows = await verify.GetCollection("items").FindAll().ToListAsync();
        rows.Should().HaveCount(20);
        rows.Select(x => x["_id"].AsInt32).Distinct().Should().HaveCount(20);
    }

    [Fact]
    public async Task Read_On_New_File_And_Concurrent_First_Write_Do_Not_Corrupt_Initialization()
    {
        using var file = new TempFile();

        var readerTask = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
            db.Timeout = TimeSpan.FromSeconds(5);
            return await db.GetCollection("items").Count();
        });

        var writerTask = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
            db.Timeout = TimeSpan.FromSeconds(5);
            await db.GetCollection("items").Insert(new BsonDocument { ["_id"] = 1 });
        });

        await Task.WhenAll(readerTask, writerTask);

        await using var verify = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.LockFile));
        (await verify.GetCollection("items").Count()).Should().Be(1);
    }
}

