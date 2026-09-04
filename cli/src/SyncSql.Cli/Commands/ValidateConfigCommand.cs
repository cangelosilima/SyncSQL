using Microsoft.Extensions.Logging;
using SyncSql.Cli.Composition;
using SyncSql.Core.Configuration;
using System.CommandLine;

namespace SyncSql.Cli.Commands;

/// <summary>`syncsql validate-config [--config &lt;path&gt;]` - parses and validates a config/servers.json file without touching any database or git remote.</summary>
internal static class ValidateConfigCommand
{
    public static Command Build(IServiceProvider services)
    {
        Option<string> configOption = new("--config")
        {
            Description = "Path to the config/servers.json file to validate, relative to the current directory unless absolute.",
            DefaultValueFactory = _ => SyncSqlPaths.DefaultConfigPath,
        };

        Command command = new("validate-config", "Parse and validate a config/servers.json file.")
        {
            configOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            string configPath = Path.GetFullPath(parseResult.GetValue(configOption) ?? SyncSqlPaths.DefaultConfigPath);
            ILogger logger = services.GetLogger(nameof(ValidateConfigCommand));

            try
            {
                SyncSqlConfig config = await SyncSqlConfigLoader.LoadAsync(configPath, cancellationToken);
                logger.LogInformation("OK: {ServerCount} server(s) defined in {Path}", config.Servers.Count, configPath);
                return 0;
            }
            catch (ConfigValidationException ex)
            {
                logger.LogError("{Message}", ex.Message);
                return 1;
            }
        });

        return command;
    }
}
