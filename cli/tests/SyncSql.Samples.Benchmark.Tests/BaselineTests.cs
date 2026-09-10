using System.Text;

namespace SyncSql.Samples.Benchmark.Tests;

/// <summary>
/// The recorded totals, compared run over run.
///
/// This is the part of the benchmark nobody can write by hand: how many objects AdventureWorks
/// actually yields, how many lineage edges the whole fleet produces. The first run against a given
/// fleet records them into samples/expected/baseline.json and says so; every run after that compares
/// and reports what moved. Set SYNCSQL_SAMPLES_UPDATE_BASELINE=1 to re-record deliberately - for
/// instance after adding a sample or after a change that is supposed to find more references.
///
/// A moved number is not automatically a bug, which is why it is kept apart from
/// <see cref="CatalogTests"/>: those assertions come from the upstream DDL and a failure there is
/// always wrong. Here, a failure means "look at this and decide".
/// </summary>
[Collection(SampleBenchmarkCollection.Name)]
public sealed class BaselineTests(SampleBenchmarkFixture fleet)
{
    [SampleFleetFact]
    public void CatalogTotalsMatchTheRecordedBaseline()
    {
        SampleBaseline current = SampleBaseline.Capture(fleet.Catalog);
        SampleBaseline? recorded = SampleBaseline.TryLoad(SampleFleet.BaselinePath);

        if (recorded is null || SampleFleet.UpdateBaseline)
        {
            // Nothing to compare against yet (or a deliberate re-record). Writing the file is the
            // useful outcome, and the message says so loudly enough that a reviewer knows a new file
            // wants committing.
            current.Save(SampleFleet.BaselinePath);
            Assert.True(File.Exists(SampleFleet.BaselinePath),
                $"Failed to write a baseline to {SampleFleet.BaselinePath}.");
            Console.WriteLine(
                $"Recorded a benchmark baseline at {SampleFleet.BaselinePath} - review it and commit it:"
                + Environment.NewLine + current.Describe());
            return;
        }

        IReadOnlyList<string> differences = current.DiffAgainst(recorded);

        StringBuilder message = new();
        message.AppendLine($"The sample fleet no longer matches {SampleFleet.BaselinePath} ({differences.Count} differences):");
        foreach (string difference in differences)
        {
            message.AppendLine($"  - {difference}");
        }
        message.AppendLine();
        message.AppendLine($"If the change is intended, re-record with {SampleFleet.UpdateBaselineVariable}=1 and commit the new baseline.");

        Assert.True(differences.Count == 0, message.ToString());
    }
}
