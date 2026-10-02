using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoFlow.Identity;

namespace MongoFlow.IntegrationTests;

public partial class MongoUserStoreTests
{
    [Test]
    public async Task UpdateAsync_UserChangedSinceItWasRead_FailsWithConcurrencyFailureAndKeepsItsStamp()
    {
        // Arrange — two requests read Ada; the first saves a change, which renews her stamp.
        await using var host = Host();
        await Users(host).CreateAsync(Ada());
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstUsers = first.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var secondUsers = second.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var firstAda = (await firstUsers.FindByIdAsync(AdaId.ToString()))!;
        var secondAda = (await secondUsers.FindByIdAsync(AdaId.ToString()))!;
        var read = secondAda.ConcurrencyStamp;
        await firstUsers.SetPhoneNumberAsync(firstAda, "+90 555 000 00 01");

        // Act
        var result = await secondUsers.SetPhoneNumberAsync(secondAda, "+90 555 000 00 02");

        // Assert
        await Assert.That(secondAda.ConcurrencyStamp).IsEqualTo(read);
        await Verify(new { result.Errors, Stored = IdentityDocuments.Stable(await host.StoredAsync("Users")) });
    }

    [Test]
    public async Task UpdateAsync_UserAnotherTransactionIsChanging_FailsWithConcurrencyFailure()
    {
        // Arrange — the first request changes Ada in a transaction it hasn't committed yet.
        await using var host = Host();
        await Users(host).CreateAsync(Ada());
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstUsers = first.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var secondUsers = second.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var firstAda = (await firstUsers.FindByIdAsync(AdaId.ToString()))!;
        var secondAda = (await secondUsers.FindByIdAsync(AdaId.ToString()))!;
        await using var transaction = await first.ServiceProvider.GetRequiredService<IVaultTransactionManager>().BeginAsync();
        await firstUsers.SetPhoneNumberAsync(firstAda, "+90 555 000 00 01");

        // Act
        var result = await secondUsers.SetPhoneNumberAsync(secondAda, "+90 555 000 00 02");

        // Assert
        await Verify(result.Errors);
    }

    [Test]
    public async Task UpdateAsync_RejectedByTheServer_ThrowsItsError()
    {
        // Arrange — the app keeps phone numbers unique, which Identity doesn't check.
        await using var host = Host();
        await host.Database.GetCollection<MongoUser>("Users").Indexes.CreateOneAsync(new CreateIndexModel<MongoUser>(
            Builders<MongoUser>.IndexKeys.Ascending(x => x.PhoneNumber),
            new CreateIndexOptions<MongoUser>
            {
                Unique = true,
                PartialFilterExpression = Builders<MongoUser>.Filter.Type(x => x.PhoneNumber, BsonType.String)
            }));
        var users = Users(host);
        var ada = Ada();
        var bob = new MongoUser { UserName = "bob", Email = "bob@example.com" };
        await users.CreateAsync(ada);
        await users.CreateAsync(bob);
        await users.SetPhoneNumberAsync(ada, "+90 555 000 00 01");

        // Act & Assert — only a write conflict is a concurrency failure.
        await Assert.That(Task () => users.SetPhoneNumberAsync(bob, "+90 555 000 00 01")).ThrowsExactly<ClientBulkWriteException>();
    }

    [Test]
    public async Task DeleteAsync_UserChangedSinceItWasRead_FailsWithConcurrencyFailure()
    {
        // Arrange
        await using var host = Host();
        await Users(host).CreateAsync(Ada());
        await using var first = host.CreateScope();
        await using var second = host.CreateScope();
        var firstUsers = first.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var secondUsers = second.ServiceProvider.GetRequiredService<UserManager<MongoUser>>();
        var firstAda = (await firstUsers.FindByIdAsync(AdaId.ToString()))!;
        var secondAda = (await secondUsers.FindByIdAsync(AdaId.ToString()))!;
        await firstUsers.SetPhoneNumberAsync(firstAda, "+90 555 000 00 01");

        // Act
        var result = await secondUsers.DeleteAsync(secondAda);

        // Assert
        await Verify(new { result.Errors, Stored = IdentityDocuments.Stable(await host.StoredAsync("Users")) });
    }

    [Test]
    public async Task UpdateAsync_Succeeding_RenewsTheStoredStamp()
    {
        // Arrange
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        await users.CreateAsync(ada);
        var created = ada.ConcurrencyStamp;

        // Act
        await users.SetPhoneNumberAsync(ada, "+90 555 000 00 01");

        // Assert
        await Assert.That(ada.ConcurrencyStamp).IsNotEqualTo(created);
        await Assert.That((await host.StoredAsync("Users"))[0]["ConcurrencyStamp"].AsString).IsEqualTo(ada.ConcurrencyStamp);
    }

    [Test]
    public async Task DeleteAsync_UserWithTokens_DeletesThemToo()
    {
        // Arrange — another user's token stays.
        await using var host = Host();
        var users = Users(host);
        var ada = Ada();
        var bob = new MongoUser { UserName = "bob" };
        await users.CreateAsync(ada);
        await users.CreateAsync(bob);
        await users.SetAuthenticationTokenAsync(ada, "authenticator", "key", "ada's");
        await users.SetAuthenticationTokenAsync(bob, "authenticator", "key", "bob's");

        // Act
        await users.DeleteAsync(ada);

        // Assert
        await Verify((await host.StoredAsync("UserTokens")).Select(token => token["Value"]));
    }
}
