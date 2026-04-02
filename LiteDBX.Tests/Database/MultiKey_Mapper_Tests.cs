using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class MultiKey_Mapper_Tests
{
    [Fact]
    public async Task MultiKey_Mapper()
    {
        await using (var db = await LiteDatabase.Open(":memory:"))
        {
            var col = db.GetCollection<MultiKeyDoc>("col");

            await col.Insert(new MultiKeyDoc
            {
                Id = 1,
                Keys = new[] { 1, 2, 3 },
                Customers = new List<Customer>
                {
                    new() { Name = "John" },
                    new() { Name = "Ana" },
                    new() { Name = "Doe" },
                    new() { Name = "Dante" }
                }
            });

            await col.Insert(new MultiKeyDoc
            {
                Id = 2,
                Keys = new[] { 2 },
                Customers = new List<Customer>
                {
                    new() { Name = "Ana" }
                }
            });

            await col.EnsureIndex(x => x.Keys);
            await col.EnsureIndex(x => x.Customers.Select(z => z.Name));

            // Query.EQ("Keys", 2)
            (await col.Count(Query.Any().EQ("Keys", 2))).Should().Be(2);
            (await col.Count(x => x.Keys.Contains(2))).Should().Be(2);

            (await col.Count(Query.Any().StartsWith("Customers[*].Name", "Ana"))).Should().Be(2);
            (await col.Count(x => x.Customers.Select(z => z.Name).Any(z => z.StartsWith("Ana")))).Should().Be(2);

            (await col.Count(Query.Any().StartsWith("Customers[*].Name", "D"))).Should().Be(1);
            (await col.Count(x => x.Customers.Select(z => z.Name).Any(z => z.StartsWith("D")))).Should().Be(1);
        }
    }

    #region Model

    public class MultiKeyDoc
    {
        public int Id { get; set; }
        public int[] Keys { get; set; }
        public List<Customer> Customers { get; set; }
    }

    public class Customer
    {
        public string Login { get; set; }
        public string Name { get; set; }
    }

    #endregion
}