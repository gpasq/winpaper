using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Paperboy;

/// One Bing homepage image, from either Bing's live feed or the long-term archive.
public sealed class BingImage
{
    /// yyyyMMdd
    public string StartDate { get; }
    /// Stable short name, e.g. "SneffelsAutumn".
    public string Slug { get; }
    public string Title { get; }
    public string Caption { get; }
    public Uri ThumbnailUrl { get; }
    public Uri PreviewUrl { get; }
    public Uri FullUrl { get; }

    /// Same image on the same day gets the same id from either source; doubles as the local filename.
    public string Id => $"{StartDate}-{Slug}";

    public DateTime? Date =>
        DateTime.TryParseExact(StartDate, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    public BingImage(string startDate, string slug, string title, string caption,
                     Uri thumbnailUrl, Uri previewUrl, Uri fullUrl)
    {
        StartDate = startDate;
        Slug = slug;
        Title = title;
        Caption = caption;
        ThumbnailUrl = thumbnailUrl;
        PreviewUrl = previewUrl;
        FullUrl = fullUrl;
    }

    /// `bingBase` is a Bing image path without the size suffix, e.g. "/th?id=OHR.SneffelsAutumn_EN-US3953128777".
    public static BingImage FromBingBase(string startDate, string title, string caption, string bingBase)
    {
        var i = bingBase.LastIndexOf("OHR.", StringComparison.Ordinal);
        var name = i >= 0 ? bingBase[(i + 4)..] : bingBase;
        return new BingImage(
            startDate,
            name.Split('_')[0],
            title,
            caption,
            new Uri($"https://www.bing.com{bingBase}_400x240.jpg"),
            new Uri($"https://www.bing.com{bingBase}_1920x1080.jpg"),
            new Uri($"https://www.bing.com{bingBase}_UHD.jpg"));
    }
}

static class Net
{
    public static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Paperboy/1.0 (Windows)");
        return c;
    }

    /// "en-US"-style market from the user's regional settings.
    public static string BingMarket
    {
        get
        {
            try
            {
                var culture = CultureInfo.CurrentCulture;
                if (culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name)) return "en-US";
                var region = new RegionInfo(culture.Name);
                return $"{culture.TwoLetterISOLanguageName}-{region.TwoLetterISORegionName}";
            }
            catch
            {
                return "en-US";
            }
        }
    }
}

public static class BingService
{
    /// Bing exposes roughly the last 15 days: idx 0–7, at most 8 per request.
    public static async Task<List<BingImage>> FetchRecentAsync(string? market = null)
    {
        market ??= Net.BingMarket;
        var a = FetchAsync(0, 8, market);
        var b = FetchAsync(7, 8, market);
        var all = (await a).Concat(await b);
        var seen = new HashSet<string>();
        return all.Where(i => seen.Add(i.Id))
                  .OrderByDescending(i => i.StartDate, StringComparer.Ordinal)
                  .ToList();
    }

    static async Task<List<BingImage>> FetchAsync(int idx, int n, string market)
    {
        var url = $"https://www.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n={n}&mkt={Uri.EscapeDataString(market)}";
        var json = await Net.Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var result = new List<BingImage>();
        foreach (var e in doc.RootElement.GetProperty("images").EnumerateArray())
        {
            var copyright = e.GetProperty("copyright").GetString() ?? "";
            // Caption without the trailing "(© credit)".
            var cut = copyright.IndexOf(" (©", StringComparison.Ordinal);
            var caption = cut >= 0 ? copyright[..cut] : copyright;
            result.Add(BingImage.FromBingBase(
                e.GetProperty("startdate").GetString() ?? "",
                e.GetProperty("title").GetString() ?? "",
                caption,
                e.GetProperty("urlbase").GetString() ?? ""));
        }
        return result;
    }
}

/// Long-term archive (2010–today) from the community-run Bing Wallpaper Archive,
/// https://github.com/npanuhin/Bing-Wallpaper-Archive — Bing itself only serves ~15 days.
public static class ArchiveService
{
    public const int FirstYear = 2010;

    sealed class NotFoundException : Exception { }

    /// All images for one year, newest first.
    public static async Task<List<BingImage>> FetchYearAsync(int year, string? market = null)
    {
        var path = ArchivePath(market ?? Net.BingMarket);
        try
        {
            return await FetchPathAsync(year, path);
        }
        catch (NotFoundException) when (path != "US/en")
        {
            // Not every market is archived; fall back to the US feed.
            return await FetchPathAsync(year, "US/en");
        }
    }

    static async Task<List<BingImage>> FetchPathAsync(int year, string path)
    {
        using var response = await Net.Http.GetAsync($"https://bing.npanuhin.me/{path}.{year}.json");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new NotFoundException();
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"The server returned {(int)response.StatusCode}.");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray()
            .Select(FromEntry)
            .Where(i => i != null)
            .Select(i => i!)
            .OrderByDescending(i => i.StartDate, StringComparer.Ordinal)
            .ToList();
    }

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static BingImage? FromEntry(JsonElement e)
    {
        var date = Str(e, "date");
        if (date == null) return null;
        var startDate = date.Replace("-", "");
        var description = Str(e, "title") ?? "";
        // The archive's `title` is Bing's long description; `caption`/`subtitle` is the short headline.
        var title = Str(e, "caption") ?? Str(e, "subtitle") ?? description;
        var caption = title == description ? "" : description;

        var bing = Str(e, "bing_url");
        var r = bing?.IndexOf("/th?id=", StringComparison.Ordinal) ?? -1;
        if (bing != null && r >= 0)
        {
            var bingBase = bing[r..].Replace("_UHD.jpg", "");
            return BingImage.FromBingBase(startDate, title, caption, bingBase);
        }
        // Older entries (pre-2019 or so) only exist on the archive's own host, at a single size.
        if (!Uri.TryCreate(Str(e, "url"), UriKind.Absolute, out var url)) return null;
        var words = SplitWords(description).Take(3)
            .Select(w => w[..1].ToUpper(CultureInfo.CurrentCulture) + w[1..]);
        var slug = string.Concat(words);
        return new BingImage(startDate, slug.Length == 0 ? "Archive" : slug, title, caption, url, url, url);
    }

    static IEnumerable<string> SplitWords(string s)
    {
        var start = -1;
        for (var i = 0; i <= s.Length; i++)
        {
            var isWord = i < s.Length && char.IsLetterOrDigit(s[i]);
            if (isWord && start < 0) start = i;
            else if (!isWord && start >= 0)
            {
                yield return s[start..i];
                start = -1;
            }
        }
    }

    /// "en-US" → "US/en", the archive's directory layout.
    static string ArchivePath(string market)
    {
        var parts = market.Split('-');
        if (parts.Length != 2) return "US/en";
        return $"{parts[1].ToUpperInvariant()}/{parts[0].ToLowerInvariant()}";
    }
}
