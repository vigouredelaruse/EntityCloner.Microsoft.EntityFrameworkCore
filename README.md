
EntityCloner.Microsoft.EntityFrameworkCore
=========================================
[![Build Status](https://ci.appveyor.com/api/projects/status/github/HenkKin/EntityCloner.Microsoft.EntityFrameworkCore?branch=master&svg=true)](https://ci.appveyor.com/project/HenkKin/EntityCloner.Microsoft.EntityFrameworkCore) 
[![NuGet](https://img.shields.io/nuget/dt/EntityCloner.Microsoft.EntityFrameworkCore.svg)](https://www.nuget.org/packages/EntityCloner.Microsoft.EntityFrameworkCore) 
[![NuGet](https://img.shields.io/nuget/vpre/EntityCloner.Microsoft.EntityFrameworkCore.svg)](https://www.nuget.org/packages/EntityCloner.Microsoft.EntityFrameworkCore)


## Summary

Cloning entities using EntityFrameworkCore configuration.

This library is cross-platform and supports `net8.0`, `net9.0`, and `net10.0`.


## Installing EntityCloner.Microsoft.EntityFrameworkCore

You should install [EntityCloner.Microsoft.EntityFrameworkCore with NuGet](https://www.nuget.org/packages/EntityCloner.Microsoft.EntityFrameworkCore):

    Install-Package EntityCloner.Microsoft.EntityFrameworkCore

Or via the .NET Core command line interface:

    dotnet add package EntityCloner.Microsoft.EntityFrameworkCore

The package depends on Microsoft.EntityFrameworkCore, Newtonsoft.Json, and Microsoft.SourceLink.GitHub.

## Usage

Clones are not tracked by the EF Core change tracker. Add the cloned entity to a DbSet yourself when needed. You can clone by primary key, by a query, from an entity, or from a collection. Only relationships loaded on the source query/entity are cloned. Primary keys, included foreign keys, and concurrency tokens are reset to their default values; foreign keys for relationships that were not loaded are left unchanged.

Primary-key cloning supports composite keys and optionally configured `Include`/`ThenInclude` paths:

```csharp
var clonedOrder = await dbContext.CloneAsync<Order>(orderId);

var clonedOrderWithRelatedEntities = await dbContext.CloneAsync<Order>(
    includeQuery => includeQuery
        .Include(order => order.OrderLines)
            .ThenInclude(line => line.Discounts)
        .Include(order => order.Customer)
            .ThenInclude(customer => customer.CustomerAddresses),
    orderId);
```

`CloneOptions` controls serialization and whether entities with identical primary keys share a clone instance. Newtonsoft.Json is the default; System.Text.Json is available as an alternative. `CloneWithGraphVisitorAsync` applies a type filter and a callback to cloned entities as the graph is visited; the callback must return a non-null instance of the same runtime entity type.

The entity type must be part of the EF Core model. Collection results can use arrays, `IEnumerable<T>`, `IList<T>`, `ICollection<T>`, or their implementations.

## Documentation

The documentation is available at **[Learn Entity Framework Core](https://www.learnentityframeworkcore.com/extensions/entitycloner-microsoft-entityframeworkcore?utm_source=HenkKin&utm_medium=EntityCloner.Microsoft.EntityFrameworkCore)**.

## Sponsors

[Dapper Plus](https://dapper-plus.net/?utm_source=HenkKin&utm_medium=EntityCloner.Microsoft.EntityFrameworkCore) and [Entity Framework Extensions](https://entityframework-extensions.net/?utm_source=HenkKin&utm_medium=EntityCloner.Microsoft.EntityFrameworkCore) are major sponsors and proud to contribute to the development of EntityCloner.Microsoft.EntityFrameworkCore.

[![Dapper Plus](https://raw.githubusercontent.com/HenkKin/EntityCloner.Microsoft.EntityFrameworkCore/master/dapper-plus-sponsor.png)](https://dapper-plus.net/bulk-insert?utm_source=HenkKin&utm_medium=EntityCloner.Microsoft.EntityFrameworkCore)

[![Entity Framework Extensions](https://raw.githubusercontent.com/HenkKin/EntityCloner.Microsoft.EntityFrameworkCore/master/entity-framework-extensions-sponsor.png)](https://entityframework-extensions.net/bulk-insert?utm_source=HenkKin&utm_medium=EntityCloner.Microsoft.EntityFrameworkCore)
