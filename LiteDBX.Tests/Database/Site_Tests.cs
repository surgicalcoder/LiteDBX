using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class Site_Tests
{
    [Fact]
    public async Task Home_Example()
    {
        using var f = new TempFile();
        await using var db = new LiteDatabase(f.Filename);

        // Get customer collection
        var customers = db.GetCollection<Customer>("customers");

        // Create your new customer instance
        var customer = new Customer
        {
            Name = "John Doe",
            Phones = new[] { "8000-0000", "9000-0000" },
            IsActive = true
        };

        // Insert new customer document (Id will be auto-incremented)
        await customers.Insert(customer);

        // Update a document inside a collection
        customer.Name = "Joana Doe";
        await customers.Update(customer);

        // Index document using a document property
        await customers.EnsureIndex(x => x.Name);

        // Now, let's create a simple query
        var results = await customers.Find(x => x.Name.StartsWith("Jo")).ToListAsync();
        results.Count.Should().Be(1);

        // Or you can query using new Query() syntax
        var results2 = await customers.Query()
            .Where(x => x.Phones.Any(p => p.StartsWith("8000")))
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToList();

        // Or using SQL
        var reader = await db.Execute(
            @"SELECT _id, Name 
                FROM customers 
               WHERE Phones ANY LIKE '8000%'
               ORDER BY Name
               LIMIT 10");

        results2.Count.Should().Be(1);
        (await reader.ToList()).Count.Should().Be(1);
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string[] Phones { get; set; }
        public bool IsActive { get; set; }
    }
}