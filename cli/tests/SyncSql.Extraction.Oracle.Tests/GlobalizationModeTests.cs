using System.Globalization;

namespace SyncSql.Extraction.Oracle.Tests;

/// <summary>
/// Oracle.ManagedDataAccess.Core refuses to initialize under .NET's globalization invariant mode -
/// OracleConnection.OpenAsync throws "Globalization Invariant Mode is not supported" - so the setting
/// must stay off for every assembly in this solution. It was on once, and the only thing that noticed
/// was a live sync against an Oracle server. This test notices in CI instead; the matching build-time
/// guard lives in cli/Directory.Build.props.
/// </summary>
public class GlobalizationModeTests
{
    [Fact]
    public void Runtime_IsNotInGlobalizationInvariantMode()
    {
        bool invariantSwitchOn = AppContext.TryGetSwitch("System.Globalization.Invariant", out bool invariant) && invariant;

        Assert.False(invariantSwitchOn);
    }

    [Fact]
    public void RealCultures_AreAvailable()
    {
        // In invariant mode every requested culture collapses onto the invariant one, so a named
        // culture still resolving to its own name is the observable proof that ICU is loaded.
        CultureInfo culture = new("pt-BR");

        Assert.NotEqual(CultureInfo.InvariantCulture.EnglishName, culture.EnglishName);
        Assert.Equal("pt-BR", culture.Name);
    }
}
