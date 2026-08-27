namespace SyncSql.Lineage.MsSql.Linting;

/// <summary>One lint finding: a 1-based source position plus the rule that raised it.</summary>
public sealed record TSqlLintFinding(int Line, int Column, string RuleId, TSqlLintSeverity Severity, string Message);
