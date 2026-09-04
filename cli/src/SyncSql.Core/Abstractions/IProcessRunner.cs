using System.Diagnostics;
using System.Text;

namespace SyncSql.Core.Abstractions;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs an external process and captures its output - the seam SyncSql.Catalog's git history mining (`git log`/`git show`, read-only) shells through, so it's unit-testable without a real `git` binary.</summary>
public interface IProcessRunner
{
    public Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The only production <see cref="IProcessRunner"/>.</summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    // Encoding.UTF8 carries a byte-order-mark preamble; nothing here writes to the child, so the
    // BOM-less instance is the honest one for decoding output.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Without these, redirected output is decoded with Console.OutputEncoding - UTF-8 on
            // Linux, but the console's OEM code page on Windows (CP850, CP437, ...). git writes
            // blob bytes out verbatim, so `git show` of a UTF-8 object file would come back
            // mojibake on Windows only, silently corrupting every non-ASCII identifier, comment,
            // and string literal in the mined historical DDL.
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environmentVariables is not null)
        {
            foreach ((string key, string value) in environmentVariables)
            {
                startInfo.Environment[key] = value;
            }
        }

        using Process process = new() { StartInfo = startInfo };
        StringBuilder stdout = new();
        StringBuilder stderr = new();

        // The captured text is reassembled with '\n', not AppendLine's Environment.NewLine: the
        // event already stripped whatever line ending the child wrote, so AppendLine would invent
        // CRLF on Windows for output the child sent as LF. Callers split this back apart on '\n'
        // (GitHistoryMiner does, for both `git log` and `git show`), and a platform-dependent
        // separator leaves a stray CR on the end of every line they get.
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.Append(e.Data).Append('\n'); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.Append(e.Data).Append('\n'); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        // WaitForExitAsync is documented to potentially return before the redirected-output event
        // handlers have processed everything; the parameterless WaitForExit() is the documented way
        // to drain them, and is instant here since the process has already exited. Without it, large
        // `git log` output can come back silently truncated.
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
