using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

#if DEBUG
namespace LiteDbX.Tests.Engine
{
    public class Rebuild_Crash_Tests
    {
        [Fact]
        public async Task Rebuild_Crash_IO_Write_Error()
        {
            var N = 1_000;

            using (var file = new TempFile())
            {
                var settings = new EngineSettings
                {
                    AutoRebuild = true,
                    Filename = file.Filename,
                    Password = "46jLz5QWd5fI3m4LiL2r"
                };

                var data = Enumerable.Range(1, N).Select(i => new BsonDocument
                {
                    ["_id"] = i,
                    ["name"] = Faker.Fullname(),
                    ["age"] = Faker.Age(),
                    ["created"] = Faker.Birthday(),
                    ["lorem"] = Faker.Lorem(5, 25)
                }).ToArray();

                try
                {
                    using (var db = new LiteEngine(settings))
                    {
                        db.SimulateDiskWriteFail = page =>
                        {
                            var p = new BasePage(page);

                            if (p.PageID == 28)
                            {
                                p.ColID.Should().Be(1);
                                p.PageType.Should().Be(PageType.Data);

                                page.Write((uint)123123123, 8192 - 4);
                            }
                        };

                        await db.Pragma("USER_VERSION", 123);
                        await db.EnsureIndex("col1", "idx_age", "$.age", false);
                        await db.Insert("col1", data, BsonAutoId.Int32);
                        await db.Insert("col2", data, BsonAutoId.Int32);
                        await db.Checkpoint();

                        // will fail
                        var col1 = (await db.Query("col1", Query.All()).ToListAsync()).Count;

                        // never run here
                        Assert.Fail("should get error in query");
                    }
                }
                catch (Exception ex)
                {
                    Assert.True(ex is LiteException lex && lex.ErrorCode == 999);
                }

                using (var db = new LiteEngine(settings))
                {
                    var col1 = (await db.Query("col1", Query.All()).ToListAsync()).Count;
                    var col2 = (await db.Query("col2", Query.All()).ToListAsync()).Count;
                    var errors = (await db.Query("_rebuild_errors", Query.All()).ToListAsync()).Count;

                    col1.Should().Be(N - 1);
                    col2.Should().Be(N);
                    errors.Should().Be(1);
                }
            }
        }
    }
}

#endif