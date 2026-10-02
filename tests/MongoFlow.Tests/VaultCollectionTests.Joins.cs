using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace MongoFlow.Tests;

public partial class VaultCollectionTests
{
    [Test]
    public async Task QueryAsync_JoinsOnAnotherCollectionsQuery_LookUpThroughItsQueryFilters()
    {
        // Arrange
        await using var host = SalesHost();
        var vault = host.Vault;

        // Act
        var joins = new
        {
            GroupJoin = (from order in await vault.Orders.QueryAsync()
                         join customer in await vault.Customers.QueryAsync() on order.Customer equals customer.Name into customers
                         select new { order.Id, Customers = customers }).ToString(),
            Join = (from order in await vault.Orders.QueryAsync()
                    join customer in await vault.Customers.QueryAsync() on order.Customer equals customer.Name
                    select new { order.Id, customer.Name }).ToString(),
            LeftJoin = (from order in await vault.Orders.QueryAsync()
                        join customer in await vault.Customers.QueryAsync() on order.Customer equals customer.Name into customers
                        from customer in customers.DefaultIfEmpty()
                        select new { order.Id, Name = customer == null ? null : customer.Name }).ToString(),
            LeftJoinMethod = (await vault.Orders.QueryAsync()).LeftJoin(await vault.Customers.QueryAsync(),
                order => order.Customer,
                customer => customer.Name,
                (order, customer) => new { order.Id, Name = customer == null ? null : customer.Name }).ToString()
        };

        // Assert
        await Verify(joins);
    }

    [Test]
    public async Task QueryAsync_JoinOnAQueryWithFiltersOfItsOwn_LooksUpThroughThemToo()
    {
        // Arrange
        await using var host = SalesHost();
        var customers = (await host.Vault.Customers.QueryAsync()).Where(x => x.Name != "ada").Where(x => x.Id > 1);

        // Act
        var query = from order in await host.Vault.Orders.QueryAsync()
                    join customer in customers on order.Customer equals customer.Name
                    select new { order.Id, customer.Name };

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryAsync_TwoJoinsInARow_LookUpThroughEachOnesQueryFilters()
    {
        // Arrange
        await using var host = SalesHost();
        var vault = host.Vault;

        // Act
        var query = from order in await vault.Orders.QueryAsync()
                    join customer in await vault.Customers.QueryAsync() on order.Customer equals customer.Name
                    join other in await vault.Orders.QueryAsync() on customer.Name equals other.Customer
                    select new { order.Id, Other = other.Id };

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryAsync_JoinOnAQueryWithoutQueryFilters_IsTheDriversOwnJoin()
    {
        // Arrange
        await using var host = SalesHost();
        var customers = await host.Vault.Customers.Without(SoftDeleteFeature.Key).QueryAsync();

        // Act
        var query = from order in await host.Vault.Orders.QueryAsync()
                    join customer in customers on order.Customer equals customer.Name
                    select new { order.Id, customer.Name };

        // Assert
        await Verify(query.ToString());
    }

    [Test]
    public async Task QueryAsync_JoinOnAPagedQuery_IsLeftToTheDriver()
    {
        // Arrange — a page inside the $lookup would apply to each order's matches, not to the customers.
        await using var host = SalesHost();
        var customers = (await host.Vault.Customers.QueryAsync()).Take(1);
        var query = from order in await host.Vault.Orders.QueryAsync()
                    join customer in customers on order.Customer equals customer.Name
                    select new { order.Id, customer.Name };

        // Act & Assert
        await Assert.That(() => query.ToListAsync()).ThrowsExactly<ExpressionNotSupportedException>();
    }

    [Test]
    public async Task QueryAsync_JoinOnAQueryNotFromAVault_IsLeftToTheDriver()
    {
        // Arrange
        await using var host = SalesHost();
        var customers = host.Vault.Customers.MongoCollection.AsQueryable().Where(x => !x.IsDeleted);
        var query = from order in await host.Vault.Orders.QueryAsync()
                    join customer in customers on order.Customer equals customer.Name
                    select new { order.Id, customer.Name };

        // Act & Assert
        await Assert.That(() => query.ToListAsync()).ThrowsExactly<ExpressionNotSupportedException>();
    }

    [Test]
    public async Task QueryAsync_JoinOnAQueryOfAnotherDatabase_Throws()
    {
        // Arrange — a $lookup would look for the customers in the orders' database, and find none.
        await using var host = SalesHost(services => services.AddMongoVault<ArchiveVault>(vault => vault
            .UseDatabase(ArchiveVault.Database)));
        var orders = await host.Vault.Orders.QueryAsync();
        var archived = await host.Services.GetRequiredService<ArchiveVault>().Customers.Without(SoftDeleteFeature.Key)
            .QueryAsync();

        // Act & Assert
        await Throws(() => orders.Join(archived, order => order.Customer, customer => customer.Name, (order, _) => order.Id))
            .IgnoreStackTrace();
    }

    private static VaultHost<SalesVault> SalesHost(Action<IServiceCollection>? services = null) =>
        new(vault => vault.UseSoftDelete((ISoftDeletable x) => x.IsDeleted), services);
}
