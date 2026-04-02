using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class ThreadSafety_SmokeStress_Tests
{
    [Fact]
    public async Task Mixed_Read_Write_Delete_Upsert_Smoke_Maintains_Consistent_Final_State()
    {
        using var file = new TempFile();
        var expected = new ConcurrentDictionary<int, int>();

        await using (var db = await LiteDatabase.Open(file.Filename))
        {
            db.CheckpointSize = 0;
            var col = db.GetCollection("items");

            var workers = Enumerable.Range(0, 6)
                .Select(workerId => ConcurrencyTestHelper.RunIsolated(async () =>
                {
                    var baseId = workerId * 1000;

                    for (var iteration = 1; iteration <= 20; iteration++)
                    {
                        var id = baseId + iteration;
                        await col.Upsert(id, new BsonDocument { ["_id"] = id, ["value"] = iteration, ["worker"] = workerId });
                        expected[id] = iteration;

                        var loaded = await col.FindById(id);
                        Assert.NotNull(loaded);
                        loaded["value"].AsInt32.Should().Be(iteration);

                        if (iteration % 4 == 0)
                        {
                            var deleted = await col.Delete(id);
                            if (deleted)
                            {
                                expected.TryRemove(id, out _);
                            }
                        }
                    }
                }))
                .ToArray();

            await Task.WhenAll(workers);
            await db.Checkpoint();
        }

        await using (var reopened = await LiteDatabase.Open(file.Filename))
        {
            var actual = (await reopened.GetCollection("items").FindAll().ToListAsync())
                .ToDictionary(x => x["_id"].AsInt32, x => x["value"].AsInt32);

            actual.Should().Equal(expected.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value));
        }
    }
}

