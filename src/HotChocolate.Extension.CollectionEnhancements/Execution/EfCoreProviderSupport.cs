using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Extension.CollectionEnhancements.Execution;

internal static class EfCoreProviderSupport
{
    public static bool TryDetect(IQueryable queryable, out EfCoreProviderInfo info)
    {
        var serviceProvider = TryGetServiceProvider(queryable) ?? TryGetServiceProvider(queryable.Provider);
        if (serviceProvider is null)
        {
            info = default;
            return false;
        }

        var provider = serviceProvider.GetService<IDatabaseProvider>();
        var providerName = provider?.Name;
        var isRelational = serviceProvider.GetService<IRelationalConnection>() is not null;
        var isEfCore = provider is not null || isRelational;

        info = new EfCoreProviderInfo(
            IsEfCore: isEfCore,
            IsRelational: isRelational,
            Family: MapProviderFamily(providerName, isRelational),
            ProviderName: providerName);
        return isEfCore;
    }

    private static IServiceProvider? TryGetServiceProvider(object candidate) =>
        candidate switch
        {
            IInfrastructure<IServiceProvider> infrastructure => infrastructure.Instance,
            _ => null
        };

    private static EfCoreProviderFamily MapProviderFamily(string? providerName, bool isRelational)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return isRelational
                ? EfCoreProviderFamily.RelationalGeneric
                : EfCoreProviderFamily.NonRelational;
        }

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreProviderFamily.SqlServer;
        }

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreProviderFamily.PostgreSql;
        }

        if (providerName.Contains("Oracle", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreProviderFamily.Oracle;
        }

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return EfCoreProviderFamily.Sqlite;
        }

        return isRelational
            ? EfCoreProviderFamily.RelationalGeneric
            : EfCoreProviderFamily.NonRelational;
    }
}

internal enum EfCoreProviderFamily
{
    None,
    SqlServer,
    PostgreSql,
    Oracle,
    Sqlite,
    RelationalGeneric,
    NonRelational
}

internal readonly record struct EfCoreProviderInfo(
    bool IsEfCore,
    bool IsRelational,
    EfCoreProviderFamily Family,
    string? ProviderName);
