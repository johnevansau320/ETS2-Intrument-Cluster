using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Ets2Cluster;

/// <summary>A newer release found on GitHub.</summary>
public sealed record UpdateInfo(Version Latest, string Tag, string PageUrl);

/// <summary>
/// Asks GitHub which release is the newest one (one small request to the public API; nothing about you or your PC is sent beyond a normal web request).
/// It never downloads or installs anything: the user is shown the version and chooses to open the release page.
/// </summary>
public static class UpdateCheck
{
    public const string Repo = "johnevansau320/ETS2-Intrument-Cluster";
    public static string ReleasesPage => "https://github.com/" + Repo + "/releases/latest";

    public static string? FakeVersion;   // --fake-version 0.9.0 (test only)

    public static Version Current
    {
        get
        {
            if (FakeVersion != null && Version.TryParse(FakeVersion, out var f)) return f;
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentText => Current.ToString(3);

    /// <summary>Returns the newer release, null when this version is the latest; throws when GitHub cannot be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ETS2-Instrument-Cluster/" + CurrentText);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        string json = await http.GetStringAsync("https://api.github.com/repos/" + Repo + "/releases/latest", ct);
        using var doc = JsonDocument.Parse(json);
        string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesPage : ReleasesPage;
        if (!TryVersion(tag, out var latest)) return null;
        // only ever open a page on github.com
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = ReleasesPage;
        return latest > Current ? new UpdateInfo(latest, tag, url) : null;
    }

    public static bool TryVersion(string tag, out Version v)
    {
        tag = tag.Trim().TrimStart('v', 'V');
        if (Version.TryParse(tag, out var p)) { v = new Version(p.Major, p.Minor, Math.Max(0, p.Build)); return true; }
        v = new Version(0, 0, 0); return false;
    }
}
