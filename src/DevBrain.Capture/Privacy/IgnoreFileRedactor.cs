namespace DevBrain.Capture.Privacy;

using Microsoft.Extensions.FileSystemGlobbing;

public class IgnoreFileRedactor
{
    private readonly Matcher _matcher;

    public IgnoreFileRedactor(IEnumerable<string> patterns)
    {
        _matcher = new Matcher();
        foreach (var pattern in patterns)
        {
            _matcher.AddInclude(pattern);
        }
    }

    public bool ShouldIgnore(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            var result = _matcher.Match(path);
            if (result.HasMatches)
                return true;
        }
        return false;
    }

    public static IReadOnlyList<string> LoadPatterns(string ignoreFilePath)
    {
        if (!File.Exists(ignoreFilePath))
            return [];

        return File.ReadAllLines(ignoreFilePath)
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith('#'))
            .Select(line => line.Trim())
            .ToList();
    }
}
