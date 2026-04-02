using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_SharedMode_Tests
{
    [Fact]
    public async Task Shared_Mode_Exception_During_Enumeration_Releases_Lease()
    {
        using var file = new TempFile();
        await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.Shared));
        var col = db.GetCollection("items");
        await col.Insert(new[]
        {
            new BsonDocument { ["_id"] = 1 },
            new BsonDocument { ["_id"] = 2 },
            new BsonDocument { ["_id"] = 3 }
        });

        var act = async () =>
        {
            await foreach (var _ in col.FindAll())
            {
                throw new InvalidOperationException("boom");
            }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();

        await col.Insert(new BsonDocument { ["_id"] = 4 });
        (await col.Count()).Should().Be(4);
    }

    [Fact]
    public async Task Shared_Mode_Cancellation_During_Enumeration_Releases_Lease()
    {
        using var file = new TempFile();
        await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.Shared));
        var col = db.GetCollection("items");
        await col.Insert(Enumerable.Range(1, 10).Select(i => new BsonDocument { ["_id"] = i }));

        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in col.FindAll(cts.Token))
            {
                cts.Cancel();
            }
        });

        await col.Insert(new BsonDocument { ["_id"] = 11 });
        (await col.Count()).Should().Be(11);
    }

    [Fact]
    public async Task Independent_Shared_Mode_Callers_Can_Serialize_Without_Corruption()
    {
        using var file = new TempFile();
        await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.Shared));
        var col = db.GetCollection("items");

        var tasks = Enumerable.Range(1, 20)
            .Select(id => ConcurrencyTestHelper.RunIsolated(async () =>
            {
                await col.Insert(new BsonDocument { ["_id"] = id, ["value"] = id });
                var doc = await col.FindById(id);
                Assert.NotNull(doc);
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        var rows = await col.FindAll().ToListAsync();
        rows.Should().HaveCount(20);
        rows.Select(x => x["_id"].AsInt32).OrderBy(x => x).Should().Equal(Enumerable.Range(1, 20));
    }

    [Fact]
    public async Task Shared_Mode_Break_Early_Allows_Immediate_Write_From_Another_Flow()
    {
        using var file = new TempFile();
        await using var db = await LiteDatabase.Open(ConcurrencyTestHelper.CreateConnectionString(file, ConnectionType.Shared));
        var col = db.GetCollection("items");
        await col.Insert(new[]
        {
            new BsonDocument { ["_id"] = 1 },
            new BsonDocument { ["_id"] = 2 },
            new BsonDocument { ["_id"] = 3 }
        });

        await foreach (var _ in col.FindAll())
        {
            break;
        }

        await ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await col.Insert(new BsonDocument { ["_id"] = 4 });
        });

        (await col.Count()).Should().Be(4);
    }
}

