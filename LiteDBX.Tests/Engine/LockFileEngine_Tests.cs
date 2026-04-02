using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class LockFileEngine_Tests
{
    [Fact]
    public void Constructor_Requires_Physical_Filename()
    {
        Action act = () => _ = new LockFileEngine(new LiteDbX.Engine.EngineSettings { Filename = ":memory:" });

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*physical file-backed*");
    }

    [Fact]
    public async Task Write_Waits_For_Lock_File_And_Honours_Cancellation()
    {
        using var file = new TempFile();
        var lockFilename = FileHelper.GetLockFile(file.Filename);

        await using var database = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.LockFile
        });

        var collection = database.GetCollection("lockfile");

        using (new FileStream(lockFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    await collection.Insert(new BsonDocument(), cts.Token);
                });
            }
        }

        await collection.Insert(new BsonDocument());
        (await collection.Count()).Should().Be(1);
    }

    [Fact]
    public async Task Separate_Database_Instances_Can_Write_Sequentially_To_Same_File()
    {
        using var file = new TempFile();

        await using var first = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.LockFile
        });

        await using var second = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.LockFile
        });

        var firstCollection = first.GetCollection("items");
        var secondCollection = second.GetCollection("items");

        await firstCollection.Insert(new BsonDocument { ["name"] = "first" });
        await secondCollection.Insert(new BsonDocument { ["name"] = "second" });

        (await firstCollection.Count()).Should().Be(2);
        (await secondCollection.Count()).Should().Be(2);
    }

    [Fact]
    public async Task Read_First_On_New_File_Creates_Database_Safely()
    {
        using var file = new TempFile();

        await using var database = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            Connection = ConnectionType.LockFile
        });

        var collection = database.GetCollection("items");

        (await collection.Count()).Should().Be(0);
        File.Exists(file.Filename).Should().BeTrue();
    }
}

