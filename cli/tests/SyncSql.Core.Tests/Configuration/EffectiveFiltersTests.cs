using SyncSql.Core.Configuration;
using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Configuration;

public class EffectiveFiltersTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, null, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(null, false, false)]
    public void DefaultExclusions_ResolveIndependentlyOfExplicitFilters(bool? defaultsValue, bool? serverValue, bool expected)
    {
        ObjectFilterSet defaults = new() { UseDefaultExclusions = defaultsValue, Schemas = new() { Exclude = ["^private$"] } };
        EffectiveFilters filters = EffectiveFilters.Resolve(defaults, NewServer() with { UseDefaultExclusions = serverValue });
        Assert.Equal(expected, filters.UseDefaultExclusions);
        Assert.Equal(!expected, filters.Databases.IsAllowed("MASTER"));
        Assert.Equal(!expected, filters.Schemas.IsAllowed("sys"));
        Assert.False(filters.Schemas.IsAllowed("private"));
        Assert.True(filters.Schemas.IsAllowed("dbo"));
        Assert.Equal(["^private$"], defaults.Schemas.Exclude);
    }

    [Theory]
    [InlineData(DatabaseEngine.MsSql, "master", "sys", "orders")]
    [InlineData(DatabaseEngine.MsSql, "MODEL", "information_schema", "orders")]
    [InlineData(DatabaseEngine.MsSql, "msdb", "SYS", "orders")]
    [InlineData(DatabaseEngine.MsSql, "tempdb", "INFORMATION_SCHEMA", "orders")]
    [InlineData(DatabaseEngine.Oracle, "APP", "SYS", "BIN$abc")]
    [InlineData(DatabaseEngine.Oracle, "APP", "system", "bin$abc")]
    [InlineData(DatabaseEngine.Oracle, "APP", "APEX_240200", "BIN$abc")]
    [InlineData(DatabaseEngine.Oracle, "APP", "FLOWS_030000", "BIN$abc")]
    public void DefaultExclusions_AreEngineSpecificAndAdditive(DatabaseEngine engine, string database, string schema, string name)
    {
        ServerConfig server = NewServer() with
        {
            Type = engine,
            Schemas = new() { Include = [".*"], Exclude = ["^private$"] },
            ObjectNames = new() { Include = [".*"], Exclude = ["^skip$"] },
        };
        EffectiveFilters filters = EffectiveFilters.Resolve(null, server);
        Assert.Equal(engine == DatabaseEngine.Oracle, filters.Databases.IsAllowed(database));
        Assert.False(filters.Schemas.IsAllowed(schema));
        Assert.Equal(engine == DatabaseEngine.MsSql, filters.ObjectNames.IsAllowed(name));
        Assert.False(filters.Schemas.IsAllowed("private"));
        Assert.False(filters.ObjectNames.IsAllowed("skip"));
        Assert.True(filters.Schemas.IsAllowed("PUBLIC"));
        Assert.True(filters.Schemas.IsAllowed("APP"));
        Assert.True(filters.ObjectNames.IsAllowed("SYS_APPLICATION_TABLE"));
        Assert.True(filters.ObjectNames.IsAllowed("TMP_REPORT"));

        EffectiveFilters disabled = EffectiveFilters.Resolve(null, server with { UseDefaultExclusions = false });
        Assert.True(disabled.Databases.IsAllowed(database));
        Assert.True(disabled.Schemas.IsAllowed(schema));
        Assert.True(disabled.ObjectNames.IsAllowed(name));
        Assert.False(disabled.ObjectNames.IsAllowed("skip"));
    }

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
