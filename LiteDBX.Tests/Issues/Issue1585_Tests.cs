using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class PlayerDto
{
    public PlayerDto(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    [BsonId]
    public Guid Id { get; }

    public string Name { get; }
}

public class Issue1585a_Tests
{
    [Fact]
    public async Task Dto_Read()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }

    [Fact]
    public async Task Dto_Read1()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }

    [Fact]
    public async Task Dto_Read2()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }
}

public class Issue1585b_Tests
{
    [Fact]
    public async Task Dto_Read3()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }

    [Fact]
    public async Task Dto_Read4()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }

    [Fact]
    public async Task Dto_Read5()
    {
        await using (var db = await LiteDatabase.Open(new MemoryStream()))
        {
            var id = Guid.NewGuid();
            var col = db.GetCollection<PlayerDto>();
            await col.Insert(new PlayerDto(id, "Bob"));
            var player = await col.FindOne(x => x.Id == id);
            Assert.NotNull(player);
        }
    }
}