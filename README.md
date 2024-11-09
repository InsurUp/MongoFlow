# MongoFlow

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET C#](https://img.shields.io/badge/.NET-C%23-blue)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![NuGet](https://img.shields.io/nuget/v/MongoFlow)](https://www.nuget.org/packages/MongoFlow)

> [!WARNING]
> This package is not ready for production use. It is still in development and should not be used in a production environment.
>
> We welcome your feedback! You can reach us by [opening a GitHub issue](https://github.com/InsurUp/MongoFlow.Identity/issues).

MongoFlow is a lightweight MongoDB toolkit for .NET Core, built on top of the official MongoDB.Driver. It simplifies database operations with features like Unit of work & Repository pattern, query filters, interceptors, and more.

## Features

- 🔄 Unit of Work & Repository patterns
- 🎯 Interceptors for custom logic
- 🔍 Query filters with LINQ support
- 🗑️ Built-in soft delete support
- 🏢 Multi-tenant support
- ⚡ Transaction support

## Getting Started

### Installation

To install MongoFlow, add the following package to your project:

```bash
dotnet add package MongoFlow
```

### Create Your Vault and Models

```csharp
public class BloggingVault : MongoVault
{
    public BloggingVault(VaultConfigurationManager<BloggingVault> configurationManager) : base(configurationManager)
    {
    }

    public DocumentSet<Blog> Blogs { get; set; } = null!;
}

public class Blog
{
    public ObjectId Id { get; init; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public bool Deleted { get; set; }
    public int TenantId { get; set; }
}
```

### Create Configuration

```csharp
public sealed class BloggingConfiguration : IVaultConfigurationSpecification
{
    private readonly IMongoDatabase _db;

    // You can inject singleton services here
    public BloggingConfiguration(IMongoDatabase db)
    {
        _db = db;
    }

    public void Configure(VaultConfigurationBuilder builder)
    {
        builder.SetDatabase(_db);
    }
}
```

### Register MongoVault

MongoVault is registered as a scoped service, so you can inject it into a service.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IMongoDatabase>(_ =>
{
    var client = new MongoClient("mongodb://localhost:27017");
    return client.GetDatabase("Blogging");
});

// You can combine multiple specifications
builder.Services.AddMongoVault<BloggingVault>(x => x.AddSpecification<BloggingConfiguration>());
```

### Basic MongoVault Usage

```csharp
public class BlogService
{
    private readonly BloggingVault _vault;

    public BlogService(BloggingVault vault)
    {
        _vault = vault;
    }

    public async Task<Blog> GetBlogAsync(ObjectId id)
    {
        return await _vault.Blogs.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task AddBlogAsync(Blog blog)
    {
        _vault.Blogs.Add(blog);
        await _vault.SaveAsync();
    }
}
```

### Add Documents

MongoVault is unit of work, so you can add, update or delete documents to the vault and save them all at once with a single transaction.

```csharp
var blog1 = new Blog
{
    Title = "Hello World",
    Content = "This is a blog post",
    TenantId = 1
};
var blog2 = new Blog
{
    Title = "Hello World 2",
    Content = "This is a blog post 2",
    TenantId = 2
};

vault.Blogs.Add(blog1); // It doesn't save to the database yet
vault.Blogs.Add(blog2)

await vault.SaveAsync(); // Saves all changes in a single transaction
```

### Query Documents

Because MongoFlow is built on top of MongoDB.Driver, you can use LINQ, Find, Aggregate, etc. to query documents.

```csharp
// Query documents using LINQ
var blogsWithLinq = await vault.Blogs
    .AsQueryable()
    .Where(x => x.Title.Contains("Hello"))
    .ToListAsync();

// Query documents using Find
var blogsWithFind = await vault.Blogs
    .Find(x => x.Title.Contains("Hello"))
    .ToListAsync();

// Query documents using Aggregate
var blogsWithAggregate = await vault.Blogs
    .Aggregate()
    .Match(x => x.Title.Contains("Hello"))
    .ToListAsync();
```

Also you can access the underlying `MongoDB.Driver.IMongoCollection<T>`, but it's not recommended because MongoFlow has some features that are not supported by `MongoDB.Driver.IMongoCollection<T>` like query filters, interceptors, etc.

```csharp
var collection = vault.Blogs.Collection;
```

### Interceptors

MongoFlow has interceptors to intercept the operations before or after they are sent to the database.

```csharp
public class MyInterceptor : VaultInterceptor
{
    private readonly MyService _myService;

    // You can inject services here
    public MyInterceptor(MyService myService)
    {
        _myService = myService;
    }

    // This method is called before the changes are saved to the database
    public override async ValueTask SavingChangesAsync(VaultInterceptorContext context, CancellationToken cancellationToken)
    {
        MongoVault vault = context.Vault; // You can get the vault instance
        List<VaultOperation> operations = context.Operations; // You can get and write operations that will be saved to the database
        IClientSessionHandle session = context.Session; // You can get the current session that is used to save the changes

        await _myService.DoSomethingBeforeSaveAsync();
    }

    // This method is called after the changes are saved to the database
    public override async ValueTask SavedChangesAsync(VaultInterceptorContext context, int result, CancellationToken cancellationToken)
    {
        await _myService.DoSomethingAfterSaveAsync();
    }

    // This method is called when an exception is thrown
    public override async ValueTask SaveChangesFailedAsync(Exception exception, VaultInterceptorContext context, CancellationToken cancellationToken)
    {
        await _myService.DoSomethingOnExceptionAsync(exception);
    }
}
```

Then you can register the interceptor in the `VaultConfigurationBuilder`:

```csharp
public class BloggingConfiguration : IVaultConfigurationSpecification
{
    public void Configure(VaultConfigurationBuilder builder)
    {
        builder.AddInterceptor<MyInterceptor>(); 
        // or builder.AddInterceptor(new MyInterceptor());
    }
}
```

### Query Filters

MongoFlow has built-in query filters to filter documents. Common use cases are soft delete and multi-tenancy which are supported out of the box.

To configure basic query filters, you can enable with `AddQueryFilter` method in `IVaultConfigurationSpecification`:

```csharp
public class BloggingConfiguration : IVaultConfigurationSpecification
{
    public void Configure(VaultConfigurationBuilder builder)
    {
        builder.ConfigureDocumentType<Blog>(blogBuilder =>
        {
            // Static query filter
            blogBuilder.AddQueryFilter(x => !x.Deleted);

            // You can use IServiceProvider to resolve services
            blogBuilder.AddQueryFilter(services =>
                x => x.TenantId == services.GetRequiredService<ITenantProvider>().TenantId);
        });
    }
}
```

You can ignore query filters by calling `IgnoreQueryFilters` method on `DocumentSet<T>`:

```csharp
vault.Blogs.IgnoreQueryFilter().Find(x => x.TenantId == 1).ToListAsync();
```

If you want to add query filters to all document types that are implemented by an interface, you can use `AddMultiQueryFilters` method:

```csharp
// This will add !x.Deleted to all document types that implement ISoftDelete
builder.AddMultiQueryFilters<ISoftDelete>(x => !x.Deleted);

// This will add to all document types that implement IMultiTenant
builder.AddMultiQueryFilters<IMultiTenant>(services => x => x.TenantId == services.GetRequiredService<ITenantProvider>().TenantId);
```

### Soft Delete

You can enable soft delete support with interceptors and query filters easily. However, MongoFlow supports soft delete out of the box.

To enable soft delete support, you can use `AddSoftDelete` method in `IVaultConfigurationSpecification`:

```csharp
builder.AddSoftDelete(new VaultSoftDeleteOptions<ISoftDelete>
{
    IsDeletedAccessor = x => x.Deleted,
    ChangeIsDeleted = (entity, isDeleted) => entity.Deleted = isDeleted
});
```

Now it is done! AddSoftDelete adds a query filter and interceptor to all document types that implement `ISoftDelete`.

### Multi-tenancy

To enable multi-tenancy support, you can use `AddMultiTenancy` method in `IVaultConfigurationSpecification`:

```csharp
builder.AddMultiTenancy(new VaultMultiTenancyOptions<IMultiTenant, int>
{
    TenantIdAccessor = x => x.TenantId,
    TenantIdSetter = (entity, tenantId) => entity.TenantId = tenantId,
    TenantIdProvider = serviceProvider => serviceProvider.GetRequiredService<ITenantProvider>().TenantId
});
```

### Transaction

MongoFlow applies all operations to the database with a single transaction when `SaveChangesAsync` is called. However, you can use `BeginTransaction` method to start a transaction explicitly.

```csharp
using var transaction = vault.BeginTransaction();

try
{
    // Do something
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

## Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE.md](LICENSE.md) file for details.

## Support

- 📫 [Report a bug](https://github.com/InsurUp/MongoFlow/issues)
- 💡 [Request a feature](https://github.com/InsurUp/MongoFlow/issues)
- 📖 [Documentation](https://github.com/InsurUp/MongoFlow/wiki)
