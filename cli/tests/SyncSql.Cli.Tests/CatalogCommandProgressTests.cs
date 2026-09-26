using System.CommandLine;
using System.CommandLine.Invocation;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SyncSql.Cli.Commands;
using SyncSql.Cli.Sync;
using SyncSql.Core.Abstractions;

namespace SyncSql.Cli.Tests;

public sealed class CatalogCommandProgressTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HandledBuildErrorsReturnFailureAndRestoreTerminal(bool missingDirectory)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        var builder = Substitute.For<ICatalogBuilder>();
        Exception failure = missingDirectory ? new DirectoryNotFoundException("Missing input") : new InvalidDataException("Invalid catalog");
        builder.BuildAsync(Arg.Any<CatalogBuildRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<Core.Domain.Catalog>(failure));
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddSingleton(builder);
        registrations.AddSingleton(new SyncSqlTerminal(output, animated: true));
        using ServiceProvider services = registrations.BuildServiceProvider();

        int exit = await new RootCommand { CatalogCommand.Build(services) }
            .Parse(["catalog", "build", "--objects-root", Path.GetTempPath()]).InvokeAsync();
        Assert.Equal(1, exit);
        Assert.Contains("Catalog Failed", output.ToString());
        Assert.DoesNotContain("Catalog Done", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InterruptedBuild_PreservesExceptionAndTerminalStatus(bool cancelled)
    {
        using StringWriter output = new(CultureInfo.InvariantCulture);
        using CancellationTokenSource cancellation = new();
        var builder = Substitute.For<ICatalogBuilder>();
        Exception failure = cancelled ? new OperationCanceledException(cancellation.Token) : new IOException("Disk failed");
        builder.BuildAsync(Arg.Any<CatalogBuildRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (cancelled)
            {
                cancellation.Cancel();
            }
            return Task.FromException<Core.Domain.Catalog>(failure);
        });
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddSingleton(builder);
        registrations.AddSingleton(new SyncSqlTerminal(output, animated: true));
        using ServiceProvider services = registrations.BuildServiceProvider();
        var parsed = new RootCommand { CatalogCommand.Build(services) }
            .Parse(["catalog", "build", "--objects-root", Path.GetTempPath()]);
        var action = Assert.IsAssignableFrom<AsynchronousCommandLineAction>(parsed.Action);
        Exception? actual = await Record.ExceptionAsync(() => action.InvokeAsync(parsed, cancellation.Token));
        Assert.Same(failure, actual);
        Assert.Contains(cancelled ? "Catalog Cancelled" : "Catalog Failed", output.ToString());
        Assert.EndsWith("\e[?25h", output.ToString());
    }
}
