using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using SyncSql.Cli.Commands;

namespace SyncSql.Cli.Tests;

public sealed class CatalogDynamicSqlOptionTests
{
    [Fact]
    public void CatalogRejectsRemovedDynamicSqlOptOut()
    {
        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var parsed = new RootCommand { CatalogCommand.Build(services) }
            .Parse(["catalog", "build", "--no-dynamic-sql"]);

        Assert.NotEmpty(parsed.Errors);
    }
}
