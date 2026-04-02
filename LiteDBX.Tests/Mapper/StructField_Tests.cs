using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Mapper;

public class StructFields_Tests
{
    [Fact]
    public async Task Serialize_Struct_Fields()
    {
        var m = new BsonMapper();

        m.IncludeFields = true;

        await using (var db = await LiteDatabase.Open(new MemoryStream(), m, new MemoryStream()))
        {
            var col = db.GetCollection<Point2D>("mytable");

            await col.Insert(new Point2D { X = 10, Y = 120 });
            await col.Insert(new Point2D { X = 15, Y = 130 });
            await col.Insert(new Point2D { X = 20, Y = 140 });

            var col2 = db.GetCollection<Point2D>("mytable");

            _ = await col2.Query().Select(p => p.Y).ToArray();
            _ = await col2.Query().Select(p => p.X).ToArray();
            _ = await col2.Query().ToArray();
            _ = await col2.Query().Select(p => new { NewX = p.X, NewY = p.Y }).ToArray();
        }
    }

    public struct Point2D
    {
        public int X;
        public int Y;
    }
}