using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrayerApp.Models;
using static PrayerApp.Helpers.TextNormalization;

namespace PrayerApp.Services;

public class DeepLinkService : IDeepLinkService
{
    private const string BaseUrl = "https://practicingprayerapp.com/share";
    private const string Footer = "(Shared via Practicing Prayer)";
    private const int MaxUrlLength = 1800;

    private readonly INavigationService _nav;
    private readonly IShareService _shareService;
    private readonly IImportPayloadService _importPayloadService;
    private readonly Func<Page> _confirmImportPageFactory;
    private readonly Func<string> _getCacheDir;

    public DeepLinkService(
        INavigationService nav,
        IShareService shareService,
        IImportPayloadService importPayloadService,
        Func<Page> confirmImportPageFactory)
        : this(nav, shareService, importPayloadService, confirmImportPageFactory, () => FileSystem.CacheDirectory)
    {
    }

    // Test-only constructor: avoids MAUI FileSystem dependency.
    internal DeepLinkService(
        INavigationService nav,
        IShareService shareService,
        IImportPayloadService importPayloadService,
        Func<Page> confirmImportPageFactory,
        Func<string> getCacheDir)
    {
        _nav = nav;
        _shareService = shareService;
        _importPayloadService = importPayloadService;
        _confirmImportPageFactory = confirmImportPageFactory;
        _getCacheDir = getCacheDir;
    }

    // ── Outbound (sharing) ──────────────────────────────────────────────────

    public async Task ShareRequestAsync(Prayer prayer)
    {
        var title = NormalizeQuotes(prayer.Title);
        var details = prayer.Details is not null ? NormalizeQuotes(prayer.Details) : null;

        var json = JsonSerializer.Serialize(new { title, notes = details ?? "" });
        var compressed = GzipCompress(Encoding.UTF8.GetBytes(json));
        var base64 = "z." + ToBase64Url(compressed);
        var uri = $"{BaseUrl}/r?v=2&d={base64}";

        if (uri.Length <= MaxUrlLength)
        {
            var fallback = string.IsNullOrWhiteSpace(details)
                ? title
                : $"{title}\n{details}";
            var text = $"{uri}\n\n{fallback}\n\n{Footer}";
            await _shareService.ShareTextAsync(prayer.Title, text);
        }
        else
        {
            await ShareAsFileAsync(compressed, title ?? "shared-prayer");
        }
    }

    public async Task ShareCardAsync(PrayerCard card, IEnumerable<Prayer> prayers)
    {
        var title = NormalizeQuotes(card.Title);
        var requestList = prayers.Select(p => new
        {
            title = NormalizeQuotes(p.Title),
            notes = p.Details is not null ? NormalizeQuotes(p.Details) : ""
        }).ToArray();
        var json = JsonSerializer.Serialize(new { title, requests = requestList });
        var compressed = GzipCompress(Encoding.UTF8.GetBytes(json));
        var base64 = "z." + ToBase64Url(compressed);
        var uri = $"{BaseUrl}/c?v=2&d={base64}";

        var prayerCount = requestList.Length;

        if (uri.Length <= MaxUrlLength)
        {
            var sb = new StringBuilder();
            sb.AppendLine(uri);
            sb.AppendLine();
            sb.AppendLine(title);
            sb.AppendLine($"+ {prayerCount} request{(prayerCount == 1 ? "" : "s")}");
            sb.AppendLine();
            sb.Append(Footer);
            await _shareService.ShareTextAsync(card.Title, sb.ToString());
        }
        else
        {
            await ShareAsFileAsync(compressed, title ?? "shared-prayer");
        }
    }

