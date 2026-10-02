using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver.Linq;

namespace MongoFlow.IntegrationTests;

public partial class ReadTests
{
    private const string Agency = "a-1";

    [Test]
    public async Task QueryAsync_GroupJoinOnAnotherCollectionsQuery_GroupsWhatItsQueryFiltersShow()
    {
        // Arrange
        await using var host = await AgencyHostAsync();
        var vault = host.Vault;

        // Act
        var customers = await (from customer in await vault.Customers.QueryAsync()
                               join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId into contracts
                               orderby customer.Id
                               select new { customer.Name, Contracts = contracts.Select(x => x.Number) }).ToListAsync();

        // Assert
        await Verify(customers).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task QueryAsync_JoinOnAnotherCollectionsQuery_PairsWhatItsQueryFiltersShow()
    {
        // Arrange
        await using var host = await AgencyHostAsync();
        var vault = host.Vault;
        var pairs = from customer in await vault.Customers.QueryAsync()
                    join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId
                    orderby contract.Id
                    select new { customer.Name, contract.Number };

        // Act
        var read = new
        {
            Pairs = await pairs.ToListAsync(),
            Count = await pairs.CountAsync(),
            First = await pairs.FirstOrDefaultAsync(),
            Enumerated = pairs.ToList()
        };

        // Assert
        await Verify(read);
    }

    [Test]
    public async Task QueryAsync_LeftJoinOnAnotherCollectionsQuery_KeepsCustomersWithoutMatches()
    {
        // Arrange
        await using var host = await AgencyHostAsync();
        var vault = host.Vault;

        // Act
        var joined = new
        {
            Query = await (from customer in await vault.Customers.QueryAsync()
                           join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId into contracts
                           from contract in contracts.DefaultIfEmpty()
                           orderby customer.Id
                           select new { customer.Name, Number = contract == null ? null : contract.Number }).ToListAsync(),
            Method = await (await vault.Customers.QueryAsync())
                .LeftJoin(await vault.Contracts.QueryAsync(),
                    customer => customer.Id,
                    contract => contract.CustomerId,
                    (customer, contract) => new { customer.Name, Number = contract == null ? null : contract.Number })
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Number)
                .ToListAsync()
        };

        // Assert
        await Verify(joined);
    }

    [Test]
    public async Task QueryAsync_TwoJoinsInARow_PairWhatEachOnesQueryFiltersShow()
    {
        // Arrange
        await using var host = await AgencyHostAsync();
        var vault = host.Vault;

        // Act
        var payments = await (from customer in await vault.Customers.QueryAsync()
                              join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId
                              join payment in await vault.Payments.QueryAsync() on contract.Id equals payment.ContractId
                              select new { customer.Name, contract.Number, payment.Amount }).ToListAsync();

        // Assert
        await Verify(payments);
    }

    [Test]
    public async Task QueryAsync_JoinOnAQueryWithAnAsynchronousFilter_LooksUpThroughIt()
    {
        // Arrange
        await using var host = await AgencyHostAsync(vault => vault.Collection(x => x.Contracts, contracts => contracts
            .QueryFilter(async (_, cancellationToken) =>
            {
                await Task.Delay(0, cancellationToken);
                return x => x.Number != "C-10";
            })));
        var vault = host.Vault;

        // Act
        var numbers = await (from customer in await vault.Customers.QueryAsync()
                             join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId
                             select contract.Number).ToListAsync();

        // Assert
        await Assert.That(numbers).IsEquivalentTo(["C-14"]);
    }

    [Test]
    public async Task QueryAsync_JoinInATransaction_SeesItsUncommittedWrites()
    {
        // Arrange
        await using var host = await AgencyHostAsync();
        await using var transaction = await host.Transactions.BeginAsync();
        host.Vault.Contracts.Add(new Contract { Id = 20, CustomerId = 4, Number = "C-20" });
        await host.Vault.SaveAsync();
        await using var outside = host.CreateScope();

        // Act
        var seen = new
        {
            Inside = await NumbersOfDeeAsync(host.Vault),
            Outside = await NumbersOfDeeAsync(outside.ServiceProvider.GetRequiredService<AgencyVault>())
        };

        // Assert
        await Verify(seen).DontIgnoreEmptyCollections();
    }

    [Test]
    public async Task QueryAsync_JoinThroughATrackingView_TracksNothing()
    {
        // Arrange — a join returns its own shape, not the stored documents.
        await using var host = await AgencyHostAsync(vault => vault.UseChangeTracking());
        var vault = host.Vault;
        var joined = await (from customer in await vault.Customers.QueryAsync()
                            join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId into contracts
                            select new { Customer = customer, Contracts = contracts }).ToListAsync();
        joined[0].Customer.Name = "changed";

        // Act
        var result = await vault.SaveAsync();

        // Assert
        await Assert.That(result).IsEqualTo(SaveResult.Empty);
    }

    private static async Task<List<string>> NumbersOfDeeAsync(AgencyVault vault) =>
        await (from customer in await vault.Customers.QueryAsync()
               join contract in await vault.Contracts.QueryAsync() on customer.Id equals contract.CustomerId
               where customer.Name == "dee"
               select contract.Number).ToListAsync();

    /// <summary>
    /// Ada has a contract of each kind: shown (C-10, C-14), removed (C-11) and another agency's (C-12); bob is removed,
    /// cy is another agency's, and dee has no contract.
    /// </summary>
    private async Task<VaultHost<AgencyVault>> AgencyHostAsync(Action<IVaultBuilder<AgencyVault>>? configure = null)
    {
        var host = Mongo.Host<AgencyVault>(vault =>
        {
            vault.UseSoftDelete((IRemovable x) => x.IsRemoved)
                .UseMultiTenancy((IAgencyOwned x) => x.AgencyId, _ => Agency);
            configure?.Invoke(vault);
        });

        await host.SeedAsync("Customers",
            new Customer { Id = 1, Name = "ada", AgencyId = Agency },
            new Customer { Id = 2, Name = "bob", AgencyId = Agency, IsRemoved = true },
            new Customer { Id = 3, Name = "cy", AgencyId = "a-2" },
            new Customer { Id = 4, Name = "dee", AgencyId = Agency });
        await host.SeedAsync("Contracts",
            new Contract { Id = 10, CustomerId = 1, Number = "C-10", AgencyId = Agency },
            new Contract { Id = 11, CustomerId = 1, Number = "C-11", AgencyId = Agency, IsRemoved = true },
            new Contract { Id = 12, CustomerId = 1, Number = "C-12", AgencyId = "a-2" },
            new Contract { Id = 13, CustomerId = 2, Number = "C-13", AgencyId = Agency },
            new Contract { Id = 14, CustomerId = 1, Number = "C-14", AgencyId = Agency });
        await host.SeedAsync("Payments",
            new Payment { Id = 100, ContractId = 10, Amount = 50, AgencyId = Agency },
            new Payment { Id = 101, ContractId = 10, Amount = 60, AgencyId = "a-2" },
            new Payment { Id = 102, ContractId = 11, Amount = 70, AgencyId = Agency });

        return host;
    }
}
