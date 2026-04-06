namespace DevBrain.Cli.Output;

public static class ConsoleFormatter
{
    public static void PrintBox(string title, string content)
    {
        var lines = content.Split('\n');
        var maxLen = Math.Max(title.Length + 2, lines.Max(l => l.Length));
        var width = maxLen + 2;

        Console.WriteLine($"\u256d{new string('\u2500', width)}\u256e");
        Console.WriteLine($"\u2502 {title.PadRight(width - 2)} \u2502");
        Console.WriteLine($"\u251c{new string('\u2500', width)}\u2524");

        foreach (var line in lines)
        {
            Console.WriteLine($"\u2502 {line.PadRight(width - 2)} \u2502");
        }

        Console.WriteLine($"\u2570{new string('\u2500', width)}\u256f");
    }

    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[WARN] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[ERROR] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }

    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK] ");
        Console.ResetColor();
        Console.WriteLine(message);
    }
}
