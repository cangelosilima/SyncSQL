using System.Text;
using SyncSql.Core.Abstractions;

namespace SyncSql.Core.Tests.Abstractions;

/// <summary>
/// The two properties every caller of <see cref="SystemProcessRunner"/> depends on and neither can
/// see from a single OS: captured output is joined with LF (not Environment.NewLine, which would put
/// a CR on the end of every line a Windows caller then splits on '\n'), and it is decoded as UTF-8
/// (not the Windows console's OEM code page, which turns every non-ASCII byte git hands back into
/// mojibake). Both are exercised against a real child process, since both live in the plumbing
/// between ProcessStartInfo and the captured string.
/// </summary>
public sealed class SystemProcessRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("syncsql-proc-").FullName;
    private readonly SystemProcessRunner _runner = new();

    private string WriteBytes(string content, Encoding encoding)
    {
        string path = Path.Combine(_root, $"{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, encoding.GetBytes(content));
        return path;
    }

    // cat and cmd's TYPE both copy a file's bytes to a redirected stdout without transcoding them,
    // which is what makes them a usable stand-in here for `git show` writing out blob bytes.
    private async Task<ProcessResult> CatAsync(string path)
    {
        string fileName = "cat";
        IReadOnlyList<string> arguments = [path];
        if (OperatingSystem.IsWindows())
        {
            fileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            arguments = ["/c", "type", path];
        }

        return await _runner.RunAsync(fileName, arguments, cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task RunAsync_ChildWritesCrlf_OutputIsJoinedWithLfOnEveryPlatform()
    {
        string path = WriteBytes("first\r\nsecond\r\nthird\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ProcessResult result = await CatAsync(path);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal("first\nsecond\nthird\n", result.StandardOutput);
        Assert.DoesNotContain('\r', result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_ChildWritesUtf8_OutputIsDecodedAsUtf8NotTheConsoleCodePage()
    {
        // A Brazilian schema's accents plus a non-BMP emoji: under CP850/CP437 decoding, none of
        // this survives, and the mined historical DDL silently differs from the file on disk.
        const string content = "Pedidos: descrição da coleção · 注文 · 🧾";
        string path = WriteBytes(content + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        ProcessResult result = await CatAsync(path);

        Assert.True(result.Succeeded, result.StandardError);
        Assert.Equal(content, result.StandardOutput.TrimEnd('\n'));
    }

    [Fact]
    public async Task RunAsync_ChildFails_ReportsExitCodeWithoutThrowing()
    {
        ProcessResult result = await CatAsync(Path.Combine(_root, "does-not-exist.txt"));

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
