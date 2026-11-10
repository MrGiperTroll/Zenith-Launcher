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
    ContentImage,
    Header1,
    Header2,
    Header3,
    Paragraph,
    Bullet,
    Table,
    Blockquote
}

public class MarkdownSpan
{
    public string Text { get; init; } = "";
    public bool IsBold { get; init; }
    public bool IsCode { get; init; }
    public string? LinkUrl { get; init; }
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
                FontWeight = s.IsBold ? FontWeight.SemiBold : FontWeight.Normal
            };
            if (Avalonia.Application.Current?.Resources.TryGetResource(s.IsBold ? "TextPrimaryBrush" : "TextSecondaryBrush", null, out var b) == true && b is IBrush resBrush)
            {
                run.Foreground = resBrush;
            }
            else
            {
                run.Foreground = s.IsBold ? new SolidColorBrush(Color.Parse("#E2E8F0")) : new SolidColorBrush(Color.Parse("#94A3B8"));
            }
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
    public int ColumnCount => Math.Max(Headers.Count > 0 ? Headers.Count : 1, Rows.Count > 0 ? Rows.Max(r => r.Cells.Count) : 1);
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
                        var old = IconBitmap;
                        IconBitmap = bmp;
                        try { old?.Dispose(); } catch { }
                        OnPropertyChanged(nameof(HasIconBitmap));
                    });
                }
            }
            catch { }
        });
    }

    public void Dispose()
    {
        try { IconBitmap?.Dispose(); } catch { }
        IconBitmap = null;
    }
}

public partial class MarkdownBlock : ObservableObject
{
    public MarkdownBlockType Type { get; init; }
    public string Text { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string LinkUrl { get; init; } = "";
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
    public bool IsContentImage => Type == MarkdownBlockType.ContentImage;
    public bool IsHeader1 => Type == MarkdownBlockType.Header1;
    public bool IsHeader2 => Type == MarkdownBlockType.Header2;
    public bool IsHeader3 => Type == MarkdownBlockType.Header3;
    public bool IsParagraph => Type == MarkdownBlockType.Paragraph;
    public bool IsBullet => Type == MarkdownBlockType.Bullet;
    public bool IsTable => Type == MarkdownBlockType.Table && Table != null;
    public bool IsBlockquote => Type == MarkdownBlockType.Blockquote;

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
                    var bmp = Bitmap.DecodeToWidth(ms, 720, BitmapInterpolationMode.MediumQuality);
                    Dispatcher.UIThread.Post(() =>
                    {
                        var old = ImageBitmap;
                        ImageBitmap = bmp;
                        try { old?.Dispose(); } catch { }
                        OnPropertyChanged(nameof(HasImageBitmap));
                    });
                }
            }
            catch { }
        });
    }

    public void Dispose()
    {
        try { ImageBitmap?.Dispose(); } catch { }
        ImageBitmap = null;
        if (SocialButtons != null)
        {
            foreach (var b in SocialButtons)
                b.Dispose();
        }
    }
}

