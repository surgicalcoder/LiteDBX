using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

#if DEBUG
namespace LiteDbX.Tests.Engine;

public class ThreadSafety_TransactionLifecycle_Tests
{
    [Fact]
    public async Task No_Leaked_Transactions_After_Parallel_Commits()
    {
        await using var engine = new LiteEngine(new EngineSettings { DataStream = new MemoryStream() });
        await using var db = new LiteDatabase(engine, disposeOnClose: false);
        var monitor = engine.GetMonitor();
        var col = db.GetCollection("items");

        const int workers = 8;
        var tasks = Enumerable.Range(0, workers)
            .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
            {
                await using var tx = await db.BeginTransaction();
                await col.Insert(new BsonDocument { ["_id"] = worker, ["worker"] = worker }, tx);
                await tx.Commit();
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.Count == 0),
            "transaction monitor to drain after commits");
        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.SelectMany(x => x.Snapshots).Any() == false),
            "snapshot monitor to drain after commits");

        (await col.Count()).Should().Be(workers);
    }

    [Fact]
    public async Task No_Leaked_Transactions_After_Parallel_Rollback_Dispose_Mix()
    {
        await using var engine = new LiteEngine(new EngineSettings { DataStream = new MemoryStream() });
        await using var db = new LiteDatabase(engine, disposeOnClose: false);
        var monitor = engine.GetMonitor();
        var col = db.GetCollection("items");

        const int workers = 12;
        var committedIds = new List<int>();
        var committedLock = new object();

        var tasks = Enumerable.Range(0, workers)
            .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
            {
                await using var tx = await db.BeginTransaction();
                await col.Insert(new BsonDocument { ["_id"] = worker, ["worker"] = worker }, tx);

                switch (worker % 3)
                {
                    case 0:
                        await tx.Commit();
                        lock (committedLock)
                        {
                            committedIds.Add(worker);
                        }
                        break;
                    case 1:
                        await tx.Rollback();
                        break;
                }
            }))
            .ToArray();

        await Task.WhenAll(tasks);

        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.Count == 0),
            "transaction monitor to drain after rollback/dispose mix");
        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.SelectMany(x => x.Snapshots).Any() == false),
            "snapshot monitor to drain after rollback/dispose mix");

        var all = await col.FindAll().ToListAsync();
        all.Select(x => x["_id"].AsInt32).OrderBy(x => x).Should().Equal(committedIds.OrderBy(x => x));
    }

    [Fact]
    public async Task System_Collections_Report_Active_Transactions_And_Snapshots_Then_Clear()
    {
        await using var engine = new LiteEngine(new EngineSettings { DataStream = new MemoryStream() });
        await using var db = new LiteDatabase(engine, disposeOnClose: false);
        var monitor = engine.GetMonitor();
        var col = db.GetCollection<Person>("people");
        await col.Insert(new Person { Id = 1, Name = "seed" });

        var tx = await db.BeginTransaction();
        try
        {
            await col.Update(new Person { Id = 1, Name = "changed" });
            _ = await col.Query(tx).Count();

            var activeTransactions = await db.GetCollection("$transactions").FindAll().ToListAsync();
            var activeSnapshots = await db.GetCollection("$snapshots").FindAll().ToListAsync();

            activeTransactions.Should().NotBeEmpty();
            activeSnapshots.Should().Contain(snapshot => snapshot["collection"].AsString == "people");

            await tx.Rollback();
        }
        finally
        {
            await tx.DisposeAsync();
        }

        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.Count == 0),
            "transaction monitor to be empty after rollback");
        await ConcurrencyTestHelper.Eventually(
            () => Task.FromResult(monitor.Transactions.SelectMany(x => x.Snapshots).Any() == false),
            "snapshot monitor to be empty after rollback");
    }
}
#endif

