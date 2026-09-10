namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// Where the sample fleet lives and whether this run is allowed to touch it.
///
/// The benchmark needs the containers from <c>samples/scripts/setup-databases</c> to be up, which is
/// not true of a normal <c>dotnet test</c>, so every test here is skipped unless <c>SYNCSQL_SAMPLES=1</c>
/// says otherwise. That makes the project safe to leave in the solution: `dotnet test cli/SyncSql.slnx`
/// on a machine with no Docker reports these as skipped, not failed.
/// </summary>
internal static class SampleFleet
{
    /// <summary>Set to 1/true to run the benchmark. Anything else skips it.</summary>
    public const string EnableVariable = "SYNCSQL_SAMPLES";

    /// <summary>Set to 1/true to reuse an existing samples/output instead of re-extracting.</summary>
    public const string ReuseVariable = "SYNCSQL_SAMPLES_REUSE";

    /// <summary>Overrides where the extraction is written. Default: samples/output.</summary>
    public const string OutputVariable = "SYNCSQL_SAMPLES_OUTPUT";

    /// <summary>Set to 1/true to re-record samples/expected/baseline.json from this run.</summary>
    public const string UpdateBaselineVariable = "SYNCSQL_SAMPLES_UPDATE_BASELINE";

    /// <summary>Full path to a prebuilt syncsql (an executable, or a .dll to run with `dotnet`). Optional.</summary>
    public const string CliVariable = "SYNCSQL_CLI";

    private static readonly string RepoRootValue = FindRepoRoot();

    public static bool IsEnabled { get; } = IsTruthy(Environment.GetEnvironmentVariable(EnableVariable));

    public static bool ReuseExistingOutput => IsTruthy(Environment.GetEnvironmentVariable(ReuseVariable));

    public static bool UpdateBaseline => IsTruthy(Environment.GetEnvironmentVariable(UpdateBaselineVariable));

    public static string SkipReason =>
        $"The sample fleet benchmark is off. Bring the databases up with samples/scripts/setup-databases.sh, then set {EnableVariable}=1 (or run samples/scripts/run-benchmark.sh).";

    public static string RepoRoot => RepoRootValue;

    public static string SamplesRoot => Path.Combine(RepoRootValue, "samples");

    public static string CliProjectPath => Path.Combine(RepoRootValue, "cli", "src", "SyncSql.Cli");

    public static string ConfigPath => Path.Combine(SamplesRoot, "config", "servers.samples.json");

    public static string EnvFilePath => Path.Combine(SamplesRoot, ".env");

    public static string CredentialsPath => Path.Combine(SamplesRoot, ".cache", "credentials.json");

    public static string ExpectationsPath => Path.Combine(SamplesRoot, "expected", "expectations.json");

    public static string BaselinePath => Path.Combine(SamplesRoot, "expected", "baseline.json");

    public static string OutputRoot =>
        Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : Path.Combine(SamplesRoot, "output");

    public static string CatalogPath => Path.Combine(OutputRoot, "catalog.json");

    public static string MetricsRoot => Path.Combine(OutputRoot, "metrics");

    private static bool IsTruthy(string? value) =>
        value is not null
        && (value.Equals("1", StringComparison.Ordinal)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Walks up from the test assembly until it finds the checkout - the directory holding both
    /// global.json and samples/samples.json. Test binaries sit several levels down inside bin/, and
    /// the depth differs between `dotnet test`, Visual Studio and Rider, so this cannot be a fixed
    /// number of "..".
    /// </summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json"))
                && File.Exists(Path.Combine(directory.FullName, "samples", "samples.json")))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the SyncSQL checkout by walking up from '{AppContext.BaseDirectory}' - "
            + "no ancestor directory contains both global.json and samples/samples.json.");
    }
}
