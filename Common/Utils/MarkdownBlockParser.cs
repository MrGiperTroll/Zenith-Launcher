using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CustomMcLauncher.Services;

namespace CustomMcLauncher.Common.Utils;

public enum MarkdownBlockType
{
    BannerImage,
    SocialButtons,
    DividerImage,
    DividerLine,
    HeadlineImage,
    Header1,
    Header2,
    Header3,
    Paragraph,
    Bullet,
    Table
}

public class MarkdownSpan
{
    public string Text { get; init; } = "";
    public bool IsBold { get; init; }
    public bool IsCode { get; init; }
}

public class MarkdownParagraphControl : TextBlock
{
    public static readonly StyledProperty<List<MarkdownSpan>?> SpansProperty =
        AvaloniaProperty.Register<MarkdownParagraphControl, List<MarkdownSpan>?>(nameof(Spans));

    public List<MarkdownSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    static MarkdownParagraphControl()
    {
        SpansProperty.Changed.AddClassHandler<MarkdownParagraphControl>((x, e) => x.UpdateInlines());
    }

    private void UpdateInlines()
    {
        Inlines?.Clear();
        if (Spans == null || Spans.Count == 0)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                Inlines?.Add(new Run(Text));
            }
            return;
        }

        foreach (var s in Spans)
        {
            var run = new Run(s.Text)
            {
                FontWeight = s.IsBold ? FontWeight.Bold : FontWeight.Normal,
                Foreground = s.IsBold ? new SolidColorBrush(Color.Parse("#E2E8F0")) : new SolidColorBrush(Color.Parse("#94A3B8"))
            };
            Inlines?.Add(run);
        }
    }
}

public class MarkdownTableRow
{
    public List<string> Cells { get; init; } = new();
}

public class MarkdownTableData
{
    public List<string> Headers { get; init; } = new();
    public List<MarkdownTableRow> Rows { get; init; } = new();
}

public partial class MarkdownSocialButton : ObservableObject
{
    public string LinkUrl { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string Alt { get; init; } = "";

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    public bool HasIconBitmap => IconBitmap != null;

    public void LoadIcon()
    {
        if (string.IsNullOrWhiteSpace(ImageUrl) || IconBitmap != null) return;
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
                        IconBitmap = bmp;
                        OnPropertyChanged(nameof(HasIconBitmap));
                    });
                }
            }
            catch { }
        });
    }
}

public partial class MarkdownBlock : ObservableObject
{
    public MarkdownBlockType Type { get; init; }
    public string Text { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public MarkdownTableData? Table { get; init; }
    public List<MarkdownSpan> Spans { get; init; } = new();
    public List<MarkdownSocialButton> SocialButtons { get; init; } = new();

    [ObservableProperty]
    private Bitmap? _imageBitmap;

    public bool HasImageBitmap => ImageBitmap != null;

    public bool IsBannerImage => Type == MarkdownBlockType.BannerImage;
    public bool IsSocialButtons => Type == MarkdownBlockType.SocialButtons && SocialButtons.Count > 0;
    public bool IsDividerImage => Type == MarkdownBlockType.DividerImage;
    public bool IsDividerLine => Type == MarkdownBlockType.DividerLine;
    public bool IsHeadlineImage => Type == MarkdownBlockType.HeadlineImage;
    public bool IsHeader1 => Type == MarkdownBlockType.Header1;
    public bool IsHeader2 => Type == MarkdownBlockType.Header2;
    public bool IsHeader3 => Type == MarkdownBlockType.Header3;
    public bool IsParagraph => Type == MarkdownBlockType.Paragraph;
    public bool IsBullet => Type == MarkdownBlockType.Bullet;
    public bool IsTable => Type == MarkdownBlockType.Table && Table != null;

    public void LoadImages()
    {
        if (IsSocialButtons)
        {
            foreach (var btn in SocialButtons)
            {
                btn.LoadIcon();
            }
            return;
        }

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
    private static readonly Regex BannerRegex = new(@"\[?!\[(.*?)\]\((https?://\S+?\.(?:png|jpg|jpeg|gif|webp))\)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadlineImgRegex = new(@"(?:<img\s+[^>]*src=[""']([^""']+headlines[^""']+)[""'][^>]*alt=[""']([^""']*)[""']|!\[([^\]]*)\]\((https?://[^)]+headlines[^)]+)\))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DividerImgRegex = new(@"(?:!\[Divider\]\((https?://[^\)]+)\)|<img\s+[^>]*divider[^>]*src=[""']([^""']+)[""'])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SocialRowRegex = new(@"<a\s+href=[""']([^""']+)[""'][^>]*>\s*<img\s+src=[""']([^""']+)[""'][^>]*alt=[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[\-\*\+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    public static List<MarkdownBlock> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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

            // 1. Social buttons row: <p align="center">...<a href="..."><img ...></a>...</p>
            if (line.Contains("<p", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("github", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("curseforge", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("modrinth", StringComparison.OrdinalIgnoreCase)))
            {
                var pChunk = line;
                while (i < lines.Length && !lines[i].Contains("</p>", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i < lines.Length) pChunk += " " + lines[i].Trim();
                }
                var buttons = new List<MarkdownSocialButton>();
                foreach (Match m in SocialRowRegex.Matches(pChunk))
                {
                    buttons.Add(new MarkdownSocialButton
                    {
                        LinkUrl = m.Groups[1].Value,
                        ImageUrl = m.Groups[2].Value,
                        Alt = m.Groups[3].Value
                    });
                }
                if (buttons.Count > 0)
                {
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.SocialButtons,
                        SocialButtons = buttons
                    };
                    block.LoadImages();
                    blocks.Add(block);
                }
                i++;
                continue;
            }

            // 2. Divider image: ![Divider](...divider...)
            var divMatch = DividerImgRegex.Match(line);
            if (divMatch.Success)
            {
                var url = !string.IsNullOrWhiteSpace(divMatch.Groups[1].Value) ? divMatch.Groups[1].Value : divMatch.Groups[2].Value;
                var block = new MarkdownBlock
                {
                    Type = MarkdownBlockType.DividerImage,
                    ImageUrl = url
                };
                block.LoadImages();
                blocks.Add(block);
                i++;
                continue;
            }

            // Plain divider: ---, ***, ___
            if (line.StartsWith("---") || line.StartsWith("***") || line.StartsWith("___"))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.DividerLine });
                i++;
                continue;
            }

            // 3. Headline image: About.png, Features.png, etc.
            var headImgMatch = HeadlineImgRegex.Match(line);
            if (headImgMatch.Success)
            {
                var url = !string.IsNullOrWhiteSpace(headImgMatch.Groups[1].Value) ? headImgMatch.Groups[1].Value : headImgMatch.Groups[4].Value;
                var alt = !string.IsNullOrWhiteSpace(headImgMatch.Groups[2].Value) ? headImgMatch.Groups[2].Value : headImgMatch.Groups[3].Value;
                var block = new MarkdownBlock
                {
                    Type = MarkdownBlockType.HeadlineImage,
                    ImageUrl = url,
                    Text = alt
                };
                block.LoadImages();
                blocks.Add(block);
                i++;
                continue;
            }

            // 4. Banner image (top hero banner)
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
                    bannerBlock.LoadImages();
                    blocks.Add(bannerBlock);
                    i++;
                    continue;
                }
            }

