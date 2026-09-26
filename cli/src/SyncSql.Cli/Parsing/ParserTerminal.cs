namespace SyncSql.Cli.Parsing;

// Keep the operating-system console boundary separate from navigation and lifecycle logic.
internal class ParserTerminal
{
    public virtual TextWriter Output => Console.Out;
    public virtual TextWriter Error => Console.Error;
    public virtual bool InputRedirected => Console.IsInputRedirected;
    public virtual bool OutputRedirected => Console.IsOutputRedirected;
    public virtual string? TerminalType => Environment.GetEnvironmentVariable("TERM");
    public virtual int Width => Console.WindowWidth;
    public virtual int Height => Console.WindowHeight;
    public virtual bool KeyAvailable => Console.KeyAvailable;
    public virtual bool ControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }
    public virtual ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);
    public virtual void Wait(CancellationToken cancellationToken) => cancellationToken.WaitHandle.WaitOne(80);
}
