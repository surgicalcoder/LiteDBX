using System;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Engine;

public class Recursion_Tests
{
    [Fact]
    public async Task UpdateInFindAll()
    {
        await Test(async collection =>
        {
            await foreach (var document in collection.FindAll())
            {
                await collection.Update(document);
            }
        });
    }

    [Fact]
    public async Task InsertDeleteInFindAll()
    {
        await Test(async collection =>
        {
            await foreach (var document in collection.FindAll())
            {
                var id = await collection.Insert(new BsonDocument());
                await collection.Delete(id);
            }
        });
    }

    [Fact]
    public async Task QueryInFindAll()
    {
        await Test(async collection =>
        {
            await foreach (var document in collection.FindAll())
            {
                await collection.Query().Count();
            }
        });
    }

    private static async Task Test(Func<ILiteCollection<BsonDocument>, Task> action)
    {
        await using var database = new LiteDatabase(new ConnectionString
        {
            Filename = "Demo.db",
            Connection = ConnectionType.Shared
        });

        var accounts = database.GetCollection("Recursion");

        if (await accounts.Count() < 3)
        {
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
        }

        await action(accounts);
    }
}