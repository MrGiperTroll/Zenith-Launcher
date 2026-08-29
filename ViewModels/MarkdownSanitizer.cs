using System.Text.RegularExpressions;

namespace CustomMcLauncher.ViewModels;

/// <summary>
/// Strips raw HTML tags and image markdown from Modrinth body text
/// so it displays cleanly in a TextBlock.
/// </summary>
public static partial class MarkdownSanitizer
{
    /// <summary>Clean raw HTML/markdown body text for plain-text display.</summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw;

        // Remove HTML comments: <!-- ... -->
        s = HtmlCommentRegex().Replace(s, "");

        // Remove HTML tags: <center>, </center>, <img .../>, <br>, etc.
        s = HtmlTagRegex().Replace(s, "");

        // Remove markdown image syntax: ![alt](url)  or  [![alt](img)](link)
        s = MdImageRegex().Replace(s, "");

        // Remove bare image URLs on their own line (common in Modrinth)
        s = BareImageUrlRegex().Replace(s, "");

        // Collapse multiple blank lines into at most two newlines
        s = MultiNewlineRegex().Replace(s, "\n\n");

        return s.Trim();
    }

    [GeneratedRegex(@"<!--[\s\S]*?-->", RegexOptions.Compiled)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\[([!]?\[[^\]]*\]\([^)]*\))\]\([^)]*\)|\[[^\]]*\]\([^)]*\.(?:png|jpg|jpeg|gif|webp|svg)[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex MdImageRegex();

    [GeneratedRegex(@"^https?://\S+\.(?:png|jpg|jpeg|gif|webp|svg)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex BareImageUrlRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.Compiled)]
    private static partial Regex MultiNewlineRegex();
}
