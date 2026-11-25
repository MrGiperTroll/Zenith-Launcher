using System;
using System.Collections.Generic;
using System.Linq;
using ZenithLauncher.Models;
using ZenithLauncher.ViewModels.Items;

namespace ZenithLauncher.Services;

/// <summary>
/// Strictly bounded in-memory cache storing lightweight metadata for only the first
/// visible page (up to 14 items per category, zero bitmaps or full details) so
/// switching catalog category tabs displays content immediately during horizontal swipes.
/// </summary>
public static class CatalogFirstPageCache
{
    public const int MaxCardsPerPage = 14;
    private const int MaxEntries = 8;
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);

    public sealed class CachedCard
    {
        public ContentSource Source { get; init; }
        public string ProjectId { get; init; } = "";
        public string Slug { get; init; } = "";
        public string Title { get; init; } = "";
        public string Author { get; init; } = "";
        public string Description { get; init; } = "";
        public long Downloads { get; init; }
        public long Follows { get; init; }
        public string IconUrl { get; init; } = "";
        public string[] GameVersions { get; init; } = Array.Empty<string>();
        public string[] Categories { get; init; } = Array.Empty<string>();
        public string[] Loaders { get; init; } = Array.Empty<string>();
        public DateTime Updated { get; init; }
        public string ProjectType { get; init; } = "mod";
    }

    public sealed class Entry
    {
        public IReadOnlyList<CachedCard> Cards { get; }
        public int NextOffset { get; }
        public int TotalHits { get; }
        public string StatusText { get; }
        public DateTimeOffset CreatedUtc { get; }

        public Entry(
            IReadOnlyList<CachedCard> cards,
            int nextOffset,
            int totalHits,
            string statusText,
            DateTimeOffset createdUtc)
        {
            Cards = cards;
            NextOffset = nextOffset;
            TotalHits = totalHits;
            StatusText = statusText;
            CreatedUtc = createdUtc;
        }
    }

    private static readonly object SyncLock = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> LruOrder = new();

    public static bool TryGet(string key, out Entry? entry)
    {
        lock (SyncLock)
        {
            if (Entries.TryGetValue(key, out var found))
            {
                if (DateTimeOffset.UtcNow - found.CreatedUtc <= EntryTtl)
                {
                    LruOrder.Remove(key);
                    LruOrder.Add(key);
                    entry = found;
                    return true;
                }

                Entries.Remove(key);
                LruOrder.Remove(key);
            }
        }

        entry = null;
        return false;
    }

    public static void Set(string key, IReadOnlyList<ModrinthProjectItemViewModel> sourceItems, int totalHits, string statusText)
    {
        if (string.IsNullOrWhiteSpace(key) || sourceItems.Count == 0)
            return;

        int takeCount = Math.Min(MaxCardsPerPage, sourceItems.Count);
        var cards = new List<CachedCard>(takeCount);
        for (int i = 0; i < takeCount; i++)
        {
            var src = sourceItems[i];
            cards.Add(new CachedCard
            {
                Source = src.Source,
                ProjectId = src.ProjectId,
                Slug = src.Slug,
                Title = src.Title,
                Author = src.Author,
                Description = src.Description,
                Downloads = src.Downloads,
                Follows = src.Follows,
                IconUrl = src.IconUrl,
                GameVersions = src.GameVersions,
                Categories = src.Categories,
                Loaders = src.Loaders,
                Updated = src.Updated,
                ProjectType = src.ProjectType
            });
        }

        var entry = new Entry(cards, cards.Count, totalHits, statusText ?? "", DateTimeOffset.UtcNow);
        lock (SyncLock)
        {
            Entries[key] = entry;
            LruOrder.Remove(key);
            LruOrder.Add(key);

            while (LruOrder.Count > MaxEntries)
            {
                var oldest = LruOrder[0];
                LruOrder.RemoveAt(0);
                Entries.Remove(oldest);
            }
        }
    }

    public static List<ModrinthProjectItemViewModel> MaterializeCards(Entry entry)
    {
        var list = new List<ModrinthProjectItemViewModel>(entry.Cards.Count);
        foreach (var c in entry.Cards)
        {
            list.Add(new ModrinthProjectItemViewModel
            {
                Source = c.Source,
                ProjectId = c.ProjectId,
                Slug = c.Slug,
                Title = c.Title,
                Author = c.Author,
                Description = c.Description,
                Downloads = c.Downloads,
                Follows = c.Follows,
                IconUrl = c.IconUrl,
                GameVersions = c.GameVersions,
                Categories = c.Categories,
                Loaders = c.Loaders,
                Updated = c.Updated,
                ProjectType = c.ProjectType
            });
        }
        return list;
    }

    public static void Clear()
    {
        lock (SyncLock)
        {
            Entries.Clear();
            LruOrder.Clear();
        }
    }
}
