using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ZenithLauncher.Services;

namespace ZenithLauncher.Common.Utils;

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
    public bool IsItalic { get; init; }
    public bool IsCode { get; init; }
    public string? LinkUrl { get; init; }
}

public class MarkdownParagraphControl : TextBlock
{
    private sealed record LinkRange(int Start, int Length, string Url);
    private readonly List<LinkRange> _linkRanges = new();
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    public static readonly StyledProperty<List<MarkdownSpan>?> SpansProperty =
        AvaloniaProperty.Register<MarkdownParagraphControl, List<MarkdownSpan>?>(nameof(Spans));

    public List<MarkdownSpan>? Spans
    {
        get => GetValue(SpansProperty);
        set => SetValue(SpansProperty, value);
    }

    static MarkdownParagraphControl()
    {
        SpansProperty.Changed.AddClassHandler<MarkdownParagraphControl>((x, _) => x.UpdateInlines());
        TextProperty.Changed.AddClassHandler<MarkdownParagraphControl>((x, _) => { if (x.Spans is { Count: > 0 }) x.UpdateInlines(); });
    }

    private void UpdateInlines()
    {
        Inlines?.Clear();
        _linkRanges.Clear();

        if (Spans == null || Spans.Count == 0)
        {
            if (!string.IsNullOrEmpty(Text))
            {
                Inlines?.Add(new Run(Text));
            }
            return;
        }

        int pos = 0;
        foreach (var s in Spans)
        {
            var run = new Run(s.Text)
            {
                FontWeight = s.IsBold ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle = s.IsItalic ? FontStyle.Italic : FontStyle.Normal
            };

            if (s.IsCode)
            {
                run.FontFamily = new FontFamily("Consolas, Cascadia Code, monospace");
            }

            if (!string.IsNullOrWhiteSpace(s.LinkUrl))
            {
                if (Application.Current?.Resources.TryGetResource("AccentBrush", null, out var ab) == true && ab is IBrush linkBrush)
                    run.Foreground = linkBrush;
                else
                    run.Foreground = new SolidColorBrush(Color.Parse("#60A5FA"));

                run.TextDecorations = Avalonia.Media.TextDecorations.Underline;
                _linkRanges.Add(new LinkRange(pos, s.Text.Length, s.LinkUrl!));
            }
            else if (Application.Current?.Resources.TryGetResource(s.IsBold ? "TextPrimaryBrush" : "TextSecondaryBrush", null, out var b) == true && b is IBrush resBrush)
            {
                run.Foreground = resBrush;
            }
            else
            {
                run.Foreground = s.IsBold ? new SolidColorBrush(Color.Parse("#E2E8F0")) : new SolidColorBrush(Color.Parse("#94A3B8"));
            }

            Inlines?.Add(run);
            pos += s.Text.Length;
        }
    }

    private string? HitTestLink(Point point)
    {
        if (_linkRanges.Count == 0 || TextLayout == null) return null;
        var hit = TextLayout.HitTestPoint(point);
        if (!hit.IsInside) return null;
        int idx = hit.TextPosition;
        foreach (var range in _linkRanges)
        {
            if (idx >= range.Start && idx < range.Start + range.Length)
                return range.Url;
        }
        return null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_linkRanges.Count == 0) return;
        Cursor = HitTestLink(e.GetPosition(this)) != null ? HandCursor : Cursor.Default;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_linkRanges.Count > 0)
            Cursor = Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_linkRanges.Count == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var url = HitTestLink(e.GetPosition(this));
        if (!string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                e.Handled = true;
            }
            catch { }
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

public partial class MarkdownSocialButton : ObservableObject, IDisposable
{
    public string LinkUrl { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string Alt { get; init; } = "";

    private volatile bool _disposed;

    [ObservableProperty]
    private Bitmap? _iconBitmap;

    public bool HasIconBitmap => IconBitmap != null;

    public void LoadIcon()
    {
        if (_disposed || string.IsNullOrWhiteSpace(ImageUrl) || IconBitmap != null) return;
        Task.Run(async () =>
        {
            try
            {
                var bytes = await ModrinthApiService.GetIconAsync(ImageUrl);
                if (_disposed || bytes is not { Length: > 0 }) return;
                using var ms = new MemoryStream(bytes);
                var bmp = Bitmap.DecodeToWidth(ms, 96, BitmapInterpolationMode.MediumQuality);
                if (_disposed)
                {
                    try { bmp.Dispose(); } catch { }
                    return;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed)
                    {
                        try { bmp.Dispose(); } catch { }
                        return;
                    }
                    var old = IconBitmap;
                    IconBitmap = bmp;
                    try { old?.Dispose(); } catch { }
                    OnPropertyChanged(nameof(HasIconBitmap));
                });
            }
            catch { }
        });
    }

    public void Dispose()
    {
        _disposed = true;
        try { IconBitmap?.Dispose(); } catch { }
        IconBitmap = null;
    }
}

