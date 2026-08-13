using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KioskOsWizard.Services;

public record KioskOsRelease(string TagName, string Name, string DownloadUrl, long Size);

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

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = asset.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
            if (!assetName.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) continue;

            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
            var size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;

            return new KioskOsRelease(tag, name, url, size);
        }

        return null;
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
