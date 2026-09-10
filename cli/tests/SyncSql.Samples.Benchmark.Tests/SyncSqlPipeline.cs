using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// Drives the real CLI over the sample fleet - the same four steps, in the same order, that
/// samples/scripts/run-syncsql.sh runs:
///
///   validate-config -> sync -> metrics update -> catalog build
///
/// The CLI runs as a child process rather than in-process on purpose: that is how a pipeline invokes
/// it, so the benchmark exercises argument parsing, exit codes and file output exactly as they ship.
/// </summary>
internal sealed class SyncSqlPipeline
{
    private readonly StringBuilder _log = new();

    /// <summary>Everything the CLI printed across the run, for attaching to a failure message.</summary>
    public string Log => _log.ToString();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        WriteCredentialsFile();
        Directory.CreateDirectory(SampleFleet.OutputRoot);

        await InvokeAsync(["validate-config", "--config", SampleFleet.ConfigPath], cancellationToken);

        await InvokeAsync(
            [
                "sync",
                "--config", SampleFleet.ConfigPath,
                "--credentials-file", SampleFleet.CredentialsPath,
                "--output-root", SampleFleet.OutputRoot,
            ],
            cancellationToken);

        await InvokeAsync(["metrics", "update", "--output-root", SampleFleet.OutputRoot], cancellationToken);

        await InvokeAsync(
            [
                "catalog", "build",
                "--output-root", SampleFleet.OutputRoot,
                "--metrics-root", SampleFleet.MetricsRoot,
            ],
            cancellationToken);
    }

    /// <summary>
    /// Credentials are read from samples/.env and handed over as a file, never as arguments: a
    /// password in argv is readable by every other process on the machine.
    /// </summary>
    private static void WriteCredentialsFile()
    {
        IReadOnlyDictionary<string, string> environment = ReadEnvFile(SampleFleet.EnvFilePath);

        string saPassword = Require(environment, "MSSQL_SA_PASSWORD");
        string oraclePassword = Require(environment, "ORACLE_PASSWORD");

        Dictionary<string, object> credentials = new(StringComparer.Ordinal)
        {
            ["SAMPLES_MSSQL"] = new { user = "sa", password = saPassword },
            ["SAMPLES_ORACLE"] = new { user = "SYSTEM", password = oraclePassword },
        };

        Directory.CreateDirectory(Path.GetDirectoryName(SampleFleet.CredentialsPath)!);
        File.WriteAllText(SampleFleet.CredentialsPath, JsonSerializer.Serialize(credentials));
    }

    private static string Require(IReadOnlyDictionary<string, string> environment, string key) =>
        environment.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"'{key}' is not set in {SampleFleet.EnvFilePath}. Run samples/scripts/setup-databases.sh first - it creates that file.");

    /// <summary>Deliberately minimal KEY=VALUE parsing: samples/.env is generated from a checked-in example, not hand-rolled shell.</summary>
    private static IReadOnlyDictionary<string, string> ReadEnvFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"{path} not found. Run samples/scripts/setup-databases.sh, which creates it from samples/docker/.env.example.", path);
        }

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int separator = trimmed.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            values[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
        }
        return values;
    }

    private async Task InvokeAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        (string fileName, List<string> allArguments) = ResolveCommand();
        allArguments.AddRange(arguments);

        ProcessStartInfo startInfo = new()
        {
            FileName = fileName,
            WorkingDirectory = SampleFleet.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in allArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        _log.AppendLine($"$ {fileName} {string.Join(' ', allArguments)}");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        _log.Append(await standardOutput);
        _log.Append(await standardError);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"syncsql {arguments[0]} exited with {process.ExitCode}.{Environment.NewLine}{Log}");
        }
    }

    /// <summary>
    /// SYNCSQL_CLI points at an already-published binary when one exists (a CI job publishes once and
    /// reuses it); otherwise the project is run from source, which also builds it on first use.
    /// </summary>
    private static (string FileName, List<string> Arguments) ResolveCommand()
    {
        string? configured = Environment.GetEnvironmentVariable(SampleFleet.CliVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? ("dotnet", [configured])
                : (configured, []);
        }

        return ("dotnet", ["run", "--project", SampleFleet.CliProjectPath, "-c", "Release", "--nologo", "--"]);
    }
}
