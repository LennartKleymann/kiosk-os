using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

public record KioskOsRelease(
    string TagName, string Name, string DownloadUrl, long Size, string? ChecksumUrl = null);

/// <summary>
/// Finds kiosk-os releases and downloads ISO assets with progress reporting.
///
/// Releases are read from the repository's Atom feed rather than the GitHub
/// REST API: the API allows only 60 anonymous requests per hour and IP, which
/// a school or office behind one shared address exhausts quickly — the wizard
/// then fails with "403 rate limit exceeded". The feed is an ordinary web page
/// without that limit. It does not list assets, so the ISO is located by the
/// file name the release workflow always uses and checked with a HEAD request.
/// </summary>
public class GithubReleaseService
{
    private const string Repository = "https://github.com/LennartKleymann/kiosk-os";
    public const string FeedUrl = Repository + "/releases.atom";
    private const string IsoName = "kiosk-os.iso";

    private static readonly HttpClient DefaultClient = CreateClient();
    private readonly HttpClient Http;

    public GithubReleaseService(HttpClient? http = null) => Http = http ?? DefaultClient;

    private static HttpClient CreateClient()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.Add("User-Agent", "KioskOsWizard");
        return h;
    }

    /// <summary>
    /// Newest stable release that has an ISO, or the newest pre-release when
    /// there is no stable one yet. A release whose ISO is not uploaded (yet) —
    /// e.g. while its build is still running — is skipped.
    /// </summary>
    public async Task<KioskOsRelease?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        var feed = await Http.GetStringAsync(FeedUrl, ct);

        KioskOsRelease? newestPrerelease = null;
        foreach (var (tag, title) in ParseFeed(feed))   // newest first
        {
            var prerelease = IsPrerelease(tag);
            if (prerelease && newestPrerelease is not null) continue;

            var release = await ProbeAsync(tag, title, ct);
            if (release is null) continue;
            if (!prerelease) return release;
            newestPrerelease = release;
        }
        return newestPrerelease;
    }

    /// <summary>Tag and title of each release in the feed, in feed order (newest first).</summary>
    public static IReadOnlyList<(string Tag, string Title)> ParseFeed(string atom)
    {
        XNamespace ns = "http://www.w3.org/2005/Atom";
        var result = new List<(string, string)>();
        foreach (var entry in XDocument.Parse(atom).Root?.Elements(ns + "entry") ?? [])
        {
            // <link rel="alternate" href="https://github.com/<owner>/<repo>/releases/tag/<tag>"/>
            var href = entry.Elements(ns + "link").Select(l => (string?)l.Attribute("href")).FirstOrDefault(h => h is not null);
            const string marker = "/releases/tag/";
            var i = href?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (href is null || i < 0) continue;
            var tag = Uri.UnescapeDataString(href[(i + marker.Length)..]);
            var title = (string?)entry.Element(ns + "title") ?? tag;
            result.Add((tag, title.Trim()));
        }
        return result;
    }

    /// <summary>Same rule as the release workflow: a tag with a suffix (v1.0.0-rc1) is a pre-release.</summary>
    public static bool IsPrerelease(string tag) => tag.Contains('-');

    public static string AssetUrl(string tag, string file) =>
        $"{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{file}";

    /// <summary>The release if its ISO exists, with the size from the download's headers.</summary>
    private async Task<KioskOsRelease?> ProbeAsync(string tag, string title, CancellationToken ct)
    {
        var isoUrl = AssetUrl(tag, IsoName);
        try
        {
            using var head = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, isoUrl), ct);
            if (!head.IsSuccessStatusCode) return null;
            var size = head.Content.Headers.ContentLength ?? 0;
            return new KioskOsRelease(tag, title, isoUrl, size, AssetUrl(tag, IsoName + ".sha256"));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the ISO unless a verified copy is already cached. The image
    /// is well over a gigabyte, so re-downloading it for every stick is not
    /// reasonable.
    /// </summary>
    public async Task<string> GetOrDownloadAsync(
        KioskOsRelease release,
        Action<long, long> progress,
        CancellationToken ct = default)
    {
        var cached = Path.Combine(CacheDirectory, $"kiosk-os-{release.TagName}.iso");
        Directory.CreateDirectory(CacheDirectory);

        var expected = await GetExpectedChecksumAsync(release, ct);

        if (File.Exists(cached) && await IsIntactAsync(cached, release, expected, ct))
            return cached;

        var partial = cached + ".part";
        await DownloadAsync(release, partial, progress, ct);

        if (!await IsIntactAsync(partial, release, expected, ct))
        {
            File.Delete(partial);
            throw new IOException(
                "The downloaded image did not match its published checksum. " +
                "The download may have been corrupted or tampered with.");
        }

        File.Move(partial, cached, overwrite: true);
        return cached;
    }

    public static string CacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kiosk-os", "images");

    private async Task<string?> GetExpectedChecksumAsync(KioskOsRelease release, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(release.ChecksumUrl)) return null;

        try
        {
            // Format is "<hash>  <filename>", as produced by sha256sum.
            var body = await Http.GetStringAsync(release.ChecksumUrl, ct);
            var hash = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return hash is { Length: 64 } ? hash.ToLowerInvariant() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static async Task<bool> IsIntactAsync(
        string path, KioskOsRelease release, string? expectedHash, CancellationToken ct)
    {
        if (expectedHash is null)
            return release.Size <= 0 || new FileInfo(path).Length == release.Size;

        await using var stream = File.OpenRead(path);
        var actual = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(actual).ToLowerInvariant() == expectedHash;
    }

    public async Task DownloadAsync(
        KioskOsRelease release,
        string destinationPath,
        Action<long, long> progress,
        CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? release.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[1024 * 1024]; // 1 MB
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            progress(written, total);
        }
    }
}
