namespace MongoFlow.Tests;

public partial class KeyModelTests
{
    [Test]
    public async Task Create_DefaultKeyWithoutAnId_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Note, string>.Create(null, Collection<Note>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_DefaultKeyOfAnotherType_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Policy, string>.Create(null, Collection<Policy>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_KeyThatIsNotAMember_ThrowsArgumentException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Policy, string>.Create(p => p.Product + p.Number, Collection<Policy>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_ConvertedMember_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Policy, long>.Create(p => p.Year, Collection<Policy>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_CompositeKeyWithoutArguments_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<LoginToken, EmptyKey>.Create(_ => new EmptyKey(), Collection<LoginToken>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_CompositeStructKeyWithoutAConstructor_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Assert.That(() => KeyModel<LoginToken, StructKey>.Create(_ => new StructKey(), Collection<LoginToken>()))
            .ThrowsExactly<VaultConfigurationException>();
    }

    [Test]
    public async Task Create_CompositeArgumentThatIsNotAMember_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Assert.That(() => KeyModel<LoginToken, TokenKey>.Create(t => new TokenKey(t.UserId + "-", t.Provider),
                Collection<LoginToken>()))
            .ThrowsExactly<VaultConfigurationException>();
    }

    [Test]
    public async Task Create_CompositeParameterWithoutAProperty_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<LoginToken, RenamedKey>.Create(t => new RenamedKey(t.UserId, t.Provider), Collection<LoginToken>()))
            .IgnoreStackTrace();
    }

    [Test]
    public async Task Create_CompositeParameterNamedLikeSeveralProperties_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<LoginToken, AmbiguousKey>.Create(t => new AmbiguousKey(t.UserId, t.Provider), Collection<LoginToken>()))
            .IgnoreStackTrace();
    }

    [Test]
    public async Task Create_CompositeArgumentConverted_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Policy, YearKey>.Create(p => new YearKey(p.Year), Collection<Policy>())).IgnoreStackTrace();
    }

    [Test]
    public async Task Create_CompositePropertyOfAnotherType_ThrowsVaultConfigurationException()
    {
        // Act & Assert
        await Throws(() => KeyModel<Policy, WideningKey>.Create(p => new WideningKey(p.Year), Collection<Policy>())).IgnoreStackTrace();
    }
}
