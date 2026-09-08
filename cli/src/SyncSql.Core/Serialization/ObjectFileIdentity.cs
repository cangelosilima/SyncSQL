namespace SyncSql.Core.Serialization;

/// <summary>Logical identity is independent of the physical export hierarchy.</summary>
public sealed record ObjectFileIdentity(string Server, string Database, string? Schema, string Type, string Name);
