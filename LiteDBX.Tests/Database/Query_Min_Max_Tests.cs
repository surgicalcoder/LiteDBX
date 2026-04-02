using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Query_Min_Max_Tests
{
    [Fact]
    public async Task Query_Min_Max()
    {
        using (var f = new TempFile())
        {
            await using (var db = await LiteDatabase.Open(f.Filename))
            {
                var c = db.GetCollection<EntityMinMax>("col");

                await c.Insert(new EntityMinMax());
                await c.Insert(new EntityMinMax
                {
                    ByteValue = 200,
                    IntValue = 443500,
                    LongValue = 443500,
                    UintValue = 443500
                });

                await c.EnsureIndex(x => x.ByteValue);
                await c.EnsureIndex(x => x.IntValue);
                await c.EnsureIndex(x => x.LongValue);
                await c.EnsureIndex(x => x.UintValue);

                (await c.Max(x => x.ByteValue)).Should().Be(200);
                (await c.Max(x => x.IntValue)).Should().Be(443500);
                (await c.Max(x => x.LongValue)).Should().Be(443500L);
                (await c.Max(x => x.UintValue)).Should().Be(443500U);
            }
        }
    }

    #region Model

    public class EntityMinMax
    {
        public int Id { get; set; }
        public byte ByteValue { get; set; }
        public int IntValue { get; set; }
        public uint UintValue { get; set; }
        public long LongValue { get; set; }
    }

    #endregion
}