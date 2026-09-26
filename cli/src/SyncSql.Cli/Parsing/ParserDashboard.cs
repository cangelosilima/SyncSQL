using System.Text;

namespace SyncSql.Cli.Parsing;

internal static class ParserDashboard
{
    // Never allow SQL text, filenames or diagnostics to inject terminal control sequences.
    internal static string Safe(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());

    public static void Print(SqlInspection inspection, TextWriter output)
    {
        output.WriteLine($"{Safe(inspection.Path)} ({inspection.Engine})");
        foreach (SqlSection section in inspection.Sections)
        {
            output.WriteLine($"\n[{section.Name}] {section.Pieces.Count}");
            foreach (SqlPiece piece in section.Pieces)
            {
                foreach (string line in inspection.Describe(piece).Split('\n'))
                {
                    output.WriteLine(Safe(line));
                }
            }
        }
    }

    public static void Run(SqlInspection inspection, CancellationToken cancellationToken)
    {
        DashboardState state = new(inspection);
        bool originalControlC = Console.TreatControlCAsInput;
        try
        {
            Console.TreatControlCAsInput = true;
            Console.Write("\u001b[?1049h\u001b[?25l");
            int lastWidth = 0;
            int lastHeight = 0;
            bool redraw = true;
            while (!cancellationToken.IsCancellationRequested)
            {
                int width = Math.Max(1, Console.WindowWidth);
                int height = Math.Max(1, Console.WindowHeight);
                if (redraw || width != lastWidth || height != lastHeight)
                {
                    Console.Write(Render(state, width, height));
                    lastWidth = width;
                    lastHeight = height;
                    redraw = false;
                }
                if (Console.KeyAvailable)
                {
                    if (!state.Handle(Console.ReadKey(intercept: true), Math.Max(1, height - 7))) { break; }
                    redraw = true;
                }
                else
                {
                    cancellationToken.WaitHandle.WaitOne(80);
                }
            }
        }
        finally
        {
            Console.Write("\u001b[0m\u001b[?25h\u001b[?1049l");
            Console.TreatControlCAsInput = originalControlC;
        }
    }

    internal static string Render(DashboardState state, int width, int height)
    {
        // Leave the final terminal column unused to avoid automatic line wrapping.
        int columns = Math.Max(1, width - 1);
        List<string> lines = [];
        if (width < 70 || height < 12)
        {
            lines.Add("Resize terminal to at least 70 x 12. Press q to quit.");
        }
        else
        {
            SqlInspection inspection = state.Inspection;
            lines.Add($" SYNCSQL / PARSER   {inspection.Engine.ToUpperInvariant()}   {(inspection.HasErrors ? "PARSE ERRORS - partial tree" : "PARSED")}");
            lines.Add(" " + inspection.Path);
            lines.Add(" " + string.Join(" ", inspection.Sections.Select((s, i) =>
                $"{(i == state.Section ? ">" : "")}{i + 1}:{s.Name.Replace("Syntax tree", "Tree", StringComparison.Ordinal).Replace("Diagnostics", "Errors", StringComparison.Ordinal)}")));
            int left = Math.Clamp(columns / 3, 24, 48);
            int right = columns - left - 3;
            lines.Add(Fit($" {inspection.Sections[state.Section].Name} / {state.Items.Count} pieces", left) + " | " + "DETAIL / SQL");
            int rows = height - 7;
            int start = state.Selected / rows * rows;
            string[] details = state.Items.Count == 0 ? ["No pieces match this view."] :
                inspection.Describe(state.Items[state.Selected]).Replace("\r", "", StringComparison.Ordinal).Split('\n');
            state.DetailRow = Math.Clamp(state.DetailRow, 0, Math.Max(0, details.Length - rows));
            for (int row = 0; row < rows; row++)
            {
                int index = start + row;
                string label = index < state.Items.Count ?
                    $"{(index == state.Selected ? ">" : " ")} {state.Items[index].Name}" : "";
                int detailIndex = state.DetailRow + row;
                string detail = detailIndex < details.Length ? details[detailIndex] : "";
                detail = detail[Math.Min(state.DetailColumn, detail.Length)..];
                string line = Fit(label, left) + " | " + Fit(detail, right);
                lines.Add(line);
            }
            lines.Add($" / Filter: {state.Filter}{(state.EditingFilter ? "_ (Enter to apply, Esc to clear)" : "")}  | detail line {state.DetailRow + 1}/{details.Length}");
            lines.Add(" Tab/Left/Right: view  Up/Down/j/k: select  1-7: view  /: filter");
            lines.Add(" PgUp/PgDn: scroll detail  h/l: pan detail  Home/End: first/last  q/Esc: quit");
        }
        StringBuilder frame = new("\u001b[H");
        for (int row = 0; row < height; row++)
        {
            if (row > 0) { frame.Append("\r\n"); }
            if (row < 4) { frame.Append("\u001b[36m"); }
            frame.Append(Fit(row < lines.Count ? lines[row] : "", columns));
            frame.Append("\u001b[0m");
        }
        return frame.ToString();
    }

