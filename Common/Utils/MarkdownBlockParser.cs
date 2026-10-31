using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Common.Utils;

public enum MarkdownBlockType
{
    BannerImage,
    Header1,
    Header2,
    Header3,
    Paragraph,
    Bullet,
    Divider,
    Table
}

public class MarkdownTableRow
{
    public List<string> Cells { get; init; } = new();
}

public class MarkdownTableData
{
    public List<string> Headers { get; init; } = new();
    public List<MarkdownTableRow> Rows { get; init; } = new();
    public bool HasHeaders => Headers.Count > 0;
}

public partial class MarkdownBlock : ObservableObject
{
    public MarkdownBlockType Type { get; init; }
    public string Text { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public MarkdownTableData? Table { get; init; }

    [ObservableProperty]
    private Bitmap? _imageBitmap;

    public bool HasImageBitmap => ImageBitmap != null;

    public bool IsBannerImage => Type == MarkdownBlockType.BannerImage;
    public bool IsHeader1 => Type == MarkdownBlockType.Header1;
    public bool IsHeader2 => Type == MarkdownBlockType.Header2;
    public bool IsHeader3 => Type == MarkdownBlockType.Header3;
    public bool IsParagraph => Type == MarkdownBlockType.Paragraph;
    public bool IsBullet => Type == MarkdownBlockType.Bullet;
    public bool IsDivider => Type == MarkdownBlockType.Divider;
    public bool IsTable => Type == MarkdownBlockType.Table && Table != null;

    public void LoadImage()
    {
        if (string.IsNullOrWhiteSpace(ImageUrl) || ImageBitmap != null) return;
        Task.Run(async () =>
        {
            try
            {
                var bytes = await ModrinthApiService.GetIconAsync(ImageUrl);
                if (bytes is { Length: > 0 })
                {
                    using var ms = new MemoryStream(bytes);
                    var bmp = new Bitmap(ms);
                    Dispatcher.UIThread.Post(() =>
                    {
                        ImageBitmap = bmp;
                        OnPropertyChanged(nameof(HasImageBitmap));
                    });
                }
            }
            catch { }
        });
    }
}

public static class MarkdownBlockParser
{
    private static readonly Regex BannerRegex = new(@"^!\[(.*?)\]\((https?://\S+?\.(?:png|jpg|jpeg|gif|webp))\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadlineImgRegex = new(@"(?:!\[([a-zA-Z0-9\s]+)\]\([^)]*headlines[^)]*\)|<img[^>]+alt=[""']([a-zA-Z0-9\s]+)[""'][^>]*>)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[\-\*\+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex LinkRegex = new(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);

    public static List<MarkdownBlock> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var blocks = new List<MarkdownBlock>();
        int i = 0;
        bool firstBannerFound = false;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            // Top banner image (first image found in body)
            if (!firstBannerFound)
            {
                var bannerMatch = BannerRegex.Match(line);
                if (bannerMatch.Success)
                {
                    firstBannerFound = true;
                    var bannerBlock = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.BannerImage,
                        ImageUrl = bannerMatch.Groups[2].Value
                    };
                    bannerBlock.LoadImage();
                    blocks.Add(bannerBlock);
                    i++;
                    continue;
                }
            }

            // Social badges row: <p ...>...<a ...><img ...>...</a>...</p>
            if (line.Contains("<p", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("github", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("curseforge", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("modrinth", StringComparison.OrdinalIgnoreCase)))
            {
                while (i < lines.Length && !lines[i].Contains("</p>", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                }
                i++;
                continue;
            }

            // Divider: ---, ***, ___, or image divider
            if (line.StartsWith("---") || line.StartsWith("***") || line.StartsWith("___") || line.Contains("divider", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Divider });
                i++;
                continue;
            }

            // Headline image: <img ... alt="About" ...> or ![About](...headlines...)
            var headImgMatch = HeadlineImgRegex.Match(line);
            if (headImgMatch.Success)
            {
                var title = !string.IsNullOrWhiteSpace(headImgMatch.Groups[1].Value)
                    ? headImgMatch.Groups[1].Value
                    : headImgMatch.Groups[2].Value;
                title = title.Trim();
                if (!string.IsNullOrWhiteSpace(title) && !title.Equals("link", StringComparison.OrdinalIgnoreCase) && !title.Equals("github", StringComparison.OrdinalIgnoreCase) && !title.Equals("discord", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header1, Text = title });
                    i++;
                    continue;
                }
            }

            // Markdown headers #, ##, ###
            if (line.StartsWith("# "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header1, Text = CleanText(line[2..]) });
                i++;
                continue;
            }
            if (line.StartsWith("## "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header2, Text = CleanText(line[3..]) });
                i++;
                continue;
            }
            if (line.StartsWith("### "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header3, Text = CleanText(line[4..]) });
                i++;
                continue;
            }

            // Table (| Col 1 | Col 2 |)
            if (line.StartsWith('|') && line.Count(c => c == '|') >= 2)
            {
                var headers = line.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => CleanText(s.Trim())).Where(s => !string.IsNullOrEmpty(s)).ToList();
                i++;
                // Skip separator line (|---|---|)
                if (i < lines.Length && lines[i].Trim().StartsWith('|') && lines[i].Contains('-'))
                {
                    i++;
                }
                var rows = new List<MarkdownTableRow>();
                while (i < lines.Length && lines[i].Trim().StartsWith('|'))
                {
                    var cells = lines[i].Trim().Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => CleanText(s.Trim())).ToList();
                    if (cells.Count > 0)
                    {
                        rows.Add(new MarkdownTableRow { Cells = cells });
                    }
                    i++;
                }

                if (headers.Count > 0 || rows.Count > 0)
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Table,
                        Table = new MarkdownTableData { Headers = headers, Rows = rows }
                    });
                }
                continue;
            }

            // Bullets: - item, * item, + item
            var bulletMatch = BulletRegex.Match(line);
            if (bulletMatch.Success)
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Bullet, Text = CleanText(bulletMatch.Groups[1].Value) });
                i++;
                continue;
            }

            // Paragraph
            var cleaned = CleanText(line);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                // If line was just an image tag or stray brackets, skip
                if (!cleaned.Equals("!") && !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Paragraph, Text = cleaned });
                }
            }
            i++;
        }

        return blocks;
    }

    private static string CleanText(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = HtmlTagRegex.Replace(s, " ");
        s = LinkRegex.Replace(s, "$1");
        s = BoldRegex.Replace(s, "$1");
        s = s.Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&quot;", "\"")
             .Replace("&#39;", "'")
             .Replace("&apos;", "'")
             .Replace(":white_check_mark:", "✓")
             .Replace(":x:", "✗");
        return s.Trim();
    }
}
