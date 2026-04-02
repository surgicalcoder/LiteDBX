using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using LiteDbX.Engine;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2417_Tests
{
    [Fact]
    public async Task Rebuild_Detected_Infinite_Loop()
    {
        var original = "../../../Resources/Issue2417_MyData.db";

        using (var filename = new TempFile(original))
        {
            var settings = new EngineSettings { Filename = filename, AutoRebuild = true };

            try
            {
                await using (var db = await LiteEngine.Open(settings))
                {
                    var col = await db.Query("customers", Query.All()).ToListAsync();
                    Assert.Fail("not expected");
                }
            }
            catch (Exception ex)
            {
                Assert.True(ex is LiteException lex && lex.ErrorCode == 999);
            }

            await using (var db = await LiteEngine.Open(settings))
            {
                var col = (await db.Query("customers", Query.All()).ToListAsync()).Count;
                var errors = (await db.Query("_rebuild_errors", Query.All()).ToListAsync()).Count;

                col.Should().Be(4);
                errors.Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task Rebuild_Detected_Infinite_Loop_With_Password()
    {
        var original = "../../../Resources/Issue2417_TestCacheDb.db";

        using (var filename = new TempFile(original))
        {
            var settings = new EngineSettings
            {
                Filename = filename,
                Password = "bzj2NplCbVH/bB8fxtjEC7u0unYdKHJVSmdmPgArRBwmmGw0+Wd2tE+b2zRMFcHAzoG71YIn/2Nq1EMqa5JKcQ==",
                AutoRebuild = true
            };

            try
            {
                await using (var db = await LiteEngine.Open(settings))
                {
                    var col = await db.Query("hubData$AppOperations", Query.All()).ToListAsync();
                    Assert.Fail("not expected");
                }
            }
            catch (Exception ex)
            {
                Assert.True(ex is LiteException lex && lex.ErrorCode == 999);
            }

            await using (var db = await LiteEngine.Open(settings))
            {
                var col = (await db.Query("hubData$AppOperations", Query.All()).ToListAsync()).Count;
                var errors = (await db.Query("_rebuild_errors", Query.All()).ToListAsync()).Count;

                col.Should().Be(408);
                errors.Should().Be(0);
            }
        }
    }
}