using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql catalog build` - standalone catalog.json builder, mirrors Build-Catalog.ps1. Wired up once SyncSql.Catalog exists.</summary>
internal static class CatalogCommand
{
    public static Command Build(IServiceProvider services)
    {
        Command buildCommand = new("build", "Build catalog.json from an extracted-objects tree.");
        buildCommand.SetAction((_, _) =>
        {
            services.GetLogger(nameof(CatalogCommand)).LogError("Not yet implemented - see the SyncSql.Catalog phase.");
            return Task.FromResult(1);
        });

        return new Command("catalog", "Catalog-related commands.") { buildCommand };
    }
}
