using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SyncSql.Cli.Composition;

internal static class ServiceProviderExtensions
{
    public static ILogger GetLogger(this IServiceProvider services, string categoryName) =>
        services.GetRequiredService<ILoggerFactory>().CreateLogger(categoryName);
}
