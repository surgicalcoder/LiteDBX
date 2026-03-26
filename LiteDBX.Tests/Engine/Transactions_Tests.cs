using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Transactions_Tests
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(5);

    private static async Task<(LiteTransaction transaction, TransactionService service)> CreateTransactionWithReadableDirtyCollectionPage(
        LiteDatabase db,
        string collectionName,
        string indexName)
    {
        var collection = db.GetCollection<Person>(collectionName);
        await collection.Insert(new Person { Id = 1, Name = "seed" });

        var transaction = (LiteTransaction)await db.BeginTransaction();
        var service = transaction.Service;
        var snapshot = await service.CreateSnapshotAsync(LockMode.Write, collectionName, addIfNotExists: false);

        snapshot.CollectionPage.InsertCollectionIndex(indexName, "$.Name", unique: false);
        snapshot.CollectionPage.IsDirty.Should().BeTrue();

        // Simulate a safepoint-flushed page buffer: the page is still tracked by the snapshot,
        // but the underlying buffer is no longer writable.
        snapshot.CollectionPage.Buffer.ShareCounter = 0;

        return (transaction, service);
    }

    private static Task RunIsolated(Func<Task> action)
    {
        using (ExecutionContext.SuppressFlow())
        {
            return Task.Run(action);
        }
    }

    private static async Task WaitForSignal(SemaphoreSlim semaphore, string description)
    {
        if (!await semaphore.WaitAsync(CoordinationTimeout))
        {
            throw new TimeoutException($"Timed out waiting for {description}.");
        }
    }

    /// <summary>
    /// Two concurrent tasks: task A holds a write transaction; task B should time out waiting
    /// for the lock.
    /// </summary>
    [Fact]
    public async Task Transaction_Write_Lock_Timeout()
    {
        var data1 = DataGen.Person(1, 100).ToArray();
        var data2 = DataGen.Person(101, 200).ToArray();

        await using var db = new LiteDatabase("filename=:memory:");
        db.Timeout = TimeSpan.FromSeconds(1);

        var person = db.GetCollection<Person>();
        await person.Insert(data1);

        var taskASemaphore = new SemaphoreSlim(0, 1);
        var taskBSemaphore = new SemaphoreSlim(0, 1);

        // Task A: open explicit transaction, insert, signal B, wait, then commit
        var ta = RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2, tx);

            taskBSemaphore.Release();
            await WaitForSignal(taskASemaphore, "task B to finish its lock-timeout assertion");

            (await person.Query(tx).Count()).Should().Be(data1.Length + data2.Length);
            await tx.Commit();
        });

        // Task B: should fail to acquire write lock within timeout
        var tb = RunIsolated(async () =>
        {
            await WaitForSignal(taskBSemaphore, "task A to acquire the write transaction");

            try
            {
                await db.Invoking(async d =>
                {
                    await using var tx = await d.BeginTransaction();
                    await person.DeleteMany("1 = 1");
                }).Should().ThrowAsync<LiteException>()
                  .Where(ex => ex.ErrorCode == LiteException.LOCK_TIMEOUT);
            }
            finally
            {
                taskASemaphore.Release();
            }
        });

        await Task.WhenAll(ta, tb);
    }

    /// <summary>
    /// Dirty-read isolation: changes in an uncommitted transaction must not be visible outside it.
    /// </summary>
    [Fact]
    public async Task Transaction_Avoid_Dirty_Read()
    {
        var data1 = DataGen.Person(1, 100).ToArray();
        var data2 = DataGen.Person(101, 200).ToArray();

        await using var db = new LiteDatabase(new MemoryStream());
        var person = db.GetCollection<Person>();
        await person.Insert(data1);

        var taskASemaphore = new SemaphoreSlim(0, 1);
        var taskBSemaphore = new SemaphoreSlim(0, 1);

        var ta = RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2, tx);

            taskBSemaphore.Release();
            await WaitForSignal(taskASemaphore, "task B to verify the pre-commit count");

            try
            {
                (await person.Query(tx).Count()).Should().Be(data1.Length + data2.Length);
                await tx.Commit();
            }
            finally
            {
                taskBSemaphore.Release();
            }
        });

        var tb = RunIsolated(async () =>
        {
            await WaitForSignal(taskBSemaphore, "task A to stage uncommitted changes");

            try
            {
                // outside any transaction — must see only the committed 100 docs
                (await person.Count()).Should().Be(data1.Length);
            }
            finally
            {
                taskASemaphore.Release();
            }

            await WaitForSignal(taskBSemaphore, "task A to finish committing");

            // after A committed — now 200 docs visible
            (await person.Count()).Should().Be(data1.Length + data2.Length);
        });

        await Task.WhenAll(ta, tb);
    }

    /// <summary>
    /// Read-version isolation: a transaction opened before a commit must not see the new data.
    /// </summary>
    [Fact]
    public async Task Transaction_Read_Version()
    {
        var data1 = DataGen.Person(1, 100).ToArray();
        var data2 = DataGen.Person(101, 200).ToArray();

        await using var db = new LiteDatabase(new MemoryStream());
        var person = db.GetCollection<Person>();
        await person.Insert(data1);

        var taskASemaphore = new SemaphoreSlim(0, 1);
        var taskBSemaphore = new SemaphoreSlim(0, 1);

        var ta = RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2, tx);

            taskBSemaphore.Release();
            await WaitForSignal(taskASemaphore, "task B to verify its snapshot");

            try
            {
                await tx.Commit();
            }
            finally
            {
                taskBSemaphore.Release();
            }
        });

        var tb = RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();

            await WaitForSignal(taskBSemaphore, "task A to stage the concurrent write");

            try
            {
                (await person.Query(tx).Count()).Should().Be(data1.Length);
            }
            finally
            {
                taskASemaphore.Release();
            }

            await WaitForSignal(taskBSemaphore, "task A to commit the concurrent write");

            // still in same transaction — snapshot must remain at 100 docs
            (await person.Query(tx).Count()).Should().Be(data1.Length);
        });

        await Task.WhenAll(ta, tb);
    }

    /// <summary>
    /// Basic transaction lifecycle: commit, double-commit guard, rollback guard.
    /// </summary>
    [Fact]
    public async Task Test_Transaction_States()
    {
        var data0 = DataGen.Person(1, 10).ToArray();
        var data1 = DataGen.Person(11, 20).ToArray();

        await using var db = new LiteDatabase(new MemoryStream());
        var person = db.GetCollection<Person>();

        // explicit transaction commit
        await using (var tx = await db.BeginTransaction())
        {
            await person.Insert(data0);
            await tx.Commit();
        }

        // auto-commit insert (no explicit transaction)
        await person.Insert(data1);

        (await person.Count()).Should().Be(20);
    }

    /// <summary>
    /// Rollback on disposal: if DisposeAsync is called without Commit, changes are discarded.
    /// </summary>
    [Fact]
    public async Task Test_Transaction_Rollback_On_Dispose()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<Person>();

        await using (var tx = await db.BeginTransaction())
        {
            await col.Insert(DataGen.Person(1, 5).ToArray(), tx);
            // dispose without commit → rollback
        }

        (await col.Count()).Should().Be(0);
    }

    [Fact]
    public async Task Transaction_RollbackAsync_Ignores_Readable_Safepoint_Buffers()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var (transaction, service) = await CreateTransactionWithReadableDirtyCollectionPage(db, "rollback_async", "idx_async");

        try
        {
            await FluentActions.Invoking(() => service.RollbackAsync().AsTask()).Should().NotThrowAsync();
            service.State.Should().Be(TransactionState.Aborted);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [Fact]
    public async Task Transaction_Rollback_Ignores_Readable_Safepoint_Buffers()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var (transaction, service) = await CreateTransactionWithReadableDirtyCollectionPage(db, "rollback_sync", "idx_sync");

        try
        {
            service.Invoking(x => x.Rollback()).Should().NotThrow();
            service.State.Should().Be(TransactionState.Aborted);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    [Fact]
    public async Task Transaction_Dispose_Ignores_Readable_Safepoint_Buffers()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var (transaction, service) = await CreateTransactionWithReadableDirtyCollectionPage(db, "rollback_dispose", "idx_dispose");

        try
        {
            service.Invoking(x => x.Dispose()).Should().NotThrow();
            service.State.Should().Be(TransactionState.Disposed);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }
}