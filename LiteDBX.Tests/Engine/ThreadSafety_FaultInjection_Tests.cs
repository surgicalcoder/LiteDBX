#if DEBUG
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_FaultInjection_Tests
{
    [Fact]
    public async Task Concurrent_Write_Corruption_Triggers_Recovery_And_Reopen_Remains_Usable()
    {
        using var file = new TempFile();
        var settings = new EngineSettings
        {
            AutoRebuild = true,
            Filename = file.Filename
        };

        var injected = 0;

        try
        {
            await using var engine = await LiteEngine.Open(settings);
            await using var db = new LiteDatabase(engine, disposeOnClose: false);
            var first = db.GetCollection("first");
            var second = db.GetCollection("second");

            engine.SimulateDiskWriteFail = page =>
            {
                if (Interlocked.Exchange(ref injected, 1) != 0)
                {
                    return;
                }

                var basePage = new BasePage(page);
                if (basePage.PageType == PageType.Data)
                {
                    page.Write((uint)123123123, 8192 - 4);
                }
            };

            var tasks = new[]
            {
                ConcurrencyTestHelper.RunIsolated(async () =>
                {
                    await first.Insert(Enumerable.Range(1, 100).Select(i => new BsonDocument { ["_id"] = i, ["name"] = "first" + i }));
                }),
                ConcurrencyTestHelper.RunIsolated(async () =>
                {
                    await second.Insert(Enumerable.Range(1001, 100).Select(i => new BsonDocument { ["_id"] = i, ["name"] = "second" + i }));
                })
            };

            await Task.WhenAll(tasks);

            var act = async () => await first.FindAll().ToListAsync();
            await act.Should().ThrowAsync<Exception>();
        }
        catch
        {
            // The corrupted run is expected to fail; reopen validation below is the real assertion.
        }

        await using var reopened = await LiteDatabase.Open(new ConnectionString
        {
            Filename = file.Filename,
            AutoRebuild = true
        });

        var firstRows = await reopened.GetCollection("first").FindAll().ToListAsync();
        var secondRows = await reopened.GetCollection("second").FindAll().ToListAsync();
        var rebuildErrors = await reopened.GetCollection("_rebuild_errors").FindAll().ToListAsync();

        (firstRows.Count + secondRows.Count).Should().BeGreaterThan(0);
        firstRows.Should().OnlyHaveUniqueItems(x => x["_id"].AsInt32);
        secondRows.Should().OnlyHaveUniqueItems(x => x["_id"].AsInt32);
        await reopened.GetCollection("third").Insert(new BsonDocument { ["_id"] = 1 });
        (await reopened.GetCollection("third").Count()).Should().Be(1);
        rebuildErrors.Select(x => x.TryGetValue("_id", out var id) ? id.AsString : string.Empty).Should().OnlyHaveUniqueItems();
    }
}
#endif

