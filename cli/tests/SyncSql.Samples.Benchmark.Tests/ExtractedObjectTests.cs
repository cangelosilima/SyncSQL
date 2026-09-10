using System.Text;
using SyncSql.Core.Serialization;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// The extraction half of the benchmark: what `syncsql sync` wrote to disk. These assertions are
/// deliberately about files rather than about catalog.json, because the file tree is the artifact
/// that gets committed and diffed - a catalog can be rebuilt from it, but not the other way round.
/// </summary>
[Collection(SampleBenchmarkCollection.Name)]
public sealed class ExtractedObjectTests(SampleBenchmarkFixture fleet)
{
    /// <summary>One extracted file, parsed once - several assertions below walk the whole tree.</summary>
    private sealed record ExtractedFile(string Path, string RelativePath, ParsedObjectFile Parsed);

    private IReadOnlyList<ExtractedFile>? _files;

    /// <summary>
    /// Every extracted object, and nothing else: metrics/ and metrics-snapshot/ sit beside the tree
    /// and hold JSON only, so filtering to *.sql is enough.
    /// </summary>
    private IReadOnlyList<ExtractedFile> Files => _files ??= ReadExtractedFiles();

    private IReadOnlyList<ExtractedFile> ReadExtractedFiles() =>
    [
        .. Directory
            .EnumerateFiles(fleet.OutputRoot, "*.sql", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => new ExtractedFile(
                path,
                System.IO.Path.GetRelativePath(fleet.OutputRoot, path),
                ExtractedObjectFile.Parse(File.ReadAllLines(path, Encoding.UTF8)))),
    ];

    [SampleFleetFact]
    public void EveryExpectedObjectWasWrittenToItsOwnFile()
    {
        List<string> missing = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups)
        {
            foreach (ExpectedObject expected in group.Objects)
            {
                string relative = ExtractedObjectFile.RelativePath(
                    group.Server, group.Database, expected.ResolveSchema(group), expected.Type, expected.Name);
                string path = Path.Combine(fleet.OutputRoot, relative.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(path))
                {
                    missing.Add($"{group.Describe()}: {relative}");
                }
            }
        }

        Assert.True(missing.Count == 0, BenchmarkReport.Describe("Expected objects with no extracted file", missing));
    }

    [SampleFleetFact]
    public void EveryExtractedFileCarriesTheSyncSqlHeaderAndSomeDdl()
    {
        List<string> broken = [];

        foreach (ExtractedFile file in Files)
        {
            if (file.Parsed.Identity is null)
            {
                broken.Add($"{file.RelativePath}: no '-- Identity:' header");
            }
            else if (string.IsNullOrWhiteSpace(file.Parsed.Ddl))
            {
                broken.Add($"{file.RelativePath}: header present but no DDL");
            }
        }

        Assert.True(broken.Count == 0, BenchmarkReport.Describe("Extracted files that are not readable as SyncSQL object files", broken));
    }

    [SampleFleetFact]
    public void ExpectedDdlFragmentsSurvivedTheExtraction()
    {
        List<string> mismatches = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups)
        {
            foreach (ExpectedObject expected in group.Objects.Where(o => o.DdlContains.Count > 0))
            {
                string relative = ExtractedObjectFile.RelativePath(
                    group.Server, group.Database, expected.ResolveSchema(group), expected.Type, expected.Name);
                string path = Path.Combine(fleet.OutputRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue; // Already reported by EveryExpectedObjectWasWrittenToItsOwnFile.
                }

                ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path, Encoding.UTF8));

                foreach (string fragment in expected.DdlContains)
                {
                    if (!parsed.Ddl.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    {
                        mismatches.Add($"{relative}: DDL does not contain '{fragment}'");
                    }
                }
            }
        }

        Assert.True(mismatches.Count == 0, BenchmarkReport.Describe("Extracted DDL missing expected fragments", mismatches));
    }

    [SampleFleetFact]
    public void ExpectedColumnsWereCapturedInTheColumnsSection()
    {
        List<string> mismatches = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups)
        {
            foreach (ExpectedObject expected in group.Objects.Where(o => o.Columns.Count > 0))
            {
                string relative = ExtractedObjectFile.RelativePath(
                    group.Server, group.Database, expected.ResolveSchema(group), expected.Type, expected.Name);
                string path = Path.Combine(fleet.OutputRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue;
                }

                ParsedObjectFile parsed = ExtractedObjectFile.Parse(File.ReadAllLines(path, Encoding.UTF8));
                HashSet<string> actual = new(parsed.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
                foreach (string column in expected.Columns)
                {
                    if (!actual.Contains(column))
                    {
                        mismatches.Add($"{relative}: no column '{column}' (found: {string.Join(", ", actual.Order(StringComparer.OrdinalIgnoreCase))})");
                    }
                }
            }
        }

        Assert.True(mismatches.Count == 0, BenchmarkReport.Describe("Expected columns missing from the extracted files", mismatches));
    }

    [SampleFleetFact]
    public void ObjectCountsPerTypeMatchTheUpstreamSchemas()
    {
        List<string> mismatches = [];

        foreach (ExpectedGroup group in fleet.Expectations.Groups)
        {
            foreach ((string type, int expected) in group.ExactCounts)
            {
                int actual = CountFiles(group, type);
                if (actual != expected)
                {
                    mismatches.Add($"{group.Describe()}: expected exactly {expected} {type}, found {actual}");
                }
            }

            foreach ((string type, int minimum) in group.MinimumCounts)
            {
                int actual = CountFiles(group, type);
                if (actual < minimum)
                {
                    mismatches.Add($"{group.Describe()}: expected at least {minimum} {type}, found {actual}");
                }
            }
        }

        Assert.True(mismatches.Count == 0, BenchmarkReport.Describe("Object counts that do not match the installed schemas", mismatches));
    }

    /// <summary>
    /// Counts by reading each file's identity header rather than by trusting the directory names -
    /// the export layout has changed before (see docs/adr/0002-schema-first-export-paths.md) and the
    /// header is the part that is guaranteed stable.
    /// </summary>
    private int CountFiles(ExpectedGroup group, string type) =>
        Files.Count(file =>
            file.Parsed.Identity is { } identity
            && string.Equals(identity.Server, group.Server, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Database, group.Database, StringComparison.OrdinalIgnoreCase)
            && string.Equals(identity.Type, type, StringComparison.OrdinalIgnoreCase)
            && (group.Schema is null || string.Equals(identity.Schema, group.Schema, StringComparison.OrdinalIgnoreCase)));
}
