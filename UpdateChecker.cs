using System.Net.Http.Headers;
using System.Text.Json;

namespace WindowKeeper;

internal sealed record UpdateResult(bool Available, string LatestVersion, string ReleaseUrl);

internal static class UpdateChecker
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/naturian/WindowKeeper/releases/latest";

    public static async Task<UpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
            "WindowKeeper", Diagnostics.VersionText));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/vnd.github+json"));

        using HttpResponseMessage response = await client.GetAsync(
            LatestReleaseApi, cancellationToken).ConfigureAwait(true);
        response.EnsureSuccessStatusCode();
        await using Stream body = await response.Content.ReadAsStreamAsync(
            cancellationToken).ConfigureAwait(true);
        using JsonDocument json = await JsonDocument.ParseAsync(
            body, cancellationToken: cancellationToken).ConfigureAwait(true);

        string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = json.RootElement.GetProperty("html_url").GetString()
            ?? "https://github.com/naturian/WindowKeeper/releases";
        string latestText = tag.TrimStart('v', 'V');
        bool available = Version.TryParse(latestText, out Version? latest)
            && Version.TryParse(Diagnostics.VersionText, out Version? current)
            && latest > current;
        return new UpdateResult(available, latestText, url);
    }
}
