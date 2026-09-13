namespace SyncSql.Cli.Sync;

/// <summary>Small ANSI palette shared by logs and the live extraction dashboard.</summary>
internal static class TerminalColors
{
    public const string Reset = "\e[0m";
    public const string Dim = "\e[2m";
    public const string Cyan = "\e[36m";
    public const string Green = "\e[32m";
    public const string Yellow = "\e[33m";
    public const string Red = "\e[31m";
    public const string Gray = "\e[90m";

    public static string Wrap(string value, string color, bool enabled) =>
        enabled ? $"{color}{value}{Reset}" : value;

    public static string Fit(string value, int width)
    {
        if (!value.Contains('\e'))
        {
            string clean = new([.. value.Select(c => char.IsControl(c) ? ' ' : c)]);
            return clean.Length <= width ? clean : clean[..Math.Max(0, width - 3)] + "...";
        }
        if (width <= 3)
        {
            return value.Replace("\e[", "", StringComparison.Ordinal);
        }

        int visible = 0;
        int index = 0;
        System.Text.StringBuilder result = new();
        bool hasAnsi = false;
        while (index < value.Length)
        {
            if (value[index] == '\e' && index + 1 < value.Length && value[index + 1] == '[')
            {
                int end = index + 2;
                while (end < value.Length && !char.IsLetter(value[end]))
                {
                    end++;
                }
                if (end < value.Length)
                {
                    if (value[end] == 'm')
                    {
                        result.Append(value, index, end - index + 1);
                        hasAnsi = true;
                    }
                    index = end + 1;
                    continue;
                }
            }
            if (visible == width - 3)
            {
                return result + "..." + (hasAnsi ? Reset : string.Empty);
            }
            result.Append(char.IsControl(value[index]) ? ' ' : value[index]);
            visible++;
            index++;
        }
        return result.ToString();
    }

    public static bool SupportsColor(bool outputRedirected, bool errorRedirected, string? terminalType) =>
        !outputRedirected && !errorRedirected && !string.Equals(terminalType, "dumb", StringComparison.OrdinalIgnoreCase);
}