    private static string Fit(string text, int width)
    {
        string safe = Safe(text);
        return safe.Length > width ? safe[..width] : safe.PadRight(width);
    }
}

internal sealed class DashboardState(SqlInspection inspection)
{
    public SqlInspection Inspection { get; } = inspection;
    public int Section { get; private set; }
    public int Selected { get; private set; }
    public int DetailRow { get; set; }
    public int DetailColumn { get; private set; }
    public string Filter { get; private set; } = "";
    public bool EditingFilter { get; private set; }
    public IReadOnlyList<SqlPiece> Items { get; private set; } = inspection.Sections[0].Pieces;

    public bool Handle(ConsoleKeyInfo key, int pageSize)
    {
        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)) { return false; }
        if (EditingFilter)
        {
            if (key.Key == ConsoleKey.Enter) { EditingFilter = false; }
            else if (key.Key == ConsoleKey.Escape) { Filter = ""; EditingFilter = false; }
            else if (key.Key == ConsoleKey.Backspace) { Filter = Filter[..Math.Max(0, Filter.Length - 1)]; }
            else if (!char.IsControl(key.KeyChar)) { Filter += key.KeyChar; }
            Refresh();
            return true;
        }
        if (key.Key is ConsoleKey.Q or ConsoleKey.Escape) { return false; }
        if (key.KeyChar == '/') { EditingFilter = true; return true; }
        int section = Section;
        if (key.Key == ConsoleKey.Tab) { section += key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1; }
        else if (key.Key == ConsoleKey.RightArrow) { section++; }
        else if (key.Key == ConsoleKey.LeftArrow) { section--; }
        else if (key.KeyChar is >= '1' and <= '7') { section = key.KeyChar - '1'; }
        if (section != Section)
        {
            Section = (section + Inspection.Sections.Count) % Inspection.Sections.Count;
            Filter = "";
            Refresh();
            return true;
        }
        int selected = Selected;
        switch (key.Key)
        {
            case ConsoleKey.DownArrow: case ConsoleKey.J: selected++; break;
            case ConsoleKey.UpArrow: case ConsoleKey.K: selected--; break;
            case ConsoleKey.Home: selected = 0; break;
            case ConsoleKey.End: selected = Items.Count - 1; break;
            case ConsoleKey.PageDown: DetailRow += pageSize; break;
            case ConsoleKey.PageUp: DetailRow = Math.Max(0, DetailRow - pageSize); break;
            case ConsoleKey.H: DetailColumn = Math.Max(0, DetailColumn - 8); break;
            case ConsoleKey.L: DetailColumn += 8; break;
        }
        selected = Math.Clamp(selected, 0, Math.Max(0, Items.Count - 1));
        if (selected != Selected) { Selected = selected; DetailRow = 0; DetailColumn = 0; }
        return true;
    }

    private void Refresh()
    {
        Items = Inspection.Sections[Section].Pieces.Where(p => p.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
            (p.Description?.Contains(Filter, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
        Selected = 0;
        DetailRow = 0;
        DetailColumn = 0;
    }
}
