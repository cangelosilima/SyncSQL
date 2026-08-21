#nullable enable
using Antlr4.Runtime;

namespace SyncSql.Lineage.Oracle;

/// <summary>Replaces ANTLR's default error listener (which writes straight to the console) with one that collects messages so the analyzer can log them through ILogger instead, and so a syntax error never means writing to the process's stderr out of nowhere.</summary>
internal sealed class CollectingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
{
    public List<string> Errors { get; } = [];

    public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) =>
        Errors.Add($"line {line}:{charPositionInLine} {msg}");

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine, string msg, RecognitionException e) =>
        Errors.Add($"line {line}:{charPositionInLine} {msg}");
}
