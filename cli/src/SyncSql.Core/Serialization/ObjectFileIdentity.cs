namespace SyncSql.Core.Serialization;

/// <summary>Logical identity is independent of the physical export hierarchy.</summary>
public sealed record ObjectFileIdentity(string Server, string Database, string? Schema, string Type, string Name)
{
    /// <summary>Optional extraction context; older files omit it without changing their identity.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ServiceBrokerGuid { get; init; }
}
