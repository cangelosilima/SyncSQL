namespace SyncSql.Core.Credentials;

/// <summary>Thrown when a credential parameter or credentials file can't be understood. Always carries a message meant to be shown directly to the user.</summary>
public sealed class CredentialParseException(string message) : Exception(message);
