using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncSql.Cli;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Composition;
using SyncSql.Cli.Sync;
using System.CommandLine;

SyncSqlTerminal terminal = SyncSqlTerminal.Create();
using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddProvider(new SyncSqlConsoleLoggerProvider(terminal));
    })
    .ConfigureServices(services =>
    {
        ServiceCollectionExtensions.AddSyncSqlServices(services);
        services.AddSingleton(terminal);
    })
    .Build();

RootCommand rootCommand = new("syncsql - extraction, lineage, and catalog building for a fleet of MSSQL/Oracle servers.")
{
    ValidateConfigCommand.Build(host.Services),
    SyncCommand.Build(host.Services),
    CatalogCommand.Build(host.Services),
    MetricsCommand.Build(host.Services),
    LintCommand.Build(host.Services),
};

return await rootCommand.Parse(args).InvokeAsync();