    private async Task ShareAsFileAsync(byte[] compressed, string title)
    {
        var cacheDir = _getCacheDir();
        var safeName = SanitizeFileName(title);
        var filePath = Path.Combine(cacheDir, $"{safeName}.prayercard");

        // Clean old .prayercard files from cache (same pattern as BackupService)
        foreach (var old in Directory.GetFiles(cacheDir, "*.prayercard"))
            File.Delete(old);

        await File.WriteAllBytesAsync(filePath, compressed);
        await _shareService.ShareFileAsync($"Share: {title}", filePath, "application/x-prayercard");
    }

    // ── Inbound (receiving via URL) ─────────────────────────────────────────

    /// <summary>
    /// True when <see cref="HandleAsync"/> would stage a payload for this URI: a share
    /// host, a request or card path, and a non-blank <c>d</c> or <c>title</c> parameter.
    /// </summary>
    public static bool IsImportableShareUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || parsed.Host != "practicingprayerapp.com")
            return false;

        var path = parsed.AbsolutePath.TrimEnd('/');
        if (path != "/share/r" && path != "/share/c")
            return false;

        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        return !string.IsNullOrWhiteSpace(query["d"]) || !string.IsNullOrWhiteSpace(query["title"]);
    }

    public async Task HandleAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return;

        if (parsed.Host != "practicingprayerapp.com")
            return;

        var path = parsed.AbsolutePath.TrimEnd('/');
        var query = System.Web.HttpUtility.ParseQueryString(parsed.Query);

        // Future-proof: if a newer format version arrives, tell user to update
        if (IsUnsupportedVersion(query))
        {
            await _nav.DisplayAlertAsync("Update Required",
                "This prayer was shared from a newer version of Practicing Prayer. Please update the app to import it.", "OK");
            return;
        }

        if (path == "/share/r")
            await HandleRequestAsync(query);
        else if (path == "/share/c")
            await HandleCardAsync(query);
    }

    // ── Inbound (receiving via file) ────────────────────────────────────────

    public async Task HandleFileAsync(Stream fileStream)
    {
        try
        {
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            var compressed = ms.ToArray();
            var jsonBytes = GzipDecompress(compressed);
            var json = Encoding.UTF8.GetString(jsonBytes);

            // Determine card vs request by checking for "requests" key
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("requests", out _))
            {
                var data = JsonSerializer.Deserialize<SharedCard>(json);
                if (data is not null)
                    await StageCardAsync(data);
            }
            else
            {
                var data = JsonSerializer.Deserialize<SharedRequest>(json);
                if (data is not null)
                    await StageRequestAsync(data);
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
        {
            System.Diagnostics.Debug.WriteLine($"[DeepLink] Invalid .prayercard file: {ex.Message}");
            await _nav.DisplayAlertAsync("Unable to Import",
                "The shared prayer card file appears to be invalid.", "OK");
        }
    }

    // ── URL handlers ────────────────────────────────────────────────────────

    private async Task HandleRequestAsync(System.Collections.Specialized.NameValueCollection query)
    {
        SharedRequest? data = null;

        var payload = query["d"];
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var json = Encoding.UTF8.GetString(DecodePayload(payload));
                data = JsonSerializer.Deserialize<SharedRequest>(json);
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLink] Invalid shared request data: {ex.Message}");
                await _nav.DisplayAlertAsync("Unable to Import", "The shared prayer link appears to be invalid or incomplete.", "OK");
                return;
            }
        }
        else
        {
            // Legacy format: ?title=&notes= query params (backward compatibility)
            var title = query["title"];
            var notes = query["notes"];
            if (!string.IsNullOrWhiteSpace(title))
                data = new SharedRequest(title, notes);
        }

        if (data is not null)
            await StageRequestAsync(data);
    }

    private async Task HandleCardAsync(System.Collections.Specialized.NameValueCollection query)
    {
        SharedCard? data = null;

        var payload = query["d"];
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                var json = Encoding.UTF8.GetString(DecodePayload(payload));
                data = JsonSerializer.Deserialize<SharedCard>(json);
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
            {
                System.Diagnostics.Debug.WriteLine($"[DeepLink] Invalid shared card data: {ex.Message}");
                await _nav.DisplayAlertAsync("Unable to Import", "The shared prayer card link appears to be invalid or incomplete.", "OK");
                return;
            }
        }
        else
        {
            // Legacy format: ?title=&requests= query params (backward compatibility)
            var title = query["title"];
            var requestsBase64 = query["requests"];
            if (!string.IsNullOrWhiteSpace(requestsBase64))
            {
                try
                {
                    var json = Encoding.UTF8.GetString(FromBase64Url(requestsBase64));
                    var requests = JsonSerializer.Deserialize<SharedRequest[]>(json);
                    if (!string.IsNullOrWhiteSpace(title))
                        data = new SharedCard(title, requests);
                }
                catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException)
                {
                    System.Diagnostics.Debug.WriteLine($"[DeepLink] Invalid shared card data: {ex.Message}");
                    await _nav.DisplayAlertAsync("Unable to Import", "The shared prayer card link appears to be invalid or incomplete.", "OK");
                    return;
                }
            }
        }

        if (data is not null)
            await StageCardAsync(data);
    }

    // ── Stage → ConfirmImportPage handoff ───────────────────────────────────

    /// <summary>
    /// Single-prayer URL / file inbound. Locked decision: SuggestedCardTitle
    /// defaults to the prayer title (user can rename on ConfirmImportPage).
    /// </summary>
    private async Task StageRequestAsync(SharedRequest data)
    {
        var title = NormalizeQuotes(data.Title);
        if (string.IsNullOrWhiteSpace(title)) return;

        var detail = string.IsNullOrEmpty(data.Notes) ? null : NormalizeQuotes(data.Notes);
        var staged = new ParseResult(
            new[] { new ParsedPrayer(title, detail) },
            title);
        _importPayloadService.StageStructured(staged);
        await PushConfirmImportPageAsync();
    }

    private async Task StageCardAsync(SharedCard data)
    {
        var title = NormalizeQuotes(data.Title);
        if (string.IsNullOrWhiteSpace(title)) return;

        // Filter out empty-title requests (data quality guard).
        var requests = data.Requests?
            .Where(r => !string.IsNullOrWhiteSpace(r?.Title))
            .ToArray();
        if (requests is null || requests.Length == 0) return;

        var staged = new ParseResult(
            requests.Select(r => new ParsedPrayer(
                NormalizeQuotes(r!.Title) ?? string.Empty,
                string.IsNullOrEmpty(r.Notes) ? null : NormalizeQuotes(r.Notes))).ToArray(),
            title);
        _importPayloadService.StageStructured(staged);
        await PushConfirmImportPageAsync();
    }

    private Task PushConfirmImportPageAsync()
        => _nav.PushModalWithNavigationBarAsync(_confirmImportPageFactory());

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsUnsupportedVersion(System.Collections.Specialized.NameValueCollection query)
    {
        var version = query["v"];
        return version != null && int.TryParse(version, out var v) && v > 2;
    }

    private static byte[] DecodePayload(string payload)
    {
        // Strip trailing non-base64url characters that some messaging apps
        // inject when they URL-encode the fallback share text into the d= param
        // (email, Slack, Discord, Teams — SMS apps stop at literal whitespace)
        payload = TakeBase64UrlPrefix(payload);

        if (payload.StartsWith("z."))
            return GzipDecompress(FromBase64Url(payload[2..]));
        return FromBase64Url(payload);
    }

    private static string TakeBase64UrlPrefix(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            bool valid = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') ||
                         (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.';
            if (!valid) return s[..i];
        }
        return s;
    }

    private static byte[] GzipCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] GzipDecompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string ToBase64Url(byte[] data)
        => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] FromBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        return Convert.FromBase64String(base64);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "shared-prayer" : sanitized;
    }

    private record SharedRequest(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("notes")] string? Notes);

    private record SharedCard(
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("requests")] SharedRequest[]? Requests);
}
