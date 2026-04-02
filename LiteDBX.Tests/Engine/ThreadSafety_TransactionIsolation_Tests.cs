using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_TransactionIsolation_Tests
{
    [Fact]
    public async Task Concurrent_Writers_On_Same_Collection_Do_Not_Overlap()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        db.Timeout = TimeSpan.FromSeconds(2);

        var col = db.GetCollection("items");
        var releaseWriter = new SemaphoreSlim(0, 1);
        var writerStarted = new SemaphoreSlim(0, 1);
        var secondWriterAttempted = new SemaphoreSlim(0, 1);

        var firstWriter = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await col.Insert(new BsonDocument { ["_id"] = 1, ["owner"] = "writer-a" }, tx);
            writerStarted.Release();
            await ConcurrencyTestHelper.WaitForSignal(releaseWriter, "the first writer to be released");
            await tx.Commit();
        });

        var secondWriter = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await ConcurrencyTestHelper.WaitForSignal(writerStarted, "the first writer to acquire the collection write lock");

            var sw = Stopwatch.StartNew();
            await using var tx = await db.BeginTransaction();
            secondWriterAttempted.Release();
            await col.Insert(new BsonDocument { ["_id"] = 2, ["owner"] = "writer-b" }, tx);
            await tx.Commit();
            sw.Stop();
            return sw.Elapsed;
        });

        await ConcurrencyTestHelper.WaitForSignal(secondWriterAttempted, "the second writer to begin waiting for the lock");
        await Task.Delay(250);
        secondWriter.IsCompleted.Should().BeFalse("same-collection writers must serialize");

        releaseWriter.Release();

        var waitDuration = await secondWriter;
        await firstWriter;

        waitDuration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(200));
        (await col.Count()).Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_Writers_On_Different_Collections_Can_Proceed()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        db.Timeout = TimeSpan.FromSeconds(2);

        var left = db.GetCollection("left");
        var right = db.GetCollection("right");
        var leftReady = new SemaphoreSlim(0, 1);
        var releaseLeft = new SemaphoreSlim(0, 1);

        var taskA = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await left.Insert(new BsonDocument { ["_id"] = 1 }, tx);
            leftReady.Release();
            await ConcurrencyTestHelper.WaitForSignal(releaseLeft, "writer A to finish");
            await tx.Commit();
        });

        await ConcurrencyTestHelper.WaitForSignal(leftReady, "writer A to stage work on the left collection");

        var taskB = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await right.Insert(new BsonDocument { ["_id"] = 2 }, tx);
            await tx.Commit();
        });

        var completed = await Task.WhenAny(taskB, Task.Delay(1000));
        completed.Should().BeSameAs(taskB, "writers on different collections should not block each other");

        releaseLeft.Release();
        await Task.WhenAll(taskA, taskB);

        (await left.Count()).Should().Be(1);
        (await right.Count()).Should().Be(1);
    }

    [Fact]
    public async Task Reader_Does_Not_See_Uncommitted_Update()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<Person>("people");
        await col.Insert(new Person { Id = 1, Name = "before", Age = 20 });

        var writerStaged = new SemaphoreSlim(0, 1);
        var readerReady = new SemaphoreSlim(0, 1);

        await using var readerTx = await db.BeginTransaction();
        var initial = await col.Query(readerTx).Where(x => x.Id == 1).First();

        var writer = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await col.Update(new Person { Id = 1, Name = "after", Age = 99 });
            writerStaged.Release();
            await ConcurrencyTestHelper.WaitForSignal(readerReady, "the reader to capture its snapshot");
            await tx.Commit();
        });

        await ConcurrencyTestHelper.WaitForSignal(writerStaged, "the concurrent update to be staged");
        var insideSnapshot = await col.Query(readerTx).Where(x => x.Id == 1).First();
        readerReady.Release();
        await writer;

        var afterCommitInsideSameTx = await col.Query(readerTx).Where(x => x.Id == 1).First();

        initial.Name.Should().Be("before");
        insideSnapshot.Name.Should().Be("before");
        afterCommitInsideSameTx.Name.Should().Be("before");

        var committed = await col.FindById(1);
        committed.Name.Should().Be("after");
        committed.Age.Should().Be(99);
    }

    [Fact]
    public async Task Reader_Does_Not_See_Uncommitted_Delete()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<Person>("people");
        await col.Insert(new Person { Id = 1, Name = "persisted" });

        var deleteStaged = new SemaphoreSlim(0, 1);
        var readerReady = new SemaphoreSlim(0, 1);

        await using var readerTx = await db.BeginTransaction();
        (await col.Query(readerTx).Where(x => x.Id == 1).Count()).Should().Be(1);

        var writer = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            (await col.Delete(1)).Should().BeTrue();
            deleteStaged.Release();
            await ConcurrencyTestHelper.WaitForSignal(readerReady, "the reader to capture its pre-delete snapshot");
            await tx.Commit();
        });

        await ConcurrencyTestHelper.WaitForSignal(deleteStaged, "the concurrent delete to be staged");
        (await col.Query(readerTx).Where(x => x.Id == 1).Count()).Should().Be(1);
        readerReady.Release();
        await writer;

        (await col.Query(readerTx).Where(x => x.Id == 1).Count()).Should().Be(1);
        (await col.FindById(1)).Should().BeNull();
    }

    [Fact]
    public async Task Reader_Does_Not_See_Uncommitted_Insert()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<Person>("people");

        await using var tx = await db.BeginTransaction();
        await col.Insert(new Person { Id = 7, Name = "staged" }, tx);

        (await col.Count()).Should().Be(0);
        (await col.Query(tx).Count()).Should().Be(1);

        await tx.Commit();

        (await col.Count()).Should().Be(1);
    }

    [Fact]
    public async Task Snapshot_Remains_Stable_Across_Concurrent_Commit()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var col = db.GetCollection<Person>("people");

        await col.Insert(new[]
        {
            new Person { Id = 1, Name = "alpha", Age = 20 },
            new Person { Id = 2, Name = "beta", Age = 30 }
        });

        var writerStaged = new SemaphoreSlim(0, 1);
        var readerCaptured = new SemaphoreSlim(0, 1);

        List<Person> beforeCommit;
        List<Person> afterCommitInsideSameTx;

        await using var readerTx = await db.BeginTransaction();
        beforeCommit = await col.Query(readerTx).OrderBy(x => x.Id).ToList();

        var writer = ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await col.Update(new Person { Id = 1, Name = "alpha-updated", Age = 99 });
            await col.Insert(new Person { Id = 3, Name = "gamma", Age = 40 }, tx);
            writerStaged.Release();
            await ConcurrencyTestHelper.WaitForSignal(readerCaptured, "the reader snapshot to be captured");
            await tx.Commit();
        });

        await ConcurrencyTestHelper.WaitForSignal(writerStaged, "the concurrent writer to stage new data");
        readerCaptured.Release();
        await writer;
        afterCommitInsideSameTx = await col.Query(readerTx).OrderBy(x => x.Id).ToList();

        beforeCommit.Select(x => x.Id).Should().Equal(1, 2);
        beforeCommit.Select(x => x.Name).Should().Equal("alpha", "beta");
        afterCommitInsideSameTx.Select(x => x.Id).Should().Equal(1, 2);
        afterCommitInsideSameTx.Select(x => x.Name).Should().Equal("alpha", "beta");

        var committed = await col.FindAll().ToListAsync();
        committed.Select(x => x.Id).OrderBy(x => x).Should().Equal(1, 2, 3);
        committed.Single(x => x.Id == 1).Name.Should().Be("alpha-updated");
    }

    [Fact]
    public async Task Dispose_Without_Commit_Releases_Collection_Lock_And_Transaction_Gate()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        db.Timeout = TimeSpan.FromSeconds(2);
        var col = db.GetCollection("items");

        var tx = await db.BeginTransaction();
        await col.Insert(new BsonDocument { ["_id"] = 1 }, tx);
        await tx.DisposeAsync();

        await using var successor = await db.BeginTransaction();
        await col.Insert(new BsonDocument { ["_id"] = 2 }, successor);
        await successor.Commit();

        (await col.Count()).Should().Be(1);
        Assert.Null(await col.FindById(1));
        Assert.NotNull(await col.FindById(2));
    }

    [Fact]
    public async Task Ambient_Explicit_Transaction_Does_Not_Bleed_Into_Isolated_Task()
    {
        await using var db = new LiteDatabase(new MemoryStream());
        var ambient = db.GetCollection("ambient");
        var isolated = db.GetCollection("isolated");

        await using var tx = await db.BeginTransaction();
        await ambient.Insert(new BsonDocument { ["_id"] = 1, ["scope"] = "ambient" }, tx);

        await ConcurrencyTestHelper.RunIsolated(async () =>
        {
            await isolated.Insert(new BsonDocument { ["_id"] = 2, ["scope"] = "isolated" });
        });

        (await ambient.Count()).Should().Be(0);
        (await isolated.Count()).Should().Be(1);

        await tx.Rollback();

        (await ambient.Count()).Should().Be(0);
        (await isolated.Count()).Should().Be(1);
    }
}



