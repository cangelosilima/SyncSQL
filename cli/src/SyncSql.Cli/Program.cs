using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SyncSql.Cli;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Composition;
using System.CommandLine;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole(options => options.FormatterName = "syncsql");
        logging.AddConsoleFormatter<SyncSqlConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
    })
    .ConfigureServices(ServiceCollectionExtensions.AddSyncSqlServices)
    .Build();

RootCommand rootCommand = new("syncsql - extraction, lineage, and catalog building for a fleet of MSSQL/Oracle servers.")
{
    ValidateConfigCommand.Build(host.Services),
    SyncCommand.Build(host.Services),
    CatalogCommand.Build(host.Services),
    MetricsCommand.Build(host.Services),
};

return await rootCommand.Parse(args).InvokeAsync();