public partial class MarkdownBlock : ObservableObject, IDisposable
{
    public MarkdownBlockType Type { get; init; }
    public string Text { get; init; } = "";
    public string ImageUrl { get; init; } = "";
    public string LinkUrl { get; init; } = "";
    public MarkdownTableData? Table { get; init; }
    public List<MarkdownSpan> Spans { get; init; } = new();
    public List<MarkdownSocialButton> SocialButtons { get; init; } = new();

    private volatile bool _disposed;

    [ObservableProperty]
    private Bitmap? _imageBitmap;

    public bool HasImageBitmap => ImageBitmap != null;

    public bool IsBannerImage => Type == MarkdownBlockType.BannerImage;
    public bool IsSocialButtons => Type == MarkdownBlockType.SocialButtons && SocialButtons.Count > 0;
    public bool IsDividerImage => Type == MarkdownBlockType.DividerImage;
    public bool IsDividerLine => Type == MarkdownBlockType.DividerLine;
    public bool IsHeadlineImage => Type == MarkdownBlockType.HeadlineImage;
    public bool IsContentImage => Type == MarkdownBlockType.ContentImage;
    public bool HasLinkUrl => !string.IsNullOrWhiteSpace(LinkUrl);
    public bool IsHeader1 => Type == MarkdownBlockType.Header1;
    public bool IsHeader2 => Type == MarkdownBlockType.Header2;
    public bool IsHeader3 => Type == MarkdownBlockType.Header3;
    public bool IsParagraph => Type == MarkdownBlockType.Paragraph;
    public bool IsBullet => Type == MarkdownBlockType.Bullet;
    public bool IsTable => Type == MarkdownBlockType.Table && Table != null;
    public bool IsBlockquote => Type == MarkdownBlockType.Blockquote;

