using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace LiteDbX.Tests.Database;

public class DbRef_Include_Tests
{
    /*
    insert into Address values { _id:1, StreetName: '3600 S Las Vegas' };
    insert into Customer values { _id:1, Name: 'John Doe', MainAddress: { $id:1, $ref:'Address' } };
    insert into Product values { _id: 1, Name: 'TV', Price: 800, SupplierAddress: { $id:1, $ref:'Address' } };
    insert into Product values { _id: 2, Name: 'DVD', Price: 200 };
    insert into Order values { _id:1, Customer: {$id:1, $ref: 'Customer'}, CustomerNull: null,
    Products:[{$id:1, $ref:'Product'}, {$id:2, $ref:'Product'}],
    ProductArray:[{$id:1, $ref:'Product'}],
    ProductEmpty: [],
    ProductNull: null};

    select $ from order include customer, products
    */
    [Fact]
    public async Task DbRef_Include()
    {
        var mapper = new BsonMapper();

        mapper.Entity<Order>()
              .DbRef(x => x.Products, "products")
              .DbRef(x => x.ProductArray, "products")
              .DbRef(x => x.ProductColl, "products")
              .DbRef(x => x.ProductEmpty, "products")
              .DbRef(x => x.ProductsNull, "products")
              .DbRef(x => x.Customer, "customers")
              .DbRef(x => x.CustomerNull, "customers");

        mapper.Entity<Customer>()
              .DbRef(x => x.MainAddress, "addresses");

        mapper.Entity<Product>()
              .DbRef(x => x.SupplierAddress, "addresses");

        await using var db = new LiteDatabase(new MemoryStream(), mapper, new MemoryStream());

        var address = new Address { StreetName = "3600 S Las Vegas Blvd" };
        var customer = new Customer { Name = "John Doe", MainAddress = address };

        var product1 = new Product { Name = "TV", Price = 800, SupplierAddress = address };
        var product2 = new Product { Name = "DVD", Price = 200 };

        var customers = db.GetCollection<Customer>("customers");
        var addresses = db.GetCollection<Address>("addresses");
        var products = db.GetCollection<Product>("products");
        var orders = db.GetCollection<Order>("orders");

        await addresses.Insert(address);
        await customers.Insert(customer);
        await products.Insert(new[] { product1, product2 });

        var order = new Order
        {
            Customer = customer,
            CustomerNull = null,
            Products = new List<Product> { product1, product2 },
            ProductArray = new[] { product1 },
            ProductColl = new List<Product> { product2 },
            ProductEmpty = new List<Product>(),
            ProductsNull = null
        };

        mapper.SerializeNullValues = true;

        await orders.Insert(order);

        // query orders using customer $id, include customer data and customer address
        var r1 = await orders
            .Include(x => x.Customer)
            .Include(x => x.Customer.MainAddress)
            .Find(x => x.Customer.Id == 1)
            .FirstOrDefaultAsync();

        r1.Id.Should().Be(order.Id);
        r1.Customer.Name.Should().Be(order.Customer.Name);
        r1.Customer.MainAddress.StreetName.Should().Be(order.Customer.MainAddress.StreetName);

        // include all
        var result = await orders
            .Include(x => x.Customer)
            .Include("Customer.MainAddress")
            .Include(x => x.CustomerNull)
            .Include(x => x.Products)
            .Include(x => x.Products.Select(p => p.SupplierAddress))
            .Include(x => x.ProductArray)
            .Include(x => x.ProductColl)
            .Include(x => x.ProductsNull)
            .FindAll()
            .FirstOrDefaultAsync();

        result.Customer.Name.Should().Be(customer.Name);
        result.Customer.MainAddress.StreetName.Should().Be(customer.MainAddress.StreetName);
        result.Products[0].Price.Should().Be(product1.Price);
        result.Products[1].Name.Should().Be(product2.Name);
        result.Products[0].SupplierAddress.StreetName.Should().Be(product1.SupplierAddress.StreetName);
        result.ProductArray[0].Name.Should().Be(product1.Name);
        result.ProductColl.ElementAt(0).Price.Should().Be(product2.Price);
        result.ProductsNull.Should().BeNull();
        result.ProductEmpty.Count.Should().Be(0);

        // now, delete reference 1x1 and 1xN
        await customers.Delete(customer.Id);
        await products.Delete(product1.ProductId);

        var result2 = await orders
            .Include(x => x.Customer)
            .Include(x => x.Products)
            .FindAll()
            .FirstOrDefaultAsync();

        // must missing customer and has only 1 product
        result2.Customer.Should().BeNull();
        result2.Products.Count.Should().Be(1);

        // property ProductArray contains only deleted "product1", but has no include on query, so must returns deleted
        result2.ProductArray.Length.Should().Be(1);
    }

    #region Model

    public class Order
    {
        public ObjectId Id { get; set; }
        public Customer Customer { get; set; }
        public Customer CustomerNull { get; set; }

        public List<Product> Products { get; set; }
        public Product[] ProductArray { get; set; }
        public ICollection<Product> ProductColl { get; set; }
        public List<Product> ProductEmpty { get; set; }
        public List<Product> ProductsNull { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public Address MainAddress { get; set; }
    }

    public class Address
    {
        public int Id { get; set; }
        public string StreetName { get; set; }
    }

    public class Product
    {
        public int ProductId { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public Address SupplierAddress { get; set; }
    }

    #endregion
}