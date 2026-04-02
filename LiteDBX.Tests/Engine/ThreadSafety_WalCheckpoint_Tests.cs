using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_WalCheckpoint_Tests
{
    [Fact]
    public async Task Concurrent_Explicit_Transaction_Commits_Preserve_All_Documents()
    {
        using var file = new TempFile();
        var expectedIds = Enumerable.Range(1, 80).ToArray();

        await using (var db = new LiteDatabase(file.Filename))
        {
            db.CheckpointSize = 0;
            var col = db.GetCollection("items");

            var tasks = Enumerable.Range(0, 8)
                .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
                {
                    await using var tx = await db.BeginTransaction();

                    var documents = Enumerable.Range(1, 10)
                        .Select(offset => new BsonDocument
                        {
                            ["_id"] = worker * 10 + offset,
                            ["worker"] = worker
                        })
                        .ToArray();

                    await col.Insert(documents, tx);
                    await tx.Commit();
                }))
                .ToArray();

            await Task.WhenAll(tasks);
            (await col.Count()).Should().Be(expectedIds.Length);
        }

        await using (var reopened = new LiteDatabase(file.Filename))
        {
            var ids = (await reopened.GetCollection("items").FindAll().ToListAsync())
                .Select(x => x["_id"].AsInt32)
                .OrderBy(x => x)
                .ToArray();

            ids.Should().Equal(expectedIds);
        }
    }

    [Fact]
    public async Task Concurrent_Commits_And_Checkpoints_Do_Not_Lose_Data()
    {
        using var file = new TempFile();
        var expectedIds = new ConcurrentBag<int>();

        await using (var db = new LiteDatabase(file.Filename))
        {
            db.CheckpointSize = 0;
            var col = db.GetCollection("items");
            using var cts = new CancellationTokenSource();

            var checkpointTask = ConcurrencyTestHelper.RunIsolated(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await db.Checkpoint();
                    await Task.Delay(10);
                }
            });

            var writers = Enumerable.Range(0, 4)
                .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
                {
                    for (var batch = 0; batch < 5; batch++)
                    {
                        await using var tx = await db.BeginTransaction();

                        var docs = Enumerable.Range(1, 5)
                            .Select(offset => new BsonDocument
                            {
                                ["_id"] = worker * 100 + batch * 10 + offset,
                                ["worker"] = worker,
                                ["batch"] = batch
                            })
                            .ToArray();

                        await col.Insert(docs, tx);
                        await tx.Commit();

                        foreach (var doc in docs)
                        {
                            expectedIds.Add(doc["_id"].AsInt32);
                        }
                    }
                }))
                .ToArray();

            await Task.WhenAll(writers);
            cts.Cancel();
            await checkpointTask;
            await db.Checkpoint();
        }

        var expected = expectedIds.OrderBy(x => x).ToArray();

        await using (var reopened = new LiteDatabase(file.Filename))
        {
            var actual = (await reopened.GetCollection("items").FindAll().ToListAsync())
                .Select(x => x["_id"].AsInt32)
                .OrderBy(x => x)
                .ToArray();

            actual.Should().Equal(expected);
        }
    }

    [Fact]
    public async Task Checkpoint_Waits_For_Open_Transactions_Then_Completes()
    {
        using var file = new TempFile();

        await using var db = new LiteDatabase(file.Filename);
        db.CheckpointSize = 0;
        var col = db.GetCollection("items");
        var writerHolding = new SemaphoreSlim(0, 1);
        var releaseWriter = new SemaphoreSlim(0, 1);

        var writer = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await col.Insert(new BsonDocument { ["_id"] = 1 }, tx);
            writerHolding.Release();
            await ConcurrencyTestHelper.WaitForSignal(releaseWriter, "the writer transaction to be released");
            await tx.Commit();
        });

        await ConcurrencyTestHelper.WaitForSignal(writerHolding, "the writer transaction to hold the transaction gate");

        var checkpointTask = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await db.Checkpoint();
        });

        await Task.Delay(250);
        checkpointTask.IsCompleted.Should().BeFalse("checkpoint should wait for active transactions");

        releaseWriter.Release();

        await Task.WhenAll(writer, checkpointTask);
        (await col.Count()).Should().Be(1);
    }

    [Fact]
    public async Task Repeated_Checkpoint_Is_Idempotent_After_Quiescence()
    {
        using var file = new TempFile();

        await using (var db = new LiteDatabase(file.Filename))
        {
            db.CheckpointSize = 0;
            var col = db.GetCollection("items");
            await col.Insert(Enumerable.Range(1, 25).Select(i => new BsonDocument { ["_id"] = i, ["v"] = i }));

            for (var i = 0; i < 5; i++)
            {
                await db.Checkpoint();
            }

            (await col.Count()).Should().Be(25);
        }

        await using (var reopened = new LiteDatabase(file.Filename))
        {
            var rows = await reopened.GetCollection("items").FindAll().ToListAsync();
            rows.Should().HaveCount(25);
            rows.Select(x => x["_id"].AsInt32).OrderBy(x => x).Should().Equal(Enumerable.Range(1, 25));
        }
    }

    [Fact]
    public async Task Concurrent_Commits_Produce_Monotonic_Visible_Counts()
    {
        using var file = new TempFile();
        var observedCounts = new ConcurrentQueue<int>();

        await using var db = new LiteDatabase(file.Filename);
        db.CheckpointSize = 0;
        var col = db.GetCollection("items");
        using var cts = new CancellationTokenSource();

        var reader = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                observedCounts.Enqueue(await col.Count());
                await Task.Delay(10);
            }
        });

        var writers = Enumerable.Range(0, 3)
            .Select(worker => ConcurrencyTestHelper.RunIsolated(async () =>
            {
                for (var batch = 0; batch < 4; batch++)
                {
                    await using var tx = await db.BeginTransaction();
                    var docs = Enumerable.Range(1, 5)
                        .Select(offset => new BsonDocument { ["_id"] = worker * 100 + batch * 10 + offset })
                        .ToArray();
                    await col.Insert(docs, tx);
                    await tx.Commit();
                }
            }))
            .ToArray();

        await Task.WhenAll(writers);
        observedCounts.Enqueue(await col.Count());
        cts.Cancel();
        await reader;

        observedCounts.Should().NotBeEmpty();
        observedCounts.Should().BeInAscendingOrder();
        observedCounts.Max().Should().Be(60);
    }
}

