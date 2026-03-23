using System.IO;
using FluentAssertions;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Database;

public class DbRef_Index_Tests
{
    [Fact]
    public async Task DbRef_Index()
    {
        var mapper = new BsonMapper();

        mapper.Entity<Customer>()
              .Id(x => x.Login)
              .Field(x => x.Name, "customer_name");

        mapper.Entity<Order>()
              .Id(x => x.OrderNumber)
              .Field(x => x.Customer, "cust")
              .DbRef(x => x.Customer, "customers");

        await using var db = new LiteDatabase(new MemoryStream(), mapper, new MemoryStream());
        var customer = new Customer { Login = "jd", Name = "John Doe" };
        var order = new Order { Customer = customer };

        var customers = db.GetCollection<Customer>("Customers");
        var orders = db.GetCollection<Order>("Orders");

        await customers.Insert(customer);
        await orders.Insert(order);

        await orders.EnsureIndex(x => x.Customer.Login);

        var query = await orders
                          .Include(x => x.Customer)
                          .FindOne(x => x.Customer.Login == "jd");

        query.Customer.Name.Should().Be(customer.Name);
    }

    #region Model

    public class Customer
    {
        public string Login { get; set; }
        public string Name { get; set; }
    }

    public class Order
    {
        public int OrderNumber { get; set; }
        public Customer Customer { get; set; }
    }

    #endregion
}