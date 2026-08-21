using Microsoft.Extensions.DependencyInjection;
using SyncSql.Catalog;
using SyncSql.Core.Abstractions;
using SyncSql.Core.Domain;
using SyncSql.Extraction.MsSql;
using SyncSql.Extraction.Oracle;
using SyncSql.Git;
using SyncSql.Lineage.MsSql;
using SyncSql.Lineage.Oracle;

namespace SyncSql.Cli.Composition;

/// <summary>The single composition root: every concrete implementation gets registered here, and nowhere else in the solution references a concrete type across project boundaries.</summary>
internal static class ServiceCollectionExtensions
{
    public static void AddSyncSqlServices(IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICredentialProvider, EnvironmentCredentialProvider>();
        services.AddSingleton<IDatabaseObjectExtractorResolver, DatabaseObjectExtractorResolver>();
        services.AddSingleton<ILineageAnalyzerResolver, LineageAnalyzerResolver>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();

        services.AddKeyedSingleton<IDatabaseObjectExtractor, MsSqlObjectExtractor>(DatabaseEngine.MsSql);
        services.AddKeyedSingleton<IDatabaseObjectExtractor, OracleObjectExtractor>(DatabaseEngine.Oracle);
        services.AddKeyedSingleton<ILineageAnalyzer, MsSqlLineageAnalyzer>(DatabaseEngine.MsSql);
        services.AddKeyedSingleton<ILineageAnalyzer, OracleLineageAnalyzer>(DatabaseEngine.Oracle);

        services.AddSingleton<IGitHistoryMiner, GitHistoryMiner>();
        services.AddSingleton<IMetricsHistoryStore, MetricsHistoryStore>();
        services.AddSingleton<ICatalogBuilder, CatalogBuilder>();
        services.AddSingleton<IGitRepository, GitRepository>();
    }
}
