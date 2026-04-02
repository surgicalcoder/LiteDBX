using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1860_Tests
{
    public enum EnumAB
    {
        A = 1,
        B = 2
    }

    [Fact]
    public async Task Constructor_has_enum_bsonctor()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col1 = db.GetCollection<C1>("c1");
        var col3 = db.GetCollection<C3>("c3");

        var c1 = new C1 { Id = 1, EnumAB = EnumAB.B };
        await col1.Insert(c1);

        var c3 = new C3(1, EnumAB.B);
        await col3.Insert(c3);

        var value1 = await col1.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value1);
        Assert.Equal(c1.EnumAB, value1.EnumAB);

        var value3 = await col3.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value3);
        Assert.Equal(c3.EnumAB, value3.EnumAB);
    }

    [Fact]
    public async Task Constructor_has_enum()
    {
        await using var db = await LiteDatabase.Open(":memory:");
        var col1 = db.GetCollection<C1>("c1");
        var col2 = db.GetCollection<C2>("c2");

        var c1 = new C1 { Id = 1, EnumAB = EnumAB.B };
        await col1.Insert(c1);

        var c2 = new C2(1, EnumAB.B);
        await col2.Insert(c2);

        var value1 = await col1.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value1);
        Assert.Equal(c1.EnumAB, value1.EnumAB);

        var value2 = await col2.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value2);
        Assert.Equal(c2.EnumAB, value2.EnumAB);
    }

    [Fact]
    public async Task Constructor_has_enum_asint()
    {
        await using var db = await LiteDatabase.Open(":memory:", new BsonMapper { EnumAsInteger = true });
        var col1 = db.GetCollection<C1>("c1");
        var col2 = db.GetCollection<C2>("c2");

        var c1 = new C1 { Id = 1, EnumAB = EnumAB.B };
        await col1.Insert(c1);

        var c2 = new C2(1, EnumAB.B);
        await col2.Insert(c2);

        var value1 = await col1.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value1);
        Assert.Equal(c1.EnumAB, value1.EnumAB);

        var value2 = await col2.FindAll().FirstOrDefaultAsync();
        Assert.NotNull(value2);
        Assert.Equal(c2.EnumAB, value2.EnumAB);
    }

    public class C1
    {
        public int Id { get; set; }

        public EnumAB? EnumAB { get; set; }
    }

    public class C2
    {
        public C2(int id, EnumAB enumAB)
        {
            Id = id;
            EnumAB = enumAB;
        }

        public int Id { get; set; }

        public EnumAB EnumAB { get; set; }
    }

    public class C3
    {
        [BsonCtor]
        public C3(int id, EnumAB enumAB)
        {
            Id = id;
            EnumAB = enumAB;
        }

        public int Id { get; set; }

        public EnumAB EnumAB { get; set; }
    }
}