            // 5. Section headers: ###, ##, #
            if (line.StartsWith("### "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header3, Text = CleanText(line[4..]) });
                i++;
                continue;
            }
            if (line.StartsWith("## "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header2, Text = CleanText(line[3..]) });
                i++;
                continue;
            }
            if (line.StartsWith("# "))
            {
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.Header1, Text = CleanText(line[2..]) });
                i++;
                continue;
            }

            // 6. Markdown Table
            if (line.StartsWith('|') && line.Count(c => c == '|') >= 2)
            {
                var headers = line.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(s => CleanText(s.Trim())).Where(s => !string.IsNullOrEmpty(s)).ToList();
                i++;
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

            // 7. Bullets
            var bulletMatch = BulletRegex.Match(line);
            if (bulletMatch.Success)
            {
                var rawBullet = bulletMatch.Groups[1].Value;
                blocks.Add(new MarkdownBlock
                {
                    Type = MarkdownBlockType.Bullet,
                    Text = CleanText(rawBullet),
                    Spans = ParseSpans(rawBullet)
                });
                i++;
                continue;
            }

            // 8. Paragraph
            var cleaned = CleanText(line);
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                if (!cleaned.Equals("!") && !cleaned.StartsWith("<") && !cleaned.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Paragraph,
                        Text = cleaned,
                        Spans = ParseSpans(line)
                    });
                }
            }
            i++;
        }

        return blocks;
    }

    private static List<MarkdownSpan> ParseSpans(string line)
    {
        var spans = new List<MarkdownSpan>();
        if (string.IsNullOrWhiteSpace(line)) return spans;

        int lastIdx = 0;
        foreach (Match m in BoldRegex.Matches(line))
        {
            if (m.Index > lastIdx)
            {
                var normal = CleanText(line[lastIdx..m.Index]);
                if (!string.IsNullOrEmpty(normal)) spans.Add(new MarkdownSpan { Text = normal, IsBold = false });
            }
            var bold = CleanText(m.Groups[1].Value);
            if (!string.IsNullOrEmpty(bold)) spans.Add(new MarkdownSpan { Text = bold, IsBold = true });
            lastIdx = m.Index + m.Length;
        }

        if (lastIdx < line.Length)
        {
            var remaining = CleanText(line[lastIdx..]);
            if (!string.IsNullOrEmpty(remaining)) spans.Add(new MarkdownSpan { Text = remaining, IsBold = false });
        }

        return spans;
    }

    private static string CleanText(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1");
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
