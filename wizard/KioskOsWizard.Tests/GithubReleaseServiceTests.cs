using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KioskOsWizard.Services;
using Xunit;

namespace KioskOsWizard.Tests;

/// <summary>
/// The wizard finds releases through the Atom feed, which has no rate limit,
/// and checks for the ISO with a HEAD request. No request may go to the
/// rate-limited REST API (api.github.com).
/// </summary>
public class GithubReleaseServiceTests
{
    private const string Repo = "https://github.com/LennartKleymann/kiosk-os";

    private static string Feed(params string[] tags) =>
        """<?xml version="1.0" encoding="UTF-8"?><feed xmlns="http://www.w3.org/2005/Atom" xml:lang="en-US">""" +
        "<title>Release notes from kiosk-os</title>" +
        string.Concat(tags.Select(t =>
            $"""<entry><id>tag:github.com,2008:Repository/1/{t}</id><updated>2026-10-05T21:00:00Z</updated>""" +
            $"""<link rel="alternate" type="text/html" href="{Repo}/releases/tag/{t}"/><title>{t}</title><content type="html">notes</content></entry>""")) +
        "</feed>";

    /// <summary>Answers the feed and HEAD requests; records every URL it was asked for.</summary>
    private sealed class FakeGitHub(string feed, Dictionary<string, long> isos) : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add($"{request.Method} {url}");
            if (url == GithubReleaseService.FeedUrl)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(feed) });

            var hit = isos.FirstOrDefault(kv => url == $"{Repo}/releases/download/{kv.Key}/kiosk-os.iso");
            if (request.Method == HttpMethod.Head && hit.Key is not null)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                response.Content.Headers.ContentLength = hit.Value;
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (GithubReleaseService, FakeGitHub) Service(string feed, Dictionary<string, long> isos)
    {
        var fake = new FakeGitHub(feed, isos);
        return (new GithubReleaseService(new HttpClient(fake)), fake);
    }

    [Fact]
    public void Feed_entries_yield_tags_in_order()
    {
        var parsed = GithubReleaseService.ParseFeed(Feed("v0.1.0-rc7", "v0.1.0-rc6"));
        Assert.Equal(["v0.1.0-rc7", "v0.1.0-rc6"], parsed.Select(p => p.Tag));
    }

    [Theory]
    [InlineData("v0.1.0-rc7", true)]
    [InlineData("v1.0.0", false)]
    public void Suffixed_tags_are_prereleases(string tag, bool prerelease) =>
        Assert.Equal(prerelease, GithubReleaseService.IsPrerelease(tag));

    [Fact]
    public async Task Newest_prerelease_is_used_while_there_is_no_stable_release()
    {
        var (service, _) = Service(Feed("v0.1.0-rc7", "v0.1.0-rc6"),
            new() { ["v0.1.0-rc7"] = 1_778_761_728, ["v0.1.0-rc6"] = 1_778_761_728 });

        var release = await service.GetLatestReleaseAsync();

        Assert.NotNull(release);
        Assert.Equal("v0.1.0-rc7", release.TagName);
        Assert.Equal(1_778_761_728, release.Size);
        Assert.Equal($"{Repo}/releases/download/v0.1.0-rc7/kiosk-os.iso", release.DownloadUrl);
        Assert.Equal($"{Repo}/releases/download/v0.1.0-rc7/kiosk-os.iso.sha256", release.ChecksumUrl);
    }

    [Fact]
    public async Task Stable_release_wins_over_a_newer_prerelease()
    {
        var (service, _) = Service(Feed("v1.1.0-rc1", "v1.0.0", "v1.0.0-rc2"),
            new() { ["v1.1.0-rc1"] = 10, ["v1.0.0"] = 20, ["v1.0.0-rc2"] = 30 });

        Assert.Equal("v1.0.0", (await service.GetLatestReleaseAsync())!.TagName);
    }

    [Fact]
    public async Task Release_without_uploaded_iso_is_skipped()
    {
        // e.g. the release exists but its build has not attached the ISO yet
        var (service, _) = Service(Feed("v0.1.0-rc8", "v0.1.0-rc7"), new() { ["v0.1.0-rc7"] = 42 });

        Assert.Equal("v0.1.0-rc7", (await service.GetLatestReleaseAsync())!.TagName);
    }

    [Fact]
    public async Task No_release_with_an_iso_returns_null()
    {
        var (service, _) = Service(Feed("v0.1.0-rc1"), new());
        Assert.Null(await service.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task The_rate_limited_api_is_never_called()
    {
        var (service, fake) = Service(Feed("v0.1.0-rc7"), new() { ["v0.1.0-rc7"] = 1 });
        await service.GetLatestReleaseAsync();

        Assert.DoesNotContain(fake.Requests, r => r.Contains("api.github.com", StringComparison.Ordinal));
    }
}
