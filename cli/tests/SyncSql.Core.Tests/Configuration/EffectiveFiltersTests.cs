using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public class EffectiveFiltersTests
{
    private static ServerConfig NewServer() => new()
    {
        Name = "SQLPROD01",
        Type = DatabaseEngine.MsSql,
        Host = "sqlprod01.example.com",
        CredentialsVariablePrefix = "SQLPROD01",
    };

    [Fact]
    public void Resolve_ServerWithNoOverrides_InheritsDefaults()
    {
        ObjectFilterSet defaults = new()
        {
            Databases = new NameFilter { Exclude = ["^tempdb$"] },
            ObjectTypes = ["Tables", "Views"],
        };

        EffectiveFilters effective = EffectiveFilters.Resolve(defaults, NewServer());

        Assert.False(effective.Databases.IsAllowed("tempdb"));
        Assert.Equal(["Tables", "Views"], effective.ObjectTypes);
    }

    [Fact]
    public void Resolve_ServerOverride_FullyReplacesDefaultForThatKey()
    {
        ObjectFilterSet defaults = new()
        {
            Databases = new NameFilter { Include = [".*"] },
            ObjectTypes = ["Tables", "Views", "StoredProcedures"],
        };
        ServerConfig server = NewServer() with { ObjectTypes = ["Tables"] };

        EffectiveFilters effective = EffectiveFilters.Resolve(defaults, server);

        // The server only specified objectTypes, not databases - databases still inherits the default.
        Assert.True(effective.Databases.IsAllowed("AnyDb"));
        Assert.Equal(["Tables"], effective.ObjectTypes);
    }

    [Fact]
    public void Resolve_NoDefaultsAndNoOverride_ObjectTypesIsEmpty()
    {
        EffectiveFilters effective = EffectiveFilters.Resolve(defaults: null, NewServer());

        Assert.Empty(effective.ObjectTypes);
        Assert.True(effective.Databases.IsAllowed("AnyDb"));
    }
}
