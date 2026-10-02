using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using MongoFlow;
using MongoFlow.Identity;
using MongoFlow.Samples.Configuration;
using MongoFlow.Samples.Domain;
using MongoFlow.Samples.Infrastructure;
using MongoFlow.Samples.Interceptors;
using MongoFlow.Samples.Services;
using MongoFlow.Samples.Vaults;
using MongoFlow.Samples.Walkthrough;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// From the repository root, with Docker running:
//   dotnet run --project samples/MongoFlow.Samples
// It starts MongoDB 8.2 as a replica set unless ConnectionStrings:Mongo names a server, migrates every vault, and walks
// through the platform's services as its users' requests would; see Walkthrough/PlatformWalkthrough.cs.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

await using var server = await SampleServer.StartAsync(builder.Configuration);

// How the driver stores Guids and the strongly typed AgencyId. Serialization is the driver's; MongoFlow leaves it be.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
BsonSerializer.RegisterSerializer(new AgencyIdSerializer());

// One client for most vaults; UseDatabase("name") takes it from DI. Identity data can live in a cluster of its own.
var mongo = builder.Configuration.GetConnectionString("Mongo")!;
builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongo));
builder.Services.AddKeyedSingleton<IMongoClient>("identity", (_, _) =>
    new MongoClient(builder.Configuration.GetConnectionString("Identity") ?? mongo));

// MongoFlow logs through the ILoggerFactory in DI, under MongoFlow.* categories; appsettings.json sets their levels.
builder.Logging.AddSimpleConsole(console => console.SingleLine = true);

// Tracing and metrics through OpenTelemetry: MongoFlow's saves, migrations and instruments, and the driver's commands,
// whose spans nest under MongoFlow's. Set OTEL_EXPORTER_OTLP_ENDPOINT, such as to the Aspire dashboard, to see them.
var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("mongoflow-samples"))
    .WithTracing(tracing => tracing.AddSource(MongoFlowTelemetry.ActivitySourceName, MongoTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(MongoFlowTelemetry.MeterName));

if (builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] is not null)
{
    telemetry.UseOtlpExporter();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOutboxSignal, OutboxSignal>();
builder.Services.AddScoped<RequestUser>();
builder.Services.AddScoped<ICurrentUser>(provider => provider.GetRequiredService<RequestUser>());
builder.Services.Configure<CustomerOptions>(builder.Configuration.GetSection("Customers"));

builder.Services
    .AddScoped<PolicyService>()
    .AddScoped<ClaimService>()
    .AddScoped<RenewalService>()
    .AddScoped<CustomerService>()
    .AddScoped<AgencyService>()
    .AddScoped<AccountService>()
    .AddScoped<AuditReader>()
    .AddSingleton<PlatformWalkthrough>();

// Rules for every vault. Where these calls sit relative to AddMongoVault doesn't matter.
builder.Services.AddDefaultVaultConfiguration(typeof(PlatformDefaults<>));
builder.Services.AddDefaultVaultConfiguration(typeof(SnakeCaseNaming<>));

// Runs share a server of your own, so every database name carries a prefix: Database:Prefix, or one for this run.
var prefix = builder.Configuration["Database:Prefix"] ?? (server is null ? $"samples_{DateTime.UtcNow:yyyyMMddHHmmss}_" : "");

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

// Written by the audit and outbox interceptors from inside other vaults' saves, which it joins. Platform rules don't
// apply, which also keeps the audit and outbox interceptors from running on their own writes.
builder.Services.AddMongoVault<IOutboxVault, OutboxVault>(vault => vault
    .UseDatabase(prefix + "outbox")
    .SkipAllDefaultConfigurations());

// ASP.NET Core Identity's users and roles, in a vault of MongoFlow.Identity's. Accounts sit above tenants, so the
// platform rules are skipped but the naming default is kept; the vault adds its own soft delete.
builder.Services.AddMongoVault<PlatformUserVault>((services, vault) => vault
    .UseDatabase(services.GetRequiredKeyedService<IMongoClient>("identity").GetDatabase(prefix + "identity"))
    .SkipDefaultConfiguration<PlatformDefaults<PlatformUserVault>>());

builder.Services.AddIdentityCore<PlatformUser>()
    .AddRoles<PlatformRole>()
    .AddMongoFlowStores<PlatformUserVault>();

// Disposed at the end, which flushes the telemetry still buffered.
using var app = builder.Build();
await app.StartAsync();

if (prefix.Length > 0)
{
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MongoFlow.Samples")
        .LogInformation("Writing to databases starting with {Prefix}", prefix);
}

// Pending migrations of every registered vault. A deployment of several instances runs this from one of them.
await app.Services.GetRequiredService<IVaultMigrator>().MigrateAllAsync();

await app.Services.GetRequiredService<PlatformWalkthrough>().RunAsync(CancellationToken.None);

await app.StopAsync();
