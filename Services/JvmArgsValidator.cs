using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CustomMcLauncher.Services;

public record JvmValidationResult(
    bool HasIncompatibleFlags,
    IReadOnlyList<string> IncompatibleFlags,
    string WarningMessage,
    string CleanedArgs);

public static class JvmArgsValidator
{
    private static readonly string[] ObsoleteModernFlags =
    {
        "-XX:+UseConcMarkSweepGC",
        "-XX:-UseConcMarkSweepGC",
        "-XX:+CMSIncrementalMode",
        "-XX:-CMSIncrementalMode",
        "-XX:+CMSClassUnloadingEnabled",
        "-XX:-CMSClassUnloadingEnabled",
        "-XX:+CMSPermGenSweepingEnabled",
        "-XX:-CMSPermGenSweepingEnabled",
        "-XX:+UseParNewGC",
        "-XX:-UseParNewGC",
        "-Xincgc",
        "-XX:+AggressiveOpts",
        "-XX:-AggressiveOpts"
    };

    private static readonly string[] ObsoleteModernPrefixes =
    {
        "-XX:MaxPermSize=",
        "-XX:PermSize="
    };

    private static readonly string[] ModernOnlyPrefixes =
    {
        "--add-opens",
        "--add-exports",
        "--add-modules",
        "--add-reads",
        "--patch-module",
        "--illegal-access"
    };

    public static JvmValidationResult Validate(string? jvmArgs, int javaMajor)
    {
        if (string.IsNullOrWhiteSpace(jvmArgs))
        {
            return new JvmValidationResult(false, Array.Empty<string>(), string.Empty, string.Empty);
        }

        var tokens = SplitArgs(jvmArgs);
        var incompatible = new List<string>();
        var tokensToRemove = new HashSet<int>();

        if (javaMajor >= 17)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (ObsoleteModernFlags.Any(f => string.Equals(f, token, StringComparison.OrdinalIgnoreCase)) ||
                    ObsoleteModernPrefixes.Any(p => token.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                {
                    incompatible.Add(token);
                    tokensToRemove.Add(i);
                }
            }
        }
        else if (javaMajor > 0 && javaMajor <= 8)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var matchedPrefix = ModernOnlyPrefixes.FirstOrDefault(p =>
                    token.Equals(p, StringComparison.OrdinalIgnoreCase) ||
                    token.StartsWith(p + "=", StringComparison.OrdinalIgnoreCase));

                if (matchedPrefix != null)
                {
                    incompatible.Add(token);
                    tokensToRemove.Add(i);

                    // Handle separate value argument: e.g. --add-opens java.base/java.lang=ALL-UNNAMED
                    if (token.Equals(matchedPrefix, StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count && !tokens[i + 1].StartsWith("-"))
                    {
                        tokensToRemove.Add(i + 1);
                    }
                }
            }
        }

        if (incompatible.Count == 0)
        {
            return new JvmValidationResult(false, Array.Empty<string>(), string.Empty, jvmArgs.Trim());
        }

        var remaining = tokens.Where((_, index) => !tokensToRemove.Contains(index));
        var cleaned = string.Join(" ", remaining).Trim();

        var flagsList = string.Join(", ", incompatible.Distinct());
        var warning = $"{L10n.T("jvm_warn_incompatible")} {flagsList}";

        return new JvmValidationResult(true, incompatible, warning, cleaned);
    }

    private static List<string> SplitArgs(string args)
    {
        var result = new List<string>();
        var matches = Regex.Matches(args, @"[\""].+?[\""]|[^ ]+");
        foreach (Match m in matches)
        {
            if (!string.IsNullOrWhiteSpace(m.Value))
                result.Add(m.Value);
        }
        return result;
    }
}