public static class MarkdownBlockParser
{
    private static readonly Regex BannerRegex = new(@"\[?!\[(.*?)\]\((https?://\S+?\.(?:png|jpg|jpeg|gif|webp|svg))\)\]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadlineImgRegex = new(@"(?:<img\s+[^>]*src=[""']([^""']*(?:headline|header|title|features|about|download)[^""']*)[""'][^>]*alt=[""']([^""']*)[""']|!\[([^\]]*)\]\((https?://[^)]*(?:headline|header|title|features|about|download)[^)]*)\))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DividerImgRegex = new(@"(?:!\[Divider\]\((https?://[^\)]+)\)|<img\s+[^>]*(?:divider|line|bar)[^>]*src=[""']([^""']+)[""'])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SocialRowRegex = new(@"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>\s*<img\s+[^>]*src=[""']([^""']+)[""'][^>]*alt=[""']([^""']*)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownBadgeRegex = new(@"\[!\[(.*?)\]\((https?://[^\)]+)\)\]\((https?://[^\)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[\-\*\+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex InlineTokenRegex = new(@"(?:\[([^\]]+)\]\((https?:\/\/[^\)]+)\)|\*\*(.+?)\*\*|__(.+?)__|<b>(.*?)<\/b>|<strong>(.*?)<\/strong>|<code>(.*?)<\/code>)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<MarkdownBlock> Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new();

        // Normalize minified HTML by ensuring block tags are surrounded by newlines
        string processed = raw;
        if (processed.Contains('<') && processed.Contains('>'))
        {
            processed = Regex.Replace(processed, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            processed = Regex.Replace(processed, @"</?(?:p|h[1-6]|ul|ol|li|hr|table|tr|blockquote|div|center)[^>]*>", "\n$0\n", RegexOptions.IgnoreCase);
        }

                // 1. Resolve reference-style link definitions: [id]: url
        var linkDefinitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refDefRegex = new Regex(@"^\s*\[([^\]]+)\]:\s*(\S+)", RegexOptions.Multiline);
        foreach (Match m in refDefRegex.Matches(processed))
        {
            var key = m.Groups[1].Value.Trim();
            var url = m.Groups[2].Value.Trim();
            linkDefinitions[key] = url;
        }
        processed = refDefRegex.Replace(processed, "");

        // 2. Resolve image reference links: [![alt](img)][ref] -> [![alt](img)](url)
        processed = Regex.Replace(processed, @"\[!\[(.*?)\]\((https?:\/\/[^\)]+)\)\]\[([^\]]+)\]", m =>
        {
            var refKey = m.Groups[3].Value.Trim();
            if (linkDefinitions.TryGetValue(refKey, out var url))
                return $"[![" + m.Groups[1].Value + "](" + m.Groups[2].Value + ")](" + url + ")";
            return m.Value;
        });

        // 3. Resolve inline text reference links: [text][ref] -> [text](url)
        processed = Regex.Replace(processed, @"\[([^\]]+)\]\[([^\]]+)\]", m =>
        {
            var text = m.Groups[1].Value;
            var refKey = m.Groups[2].Value.Trim();
            if (linkDefinitions.TryGetValue(refKey, out var url))
                return "[" + text + "](" + url + ")";
            return "[" + text + "]";
        });

        var lines = processed.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
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

                        // 0. Blockquote: lines starting with >
            if (line.StartsWith('>'))
            {
                var quoteText = line.TrimStart('>', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(quoteText))
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Blockquote,
                        Text = CleanText(quoteText),
                        Spans = ParseSpans(quoteText)
                    });
                }
                i++;
                continue;
            }

            // 0b. Markdown badge links: collect consecutive badge lines [![...](...)](...)
            if (MarkdownBadgeRegex.IsMatch(line))
            {
                var buttons = new List<MarkdownSocialButton>();
                while (i < lines.Length && MarkdownBadgeRegex.IsMatch(lines[i].Trim()))
                {
                    var curLine = lines[i].Trim();
                    foreach (Match m in MarkdownBadgeRegex.Matches(curLine))
                    {
                        buttons.Add(new MarkdownSocialButton
                        {
                            Alt = m.Groups[1].Value,
                            ImageUrl = m.Groups[2].Value,
                            LinkUrl = m.Groups[3].Value
                        });
                    }
                    i++;
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
                continue;
            }

            // 1. Social / badge buttons row: <p align="center">...<a href="..."><img ...></a>...</p> OR Markdown badges
            if (line.Contains("<p", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("discord", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("github", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("curseforge", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("modrinth", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("shields.io", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("cf.way2muchnoise.eu", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("patreon", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("kofi", StringComparison.OrdinalIgnoreCase)))
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

            // 1b. Markdown badge links row: [![...](...)](...)
            var mdBadgeMatches = MarkdownBadgeRegex.Matches(line);
            if (mdBadgeMatches.Count > 1 || (mdBadgeMatches.Count == 1 && (line.Contains("shields.io", StringComparison.OrdinalIgnoreCase) || line.Contains("cf.way2muchnoise.eu", StringComparison.OrdinalIgnoreCase))))
            {
                var buttons = new List<MarkdownSocialButton>();
                foreach (Match m in mdBadgeMatches)
                {
                    buttons.Add(new MarkdownSocialButton
                    {
                        Alt = m.Groups[1].Value,
                        ImageUrl = m.Groups[2].Value,
                        LinkUrl = m.Groups[3].Value
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
                    i++;
                    continue;
                }
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

            // Plain divider: ---, ***, ___, <hr>, <hr/>
            if (line.StartsWith("---") || line.StartsWith("***") || line.StartsWith("___") ||
                line.Equals("<hr>", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("<hr/>", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("<hr />", StringComparison.OrdinalIgnoreCase))
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

            // 4. Markdown image or HTML img
            var bannerMatch = BannerRegex.Match(line);
            if (bannerMatch.Success)
            {
                var url = bannerMatch.Groups[2].Value;
                var alt = bannerMatch.Groups[1].Value;

                // Check if it's a shield/badge rather than full banner
                if (url.Contains("shields.io", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("cf.way2muchnoise.eu", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("badge", StringComparison.OrdinalIgnoreCase))
                {
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.SocialButtons,
                        SocialButtons = new List<MarkdownSocialButton>
                        {
                            new() { Alt = alt, ImageUrl = url, LinkUrl = "" }
                        }
                    };
                    block.LoadImages();
                    blocks.Add(block);
                }
                else if (!firstBannerFound && i < 4)
                {
                    firstBannerFound = true;
                    var bannerBlock = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.BannerImage,
                        ImageUrl = url,
                        Text = alt
                    };
                    bannerBlock.LoadImages();
                    blocks.Add(bannerBlock);
                }
                else
                {
                    var contentBlock = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.ContentImage,
                        ImageUrl = url,
                        Text = alt
                    };
                    contentBlock.LoadImages();
                    blocks.Add(contentBlock);
                }
                i++;
                continue;
            }

            var htmlImgMatch = Regex.Match(line, @"<img\s+[^>]*src=[""'](https?://[^""']+)[""'][^>]*>", RegexOptions.IgnoreCase);
            if (htmlImgMatch.Success)
            {
                var url = htmlImgMatch.Groups[1].Value;
                if (url.Contains("shields.io", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("badge", StringComparison.OrdinalIgnoreCase) ||
                    url.Contains("icon", StringComparison.OrdinalIgnoreCase))
                {
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.SocialButtons,
                        SocialButtons = new List<MarkdownSocialButton>
                        {
                            new() { Alt = "", ImageUrl = url, LinkUrl = "" }
                        }
                    };
                    block.LoadImages();
                    blocks.Add(block);
                }
                else if (!firstBannerFound && i < 4)
                {
                    firstBannerFound = true;
                    var bannerBlock = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.BannerImage,
                        ImageUrl = url
                    };
                    bannerBlock.LoadImages();
                    blocks.Add(bannerBlock);
                }
                else
                {
                    var contentBlock = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.ContentImage,
                        ImageUrl = url
                    };
                    contentBlock.LoadImages();
                    blocks.Add(contentBlock);
                }
                i++;
                continue;
            }

            // 5. HTML Table: <table ...> ... </table>
            if (line.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                var tableChunk = line;
                while (i < lines.Length && !lines[i].Contains("</table>", StringComparison.OrdinalIgnoreCase))
                {
                    i++;
                    if (i < lines.Length) tableChunk += " " + lines[i].Trim();
                }

                var headers = new List<string>();
                var rows = new List<MarkdownTableRow>();

                foreach (Match thMatch in Regex.Matches(tableChunk, @"<th[^>]*>(.*?)</th>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var cleanH = CleanText(thMatch.Groups[1].Value);
                    if (!string.IsNullOrEmpty(cleanH)) headers.Add(cleanH);
                }

                foreach (Match trMatch in Regex.Matches(tableChunk, @"<tr[^>]*>(.*?)</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    var trContent = trMatch.Groups[1].Value;
                    var cells = new List<string>();
                    foreach (Match tdMatch in Regex.Matches(trContent, @"<td[^>]*>(.*?)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                    {
                        cells.Add(CleanText(tdMatch.Groups[1].Value));
                    }
                    if (cells.Count > 0)
                    {
                        rows.Add(new MarkdownTableRow { Cells = cells });
                    }
                }

                if (headers.Count > 0 || rows.Count > 0)
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Table,
                        Table = new MarkdownTableData { Headers = headers, Rows = rows }
                    });
                }
                i++;
                continue;
            }

            // 6. Section headers: Markdown (###, ##, #) and HTML (<h1>, <h2>, <h3>...)
            var htmlHeaderMatch = Regex.Match(line, @"^<h([1-6])[^>]*>(.*?)(?:</h\1>)?$", RegexOptions.IgnoreCase);
            if (htmlHeaderMatch.Success)
            {
                int hLevel = int.Parse(htmlHeaderMatch.Groups[1].Value);
                string hText = CleanText(htmlHeaderMatch.Groups[2].Value);
                if (!string.IsNullOrWhiteSpace(hText))
                {
                    var hType = hLevel switch
                    {
                        1 => MarkdownBlockType.Header1,
                        2 => MarkdownBlockType.Header2,
                        _ => MarkdownBlockType.Header3
                    };
                    blocks.Add(new MarkdownBlock { Type = hType, Text = hText });
                }
                i++;
                continue;
            }

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

            // 7. Markdown Table (| Col 1 | Col 2 |)
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

            // 8. Bullets: Markdown (- / * / +) and HTML (<li>...</li>)
            var htmlLiMatch = Regex.Match(line, @"^<li[^>]*>(.*?)(?:</li>)?$", RegexOptions.IgnoreCase);
            if (htmlLiMatch.Success)
            {
                var rawLi = htmlLiMatch.Groups[1].Value;
                var cleanedLi = CleanText(rawLi);
                if (!string.IsNullOrWhiteSpace(cleanedLi))
                {
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Bullet,
                        Text = cleanedLi,
                        Spans = ParseSpans(rawLi)
                    });
                }
                i++;
                continue;
            }

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

            // 9. Paragraph
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
        foreach (Match m in InlineTokenRegex.Matches(line))
        {
            if (m.Index > lastIdx)
            {
                var normal = CleanText(line[lastIdx..m.Index]);
                if (!string.IsNullOrEmpty(normal)) spans.Add(new MarkdownSpan { Text = normal, IsBold = false });
            }

            if (m.Groups[1].Success) // Link: [Text](Url)
            {
                var linkText = CleanText(m.Groups[1].Value);
                var linkUrl = m.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(linkText))
                {
                    spans.Add(new MarkdownSpan { Text = linkText, LinkUrl = linkUrl, IsBold = false });
                }
            }
            else // Bold / Code
            {
                var bold = CleanText(m.Groups[3].Success ? m.Groups[3].Value :
                                     m.Groups[4].Success ? m.Groups[4].Value :
                                     m.Groups[5].Success ? m.Groups[5].Value :
                                     m.Groups[6].Success ? m.Groups[6].Value :
                                     m.Groups[7].Value);
                bool isCode = m.Groups[7].Success;
                if (!string.IsNullOrEmpty(bold)) spans.Add(new MarkdownSpan { Text = bold, IsBold = true, IsCode = isCode });
            }
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
        s = Regex.Replace(s, @"\[([^\]]+)\]\[[^\]]*\]", "$1");
        s = s.Replace("&nbsp;", " ")
             .Replace("&amp;", "&")
             .Replace("&lt;", "<")
             .Replace("&gt;", ">")
             .Replace("&quot;", "\"")
             .Replace("&#39;", "'")
             .Replace("&apos;", "'")
             .Replace("&#x27;", "'")
             .Replace("&bull;", "•")
             .Replace("&ndash;", "–")
             .Replace("&mdash;", "—")
             .Replace("&hellip;", "…")
             .Replace("&copy;", "©")
             .Replace("&reg;", "®")
             .Replace("&trade;", "™")
             .Replace(":white_check_mark:", "✓")
             .Replace(":x:", "✗");
        return s.Trim();
    }
}
