using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using MongoFlow;
using MongoFlow.Samples.Configuration;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Identity;
using MongoFlow.Samples.Infrastructure;
using MongoFlow.Samples.Vaults;

var builder = Host.CreateApplicationBuilder(args);

// One client for most vaults; UseDatabase("name") takes it from DI. Identity data lives in a separate cluster.
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017"));
builder.Services.AddKeyedSingleton<IMongoClient>("identity", (_, _) =>
    new MongoClient(builder.Configuration.GetConnectionString("Identity") ?? "mongodb://localhost:27018"));

builder.Services.AddScoped<ICurrentUser, AnonymousUser>();
builder.Services.Configure<CustomerOptions>(builder.Configuration.GetSection("Customers"));

// Rules for every vault. Where these calls sit relative to AddMongoVault doesn't matter.
builder.Services.AddDefaultVaultConfiguration(typeof(PlatformDefaults<>));
builder.Services.AddDefaultVaultConfiguration(typeof(SnakeCaseNaming<>));

// Test runs share one server, so every database name carries a per-run prefix.
var prefix = builder.Configuration["Database:Prefix"] ?? "";

// Its shape comes from PolicyVault.Configure; only the database is set here.
builder.Services.AddMongoVault<IPolicyVault, PolicyVault>(vault => vault.UseDatabase(prefix + "policies"));

builder.Services.AddMongoVault<CustomerVault>(vault => vault
    .UseDatabase(prefix + "customers")
    .UseConfiguration<CustomerVaultConfiguration>());

// The platform writes audit entries for every tenant, so its rules don't apply; reads are still limited to one tenant.
builder.Services.AddMongoVault<IAuditVault, AuditVault>(vault => vault
    .UseDatabase(prefix + "audit")
    .SkipAllDefaultConfigurations()
    .UseMultiTenancy(
        (ITenantOwned x) => x.AgencyId,
        services => services.GetRequiredService<ICurrentUser>().AgencyId));

// Accounts sit above tenants, so the platform rules are skipped but the naming default is kept. The identity
// package's setup can't be a default, so it's applied here.
builder.Services.AddMongoVault<PlatformUserVault>((services, vault) => vault
    .UseDatabase(services.GetRequiredKeyedService<IMongoClient>("identity").GetDatabase(prefix + "identity"))
    .SkipDefaultConfiguration<PlatformDefaults<PlatformUserVault>>()
    .UseConfiguration<UserVaultConfiguration<PlatformUserVault, PlatformUser>>());

var app = builder.Build();

await app.RunAsync();
