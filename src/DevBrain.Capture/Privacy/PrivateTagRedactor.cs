namespace DevBrain.Capture.Privacy;

using System.Text.RegularExpressions;

public class PrivateTagRedactor
{
    private static readonly Regex PrivateTagPattern = new(
        @"<private>[\s\S]*?</private>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Redact(string content)
    {
        return PrivateTagPattern.Replace(content, "[REDACTED:private]");
    }
}
