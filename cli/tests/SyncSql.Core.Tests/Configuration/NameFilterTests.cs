using SyncSql.Core.Configuration;

namespace SyncSql.Core.Tests.Configuration;

public class NameFilterTests
{
    [Fact]
    public void IsAllowed_NoPatterns_AllowsEverything()
    {
        NameFilter filter = new();

        Assert.True(filter.IsAllowed("anything"));
    }

    [Fact]
    public void IsAllowed_EmptyInclude_AllowsEverythingNotExcluded()
    {
        NameFilter filter = new() { Exclude = ["^tempdb$"] };

        Assert.True(filter.IsAllowed("AppDb"));
        Assert.False(filter.IsAllowed("tempdb"));
    }

    [Fact]
    public void IsAllowed_WithInclude_OnlyMatchingNamesPass()
    {
        NameFilter filter = new() { Include = ["^Finance$"] };

        Assert.True(filter.IsAllowed("Finance"));
        Assert.False(filter.IsAllowed("Sales"));
    }

    [Fact]
    public void IsAllowed_ExcludeWinsOverInclude()
    {
        NameFilter filter = new() { Include = [".*"], Exclude = ["_BAK$"] };

        Assert.True(filter.IsAllowed("Orders"));
        Assert.False(filter.IsAllowed("Orders_BAK"));
    }
}
