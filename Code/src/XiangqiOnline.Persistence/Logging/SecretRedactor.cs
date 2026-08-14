using System.Text.RegularExpressions;

namespace XiangqiOnline.Persistence.Logging;

/// <summary>
/// Redact token/secret/password from logs before writing.
/// </summary>
public static class SecretRedactor
{
    private static readonly Regex[] Patterns =
    {
        new(@"(?i)(password\s*=\s*)([^\s;]+)", RegexOptions.Compiled),
        new(@"(?i)(token\s*=\s*)([^\s;]+)", RegexOptions.Compiled),
        new(@"(?i)(secret\s*=\s*)([^\s;]+)", RegexOptions.Compiled),
        new(@"(?i)(authorization:\s*Bearer\s+)(\S+)", RegexOptions.Compiled),
        new(@"(?i)(connectionstring[^\n]*?)(password\s*=[^\s;]+)", RegexOptions.Compiled)
    };

    /// <summary>Replaces every secret occurrence with '[REDACTED]'.</summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var result = input;
        foreach (var pattern in Patterns)
        {
            result = pattern.Replace(result, m => m.Groups[1].Value + "[REDACTED]");
        }
        return result;
    }
}
