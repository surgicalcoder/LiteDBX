using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace LiteDbX.Tests.Issues;

public class Issue1651_Tests
{
    [Fact]
    public async Task Find_ByRelationId_Success()
    {
        BsonMapper.Global.Entity<Order>().DbRef(order => order.Customer);

        await using var _database = await LiteDatabase.Open(":memory:");
        var _orderCollection = _database.GetCollection<Order>("Order");
        var _customerCollection = _database.GetCollection<Customer>("Customer");

        var customer = new Customer { Name = "John" };

        Assert.True(await _customerCollection.Upsert(customer));
        Assert.True(await _customerCollection.Upsert(new Customer { Name = "Anonymous" }));

        Assert.NotEqual(Guid.Empty, customer.Id);

        var order = new Order { Customer = customer };
        var order2 = new Order { Customer = new Customer { Id = customer.Id } };
        var orphanOrder = new Order();

        Assert.True(await _orderCollection.Upsert(orphanOrder));
        Assert.True(await _orderCollection.Upsert(order));
        Assert.True(await _orderCollection.Upsert(order2));

        customer.Name = "Josh";
        Assert.True(await _customerCollection.Update(customer));

        var actualOrders = await _orderCollection
                                 .Include(orderEntity => orderEntity.Customer)
                                 .Find(orderEntity => orderEntity.Customer.Id == customer.Id)
                                 .ToListAsync();

        Assert.Equal(2, actualOrders.Count);
        Assert.Equal(new[] { customer.Name, customer.Name },
            actualOrders.Select(actualOrder => actualOrder.Customer.Name));
        Assert.Equal(2, (await _customerCollection.FindAll().ToListAsync()).Count);
        Assert.Equal(3, (await _orderCollection.FindAll().ToListAsync()).Count);
    }

    public class Order : BaseEntity
    {
        public Customer Customer { get; set; }
    }

    public class Customer : BaseEntity
    {
        public string Name { get; set; }
    }

    public class BaseEntity
    {
        public Guid Id { get; set; }
    }
}