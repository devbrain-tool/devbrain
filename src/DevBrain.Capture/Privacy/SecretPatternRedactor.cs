namespace DevBrain.Capture.Privacy;

using System.Text.RegularExpressions;

public class SecretPatternRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Patterns =
    [
        // GitHub PATs (ghp_, gho_, ghu_, ghs_, ghr_)
        (new Regex(@"gh[pousr]_[A-Za-z0-9_]{36,255}", RegexOptions.Compiled), "[REDACTED:github-pat]"),

        // PEM private key blocks
        (new Regex(@"-----BEGIN\s+(?:RSA\s+)?PRIVATE\s+KEY-----[\s\S]*?-----END\s+(?:RSA\s+)?PRIVATE\s+KEY-----", RegexOptions.Compiled), "[REDACTED:private-key]"),

        // Bearer tokens
        (new Regex(@"[Bb]earer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled), "[REDACTED:bearer-token]"),

        // OpenAI / Anthropic keys (sk-)
        (new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled), "[REDACTED:api-key]"),

        // Generic key=value patterns: api_key, secret, token, password
        (new Regex(@"(?i)(?:api_key|api[-_]?secret|secret|token|password)\s*[=:]\s*[""']?([A-Za-z0-9\-._~+/]{8,})[""']?", RegexOptions.Compiled), "[REDACTED:secret]"),
    ];

    public string Redact(string content)
    {
        foreach (var (pattern, replacement) in Patterns)
        {
            content = pattern.Replace(content, replacement);
        }
        return content;
    }
}
