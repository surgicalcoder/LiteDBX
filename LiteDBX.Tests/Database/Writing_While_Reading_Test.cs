using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Writing_While_Reading_Test
{
    [Fact]
    public async Task Test()
    {
        using var f = new TempFile();

        await using (var db = await LiteDatabase.Open(f.Filename))
        {
            var col = db.GetCollection<MyClass>("col");
            await col.Insert(new MyClass { Name = "John", Description = "Doe" });
            await col.Insert(new MyClass { Name = "Joana", Description = "Doe" });
            await col.Insert(new MyClass { Name = "Doe", Description = "Doe" });
        }

        await using (var db = await LiteDatabase.Open(f.Filename))
        {
            var col = db.GetCollection<MyClass>("col");

            await foreach (var item in col.FindAll())
            {
                item.Description += " Changed";
                await col.Update(item);
            }
            // no explicit transaction needed; each write auto-commits
        }

        await using (var db = await LiteDatabase.Open(f.Filename))
        {
            var col = db.GetCollection<MyClass>("col");

            await foreach (var item in col.FindAll())
            {
                Assert.EndsWith("Changed", item.Description);
            }
        }
    }

    private class MyClass
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string Description { get; set; }
    }
}