    public void LoadImages()
    {
        if (_disposed) return;
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
                if (_disposed || bytes is not { Length: > 0 }) return;
                using var ms = new MemoryStream(bytes);
                Bitmap bmp;
                using (var codec = SkiaSharp.SKCodec.Create(ms))
                {
                    ms.Position = 0;
                    if (codec != null && codec.Info.Width > 720)
                        bmp = Bitmap.DecodeToWidth(ms, 720, BitmapInterpolationMode.MediumQuality);
                    else
                        bmp = new Bitmap(ms);
                }
                if (_disposed)
                {
                    try { bmp.Dispose(); } catch { }
                    return;
                }
                Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed)
                    {
                        try { bmp.Dispose(); } catch { }
                        return;
                    }
                    var old = ImageBitmap;
                    ImageBitmap = bmp;
                    try { old?.Dispose(); } catch { }
                    OnPropertyChanged(nameof(HasImageBitmap));
                });
            }
            catch { }
        });
    }

    public void Dispose()
    {
        _disposed = true;
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
    private static readonly Regex BannerRegex = new(@"\[!\[(.*?)\]\((https?://[^\s\)]+)\)\]\((https?://[^\s\)]+)\)|!\[(.*?)\]\((https?://[^\s\)]+)\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HeadlineImgRegex = new(@"(?:<img\s+[^>]*src=[""']([^""']*(?:headline|header|title|features|about|download)[^""']*)[""'][^>]*alt=[""']([^""']*)[""']|!\[([^\]]*)\]\((https?://[^)]*(?:headline|header|title|features|about|download)[^)]*)\))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DividerImgRegex = new(@"(?:!\[Divider\]\((https?://[^\)]+)\)|<img\s+[^>]*(?:divider|line|bar)[^>]*src=[""']([^""']+)[""'])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SocialRowRegex = new(@"<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>\s*<img\s+[^>]*src=[""']([^""']+)[""'](?:[^>]*alt=[""']([^""']*)[""'])?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MarkdownBadgeRegex = new(@"\[!\[(.*?)\]\((https?://[^\)]+)\)\]\((https?://[^\)]+)\)", RegexOptions.Compiled);
    private static readonly Regex BulletRegex = new(@"^[\-\*\+]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex InlineTokenRegex = new(
        @"(?:<a\s+[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)<\/a>|\[([^\]]+)\]\((https?:\/\/[^\)\s]+)\)|\*\*(.+?)\*\*|__(.+?)__|<b>(.*?)<\/b>|<strong>(.*?)<\/strong>|\*([^\*
]+)\*|_([^_
]+)_|<i>(.*?)<\/i>|<em>(.*?)<\/em>|`([^`
]+)`|<code>(.*?)<\/code>)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsBadgeOrShield(string? imgUrl, string? alt, string? linkUrl)
    {
        var img = imgUrl ?? "";
        var a = (alt ?? "").Trim();
        var link = linkUrl ?? "";

        if (img.Contains("shields.io", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("cf.way2muchnoise.eu", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("badgen.net", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("img.shields", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("badge", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("button", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("modrinth.com/assets", StringComparison.OrdinalIgnoreCase))
            return true;

        if (img.Contains("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
            link.Contains("youtu.be", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("imgur.com", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("screenshot", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("banner", StringComparison.OrdinalIgnoreCase) ||
            img.Contains("preview", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(link) && img.Length > 0 && a.Length > 0 && a.Length <= 28)
            return true;

        return false;
    }

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

            // 0b. Markdown badge links: group consecutive badge lines (allowing single blank separators between badges)
            var badgeProbeMatches = MarkdownBadgeRegex.Matches(line);
            if (badgeProbeMatches.Count > 1 ||
                (badgeProbeMatches.Count == 1 && IsBadgeOrShield(badgeProbeMatches[0].Groups[2].Value, badgeProbeMatches[0].Groups[1].Value, badgeProbeMatches[0].Groups[3].Value)))
            {
                var buttons = new List<MarkdownSocialButton>();
                while (i < lines.Length)
                {
                    var curLine = lines[i].Trim();
                    if (string.IsNullOrWhiteSpace(curLine))
                    {
                        if (i + 1 < lines.Length)
                        {
                            var nextLine = lines[i + 1].Trim();
                            var nextMs = MarkdownBadgeRegex.Matches(nextLine);
                            if (nextMs.Count > 1 || (nextMs.Count == 1 && IsBadgeOrShield(nextMs[0].Groups[2].Value, nextMs[0].Groups[1].Value, nextMs[0].Groups[3].Value)))
                            {
                                i++;
                                continue;
                            }
                        }
                        break;
                    }
                    var ms = MarkdownBadgeRegex.Matches(curLine);
                    if (ms.Count == 0) break;
                    if (ms.Count == 1 && !IsBadgeOrShield(ms[0].Groups[2].Value, ms[0].Groups[1].Value, ms[0].Groups[3].Value)) break;
                    foreach (Match m in ms)
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
                    continue;
                }
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

            // 4. Markdown image (optionally linked: [![alt](img)](url)) or HTML img
            var bannerMatch = BannerRegex.Match(line);
            if (bannerMatch.Success)
            {
                var isLinkedImg = bannerMatch.Groups[2].Success;
                var alt = isLinkedImg ? bannerMatch.Groups[1].Value : bannerMatch.Groups[4].Value;
                var url = isLinkedImg ? bannerMatch.Groups[2].Value : bannerMatch.Groups[5].Value;
                var linkUrl = isLinkedImg ? bannerMatch.Groups[3].Value : "";

                if (IsBadgeOrShield(url, alt, linkUrl))
                {
                    var block = new MarkdownBlock
                    {
                        Type = MarkdownBlockType.SocialButtons,
                        SocialButtons = new List<MarkdownSocialButton>
                        {
                            new() { Alt = alt, ImageUrl = url, LinkUrl = linkUrl }
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
                        LinkUrl = linkUrl,
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
                        LinkUrl = linkUrl,
                        Text = alt
                    };
                    contentBlock.LoadImages();
                    blocks.Add(contentBlock);
                }
                i++;
                continue;
            }

            var htmlLinkedMatch = SocialRowRegex.Match(line);
            if (htmlLinkedMatch.Success)
            {
                var href = htmlLinkedMatch.Groups[1].Value;
                var src = htmlLinkedMatch.Groups[2].Value;
                var alt = htmlLinkedMatch.Groups[3].Success ? htmlLinkedMatch.Groups[3].Value : "";
                if (IsBadgeOrShield(src, alt, href) || SocialRowRegex.Matches(line).Count > 1)
                {
                    var buttons = new List<MarkdownSocialButton>();
                    foreach (Match m in SocialRowRegex.Matches(line))
                    {
                        buttons.Add(new MarkdownSocialButton
                        {
                            LinkUrl = m.Groups[1].Value,
                            ImageUrl = m.Groups[2].Value,
                            Alt = m.Groups[3].Success ? m.Groups[3].Value : ""
                        });
                    }
                    var block = new MarkdownBlock { Type = MarkdownBlockType.SocialButtons, SocialButtons = buttons };
                    block.LoadImages();
                    blocks.Add(block);
                }
                else
                {
                    var contentBlock = new MarkdownBlock
                    {
                        Type = (!firstBannerFound && i < 4) ? MarkdownBlockType.BannerImage : MarkdownBlockType.ContentImage,
                        ImageUrl = src,
                        LinkUrl = href,
                        Text = alt
                    };
                    if (contentBlock.Type == MarkdownBlockType.BannerImage) firstBannerFound = true;
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
                if (IsBadgeOrShield(url, "", ""))
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
                var normal = CleanTextPreservePadding(line[lastIdx..m.Index]);
                if (!string.IsNullOrEmpty(normal)) spans.Add(new MarkdownSpan { Text = normal });
            }

            if (m.Groups[1].Success) // HTML link: <a href="url">text</a>
            {
                var linkUrl = m.Groups[1].Value.Trim();
                var linkText = CleanText(m.Groups[2].Value);
                if (!string.IsNullOrEmpty(linkText))
                    spans.Add(new MarkdownSpan { Text = linkText, LinkUrl = linkUrl });
            }
            else if (m.Groups[3].Success) // Markdown link: [Text](Url)
            {
                var linkText = CleanText(m.Groups[3].Value);
                var linkUrl = m.Groups[4].Value.Trim();
                if (!string.IsNullOrEmpty(linkText))
                    spans.Add(new MarkdownSpan { Text = linkText, LinkUrl = linkUrl });
            }
            else if (m.Groups[5].Success || m.Groups[6].Success || m.Groups[7].Success || m.Groups[8].Success)
            {
                var bold = CleanText(m.Groups[5].Success ? m.Groups[5].Value :
                                     m.Groups[6].Success ? m.Groups[6].Value :
                                     m.Groups[7].Success ? m.Groups[7].Value :
                                     m.Groups[8].Value);
                if (!string.IsNullOrEmpty(bold))
                    spans.Add(new MarkdownSpan { Text = bold, IsBold = true });
            }
            else if (m.Groups[9].Success || m.Groups[10].Success || m.Groups[11].Success || m.Groups[12].Success)
            {
                var innerRaw = m.Groups[9].Success ? m.Groups[9].Value :
                               m.Groups[10].Success ? m.Groups[10].Value :
                               m.Groups[11].Success ? m.Groups[11].Value :
                               m.Groups[12].Value;
                if (innerRaw.Contains("](") || innerRaw.Contains("<a ", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var nested in ParseSpans(innerRaw))
                    {
                        spans.Add(new MarkdownSpan
                        {
                            Text = nested.Text,
                            IsBold = nested.IsBold,
                            IsItalic = true,
                            IsCode = nested.IsCode,
                            LinkUrl = nested.LinkUrl
                        });
                    }
                }
                else
                {
                    var italicText = CleanText(innerRaw);
                    if (!string.IsNullOrEmpty(italicText))
                        spans.Add(new MarkdownSpan { Text = italicText, IsItalic = true });
                }
            }
            else
            {
                var codeText = CleanText(m.Groups[13].Success ? m.Groups[13].Value : m.Groups[14].Value);
                if (!string.IsNullOrEmpty(codeText))
                    spans.Add(new MarkdownSpan { Text = codeText, IsBold = true, IsCode = true });
            }
            lastIdx = m.Index + m.Length;
        }

        if (lastIdx < line.Length)
        {
            var remaining = CleanTextPreservePadding(line[lastIdx..]);
            if (!string.IsNullOrEmpty(remaining)) spans.Add(new MarkdownSpan { Text = remaining });
        }

        return spans;
    }

    private static string CleanTextPreservePadding(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var lead = s.Length > 0 && char.IsWhiteSpace(s[0]) ? " " : "";
        var trail = s.Length > 1 && char.IsWhiteSpace(s[^1]) ? " " : "";
        var core = CleanText(s);
        if (string.IsNullOrEmpty(core))
            return string.IsNullOrWhiteSpace(s) ? " " : "";
        return lead + core + trail;
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
