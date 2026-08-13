using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

public record KioskOsRelease(
    string TagName, string Name, string DownloadUrl, long Size, string? ChecksumUrl = null);

/// <summary>
/// Fetches kiosk-os releases from the GitHub Releases API and downloads
/// ISO assets with progress reporting.
/// </summary>
public class GithubReleaseService
{
    private const string ApiUrl = "https://api.github.com/repos/LennartKleymann/kiosk-os/releases";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient();
        h.DefaultRequestHeaders.Add("User-Agent", "KioskOsWizard");
        h.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        return h;
    }

    public async Task<KioskOsRelease?> GetLatestReleaseAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"{ApiUrl}/latest", ct);
        using var doc = JsonDocument.Parse(json);
        return ParseRelease(doc.RootElement);
    }

    public async Task<IReadOnlyList<KioskOsRelease>> GetAllReleasesAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync(ApiUrl, ct);
        var releases = new List<KioskOsRelease>();
        using var doc = JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var r = ParseRelease(e);
            if (r != null) releases.Add(r);
        }
        return releases;
    }

    private static KioskOsRelease? ParseRelease(JsonElement e)
    {
        var tag = e.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        var name = e.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag;

        if (!e.TryGetProperty("assets", out var assets)) return null;

        string? isoUrl = null, checksumUrl = null;
        long isoSize = 0;

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";

            if (assetName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                checksumUrl = url;
            else if (assetName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) && isoUrl is null)
            {
                isoUrl = url;
                isoSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            }
        }

        return isoUrl is null ? null : new KioskOsRelease(tag, name, isoUrl, isoSize, checksumUrl);
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
