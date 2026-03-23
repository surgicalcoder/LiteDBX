using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2534_Tests
{
    [Fact]
    public async Task Test()
    {
        await using LiteDatabase database = new(new ConnectionString
        {
            Filename = "Demo.db",
            Connection = ConnectionType.Shared
        });

        var accounts = database.GetCollection("Issue2534");

        if (await accounts.Count() < 3)
        {
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
            await accounts.Insert(new BsonDocument());
        }

        await foreach (var document in accounts.FindAll())
        {
            await accounts.Update(document);
        }
    }
}