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
        var ta = Task.Run(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2);

            taskBSemaphore.Release();
            await taskASemaphore.WaitAsync();

            (await person.Count()).Should().Be(data1.Length + data2.Length);
            await tx.Commit();
        });

        // Task B: should fail to acquire write lock within timeout
        var tb = Task.Run(async () =>
        {
            await taskBSemaphore.WaitAsync();

            await db.Invoking(async d =>
            {
                await using var tx = await d.BeginTransaction();
                await person.DeleteMany("1 = 1");
            }).Should().ThrowAsync<LiteException>()
              .Where(ex => ex.ErrorCode == LiteException.LOCK_TIMEOUT);

            taskASemaphore.Release();
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

        var ta = Task.Run(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2);

            taskBSemaphore.Release();
            await taskASemaphore.WaitAsync();

            (await person.Count()).Should().Be(data1.Length + data2.Length);
            await tx.Commit();
            taskBSemaphore.Release();
        });

        var tb = Task.Run(async () =>
        {
            await taskBSemaphore.WaitAsync();

            // outside any transaction — must see only the committed 100 docs
            (await person.Count()).Should().Be(data1.Length);

            taskASemaphore.Release();
            await taskBSemaphore.WaitAsync();

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

        var ta = Task.Run(async () =>
        {
            await using var tx = await db.BeginTransaction();
            await person.Insert(data2);

            taskBSemaphore.Release();
            await taskASemaphore.WaitAsync();

            await tx.Commit();
            taskBSemaphore.Release();
        });

        var tb = Task.Run(async () =>
        {
            await using var tx = await db.BeginTransaction();

            await taskBSemaphore.WaitAsync();
            (await person.Count()).Should().Be(data1.Length);

            taskASemaphore.Release();
            await taskBSemaphore.WaitAsync();

            // still in same transaction — snapshot must remain at 100 docs
            (await person.Count()).Should().Be(data1.Length);
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
            await col.Insert(DataGen.Person(1, 5).ToArray());
            // dispose without commit → rollback
        }

        (await col.Count()).Should().Be(0);
    }
}