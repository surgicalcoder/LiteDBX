using System;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue2494_Tests
{
    [Fact]
    public static async Task Test()
    {
        var original = "../../../Resources/Issue_2494_EncryptedV4.db";
        using var filename = new TempFile(original);

        var connectionString = new ConnectionString(filename)
        {
            Password = "pass123",
            Upgrade = true
        };

        await using (var db = await LiteDatabase.Open(connectionString))
        {
            var col = db.GetCollection<PlayerDto>();

            await foreach (var _ in col.FindAll())
            {
            }
        }
    }

    public class PlayerDto
    {
        public PlayerDto(Guid id, string name)
        {
            Id = id;
            Name = name;
        }

        public PlayerDto() { }

        [BsonId]
        public Guid Id { get; set; }

        public string Name { get; set; }
    }
}