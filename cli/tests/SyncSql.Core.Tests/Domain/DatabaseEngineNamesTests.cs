using SyncSql.Core.Domain;

namespace SyncSql.Core.Tests.Domain;

public sealed class DatabaseEngineNamesTests
{
    [Theory]
    [InlineData("mssql", DatabaseEngine.MsSql)]
    [InlineData(" MSSQL ", DatabaseEngine.MsSql)]
    [InlineData("oracle", DatabaseEngine.Oracle)]
    [InlineData(" ORACLE ", DatabaseEngine.Oracle)]
    public void Parse_AcceptsSupportedNames(string value, DatabaseEngine expected)
    {
        Assert.True(DatabaseEngineNames.TryParse(value, out DatabaseEngine actual));
        Assert.Equal(expected, actual);
        Assert.Equal(expected, DatabaseEngineNames.Parse(value));
        Assert.Equal(value.Trim().ToLowerInvariant(), actual.ToConfigString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("postgres")]
    public void TryParse_RejectsUnsupportedNames(string? value)
    {
        Assert.False(DatabaseEngineNames.TryParse(value, out DatabaseEngine actual));
        Assert.Equal(default, actual);
        Assert.Throws<FormatException>(() => DatabaseEngineNames.Parse(value!));
    }

    [Fact]
    public void ToConfigString_RejectsUnknownEnumValue()
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(
            () => ((DatabaseEngine)42).ToConfigString());
        Assert.Equal("engine", error.ParamName);
    }
}
