using System.Text.RegularExpressions;

namespace CustomMcLauncher.Common.Utils;

/// <summary>
/// Strips raw HTML tags, image markdown, markdown markup from Modrinth body text
/// so it displays cleanly in a modern desktop interface.
/// </summary>
public static partial class MarkdownSanitizer
{
    /// <summary>Clean raw HTML/markdown body text for clean, human-readable display.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw;

        // 1. Remove HTML comments: <!-- ... -->
        s = HtmlCommentRegex().Replace(s, "");

        // 2. Remove markdown linked images: [![alt](img_url)](link_url)
        s = MdLinkedImageRegex().Replace(s, "");

        // 3. Remove standard markdown images: ![alt](url)
        s = MdImageRegex().Replace(s, "");

        // 4. Remove HTML tags: <center>, <img>, <br>, <p>, etc.
        s = HtmlTagRegex().Replace(s, "\n");

        // 5. Remove bare image URLs on their own line
        s = BareImageUrlRegex().Replace(s, "");

        // 6. Remove stray exclamation marks on their own line
        s = StrayExclamationRegex().Replace(s, "");

        // 7. Markdown headers: # Title, ## Subtitle -> Title, Subtitle
        s = HeaderRegex().Replace(s, "$1\n");

        // 8. Markdown links: [Title](url) -> Title
        s = MdLinkRegex().Replace(s, "$1");

        // 9. Markdown bold & italic: **text**, *text*, __text__, _text_
        s = BoldItalicRegex1().Replace(s, "$1");
        s = BoldItalicRegex2().Replace(s, "$1");
        s = BoldItalicRegex3().Replace(s, "$1");
        s = BoldItalicRegex4().Replace(s, "$1");

        // 10. Horizontal rules (---, ***, ___ on own line)
        s = HorizontalRuleRegex().Replace(s, "");

        // 11. HTML entities
        s = s.Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&quot;", "\"")
             .Replace("&#39;", "'")
             .Replace("&apos;", "'");

        // 12. Collapse multiple blank lines into at most two newlines
        s = MultiNewlineRegex().Replace(s, "\n\n");

        return s.Trim();
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->", RegexOptions.Compiled)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\[!\[[^\]]*\]\([^)]*\)\]\([^)]*\)", RegexOptions.Compiled)]
    private static partial Regex MdLinkedImageRegex();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled)]
    private static partial Regex MdImageRegex();

    [GeneratedRegex(@"^\s*https?://\S+\.(?:png|jpg|jpeg|gif|webp|svg)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex BareImageUrlRegex();

    [GeneratedRegex(@"^\s*!\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex StrayExclamationRegex();

    [GeneratedRegex(@"^\s*#{1,6}\s*(.+)$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HeaderRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled)]
    private static partial Regex MdLinkRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.Compiled)]
    private static partial Regex BoldItalicRegex1();

    [GeneratedRegex(@"\*(.+?)\*", RegexOptions.Compiled)]
    private static partial Regex BoldItalicRegex2();

    [GeneratedRegex(@"__(.+?)__", RegexOptions.Compiled)]
    private static partial Regex BoldItalicRegex3();

    [GeneratedRegex(@"_(.+?)_", RegexOptions.Compiled)]
    private static partial Regex BoldItalicRegex4();

    [GeneratedRegex(@"^\s*[-*_]{3,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex HorizontalRuleRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultiNewlineRegex();
}
