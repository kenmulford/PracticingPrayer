using System.IO.Compression;
using System.Text.Json;
using Microsoft.Maui.Controls;
using NSubstitute;
using PrayerApp.Models;
using PrayerApp.Services;

namespace PrayerApp.Tests.Services;

public class DeepLinkServiceTests : IDisposable
{
    private readonly INavigationService _nav;
    private readonly IShareService _shareService;
    private readonly IImportPayloadService _payloadService;
    private readonly Page _confirmImportPage;
    private readonly DeepLinkService _service;
    private readonly string _tempDir;

    public DeepLinkServiceTests()
    {
        _nav = Substitute.For<INavigationService>();
        _shareService = Substitute.For<IShareService>();
        _payloadService = Substitute.For<IImportPayloadService>();
        _confirmImportPage = Substitute.For<Page>();

        _tempDir = Path.Combine(Path.GetTempPath(), $"deeplink-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new DeepLinkService(
            _nav, _shareService, _payloadService,
            () => _confirmImportPage, () => _tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helper: build a compressed URL the same way the service does ────────

    private static string BuildCompressedUrl(string path, string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes);
        }
        var base64 = Convert.ToBase64String(output.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"https://practicingprayerapp.com/share/{path}?v=2&d=z.{base64}";
    }

    // ── ShareRequestAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ShareRequestAsync_SmallRequest_SharesAsText()
    {
        var prayer = new Prayer { Title = "Heal my friend", Details = "Surgery next week" };

        await _service.ShareRequestAsync(prayer);

        await _shareService.Received(1).ShareTextAsync(
            Arg.Any<string>(),
            Arg.Is<string>(text =>
                text.Contains("https://practicingprayerapp.com/share/r?v=2&d=z.") &&
                text.Contains("Heal my friend") &&
                text.Contains("Surgery next week") &&
                text.Contains("Shared via Practicing Prayer")));
    }

    [Fact]
    public async Task ShareRequestAsync_NormalizesSmartQuotes()
    {
        var prayer = new Prayer { Title = "Mom’s surgery" };

        await _service.ShareRequestAsync(prayer);

        await _shareService.Received(1).ShareTextAsync(
            Arg.Any<string>(),
            Arg.Is<string>(text => text.Contains("Mom's surgery")));
    }

    [Fact]
    public async Task ShareRequestAsync_NullDetails_SharesWithoutDetails()
    {
        var prayer = new Prayer { Title = "Simple prayer", Details = null };

        await _service.ShareRequestAsync(prayer);

        await _shareService.Received(1).ShareTextAsync(
            Arg.Any<string>(),
            Arg.Is<string>(text =>
                text.Contains("/share/r?v=2&d=z.") &&
                !text.Contains("null", StringComparison.OrdinalIgnoreCase)));
    }

    // ── ShareCardAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ShareCardAsync_SmallCard_SharesAsText()
    {
        var card = new PrayerCard { Title = "Family Prayers" };
        var prayers = new List<Prayer>
        {
            new() { Title = "Mom’s health" },
            new() { Title = "Dad’s job" }
        };

        await _service.ShareCardAsync(card, prayers);

        await _shareService.Received(1).ShareTextAsync(
            Arg.Any<string>(),
            Arg.Is<string>(text =>
                text.Contains("https://practicingprayerapp.com/share/c?v=2&d=z.") &&
                text.Contains("Family Prayers") &&
                text.Contains("+ 2 requests") &&
                text.Contains("Shared via Practicing Prayer")));
    }

    [Fact]
    public async Task ShareCardAsync_LargeCard_SharesAsFile()
    {
        var card = new PrayerCard { Title = "Big Card" };
        // Generate prayers with unique text that doesn't compress well (GZip excels
        // at repetitive patterns, so we need varied content to exceed 1800 chars)
        var rng = new Random(42); // deterministic seed
        var prayers = Enumerable.Range(1, 100).Select(i =>
        {
            var guid = new Guid(rng.Next(), 0, 0, new byte[8]).ToString();
            return new Prayer
            {
                Title = $"Unique prayer {guid} for person {i}",
                Details = $"Detailed notes {guid} about situation {i}: {Guid.NewGuid()}"
            };
        }).ToList();

        await _service.ShareCardAsync(card, prayers);

        await _shareService.Received(1).ShareFileAsync(
            Arg.Is<string>(t => t.Contains("Big Card")),
            Arg.Is<string>(path => path.EndsWith(".prayercard")),
            "application/x-prayercard");
        // Should NOT have called ShareTextAsync
        await _shareService.DidNotReceive().ShareTextAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    // ── HandleAsync — Request: stages structured + pushes modal ─────────────

    [Fact]
    public async Task HandleAsync_Request_SinglePrayer_CopiesPrayerTitleToCardTitle()
    {
        // Locked decision 4: single-prayer URL imports default CardTitle to
        // the prayer title. User can edit before saving on ConfirmImportPage.
        var json = JsonSerializer.Serialize(
            new { title = "Pray for safe travel", notes = "Flight on Tuesday" });
        var uri = BuildCompressedUrl("r", json);

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Pray for safe travel" &&
            s.Prayers.Count == 1 &&
            s.Prayers[0].Title == "Pray for safe travel" &&
            s.Prayers[0].Details == "Flight on Tuesday"));
    }

    [Fact]
    public async Task HandleAsync_Request_PushesConfirmImportPageModally()
    {
        var json = JsonSerializer.Serialize(new { title = "Test", notes = "" });
        var uri = BuildCompressedUrl("r", json);

        await _service.HandleAsync(uri);

        await _nav.Received(1).PushModalWithNavigationBarAsync(_confirmImportPage);
    }

    [Fact]
    public async Task HandleAsync_Request_LegacyQueryParams_StagesStructured()
    {
        // Old-format URL with title/notes query params (backward compat).
        await _service.HandleAsync(
            "https://practicingprayerapp.com/share/r?title=Test%20Prayer&notes=Details");

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test Prayer" &&
            s.Prayers.Count == 1 &&
            s.Prayers[0].Title == "Test Prayer" &&
            s.Prayers[0].Details == "Details"));
    }

    [Fact]
    public async Task HandleAsync_Request_UncompressedPayload_StagesStructured()
    {
        // Old-format URL without z. prefix (backward compat).
        var json = JsonSerializer.Serialize(
            new { title = "Old format prayer", notes = "Details here" });
        var rawBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await _service.HandleAsync($"https://practicingprayerapp.com/share/r?d={rawBase64}");

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Old format prayer" &&
            s.Prayers[0].Details == "Details here"));
    }

    // ── HandleAsync — Card: stages structured + pushes modal ────────────────

    [Fact]
    public async Task HandleAsync_Card_StagesStructuredWithCardTitleAndAllPrayers()
    {
        var json = JsonSerializer.Serialize(new
        {
            title = "Family Prayers",
            requests = new[]
            {
                new { title = "Mom health", notes = "Ongoing treatment" },
                new { title = "Dad job", notes = "" }
            }
        });
        var uri = BuildCompressedUrl("c", json);

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Family Prayers" &&
            s.Prayers.Count == 2 &&
            s.Prayers[0].Title == "Mom health" && s.Prayers[0].Details == "Ongoing treatment" &&
            s.Prayers[1].Title == "Dad job"));
    }

    [Fact]
    public async Task HandleAsync_Card_PushesConfirmImportPageModally()
    {
        var json = JsonSerializer.Serialize(new
        {
            title = "Family",
            requests = new[] { new { title = "Mom", notes = "" } }
        });
        var uri = BuildCompressedUrl("c", json);

        await _service.HandleAsync(uri);

        await _nav.Received(1).PushModalWithNavigationBarAsync(_confirmImportPage);
    }

    [Fact]
    public async Task HandleAsync_Card_LegacyFormat_StagesStructured()
    {
        var requestsJson = JsonSerializer.Serialize(new[]
        {
            new { title = "Prayer 1", notes = "Details 1" },
            new { title = "Prayer 2", notes = "" }
        });
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(requestsJson))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var uri = $"https://practicingprayerapp.com/share/c?title=Shared%20Card&requests={base64}";

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Shared Card" && s.Prayers.Count == 2));
    }

    // ── Smart-quote normalization (split out so a regression points at the
    //    right thing — see QA reviewer note) ────────────────────────────────

    [Fact]
    public async Task HandleAsync_NormalizesSmartQuotes_InStagedPayload()
    {
        // Smart curly quote in payload → straight ASCII apostrophe in
        // staged CardTitle and prayer Title/Details.
        var json = JsonSerializer.Serialize(
            new { title = "Mom’s surgery", notes = "Pray it goes well" });
        var uri = BuildCompressedUrl("r", json);

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Mom's surgery" &&
            s.Prayers[0].Title == "Mom's surgery"));
    }

    // ── Stage-before-push ordering ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_StagesStructuredBeforePushingModal()
    {
        // Cold-start safety: payload must be staged before the modal push
        // is dispatched so it survives a transient push failure /
        // app-tear-down. Cold-start gate behavior itself lives in
        // ModalPushSequenceTests.
        var json = JsonSerializer.Serialize(new { title = "Order", notes = "" });

        await _service.HandleAsync(BuildCompressedUrl("r", json));

        Received.InOrder(() =>
        {
            _payloadService.StageStructured(Arg.Any<ParseResult>());
            _nav.PushModalWithNavigationBarAsync(_confirmImportPage);
        });
    }

    // ── Failure modes ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RepeatedHandle_PushesModalEachTime()
    {
        // Documents acceptable behavior: a deep link firing while
        // ConfirmImportPage is already presented stacks a second modal on
        // top. Mid-edit collision is deferred per plan; this test pins the
        // current behavior so a future "skip if modal up" change is a
        // deliberate RED.
        var json = JsonSerializer.Serialize(new { title = "Test", notes = "" });

        await _service.HandleAsync(BuildCompressedUrl("r", json));
        await _service.HandleAsync(BuildCompressedUrl("r", json));

        await _nav.Received(2).PushModalWithNavigationBarAsync(_confirmImportPage);
    }

    [Fact]
    public async Task HandleAsync_Card_EmptyRequestsArray_DoesNotStageOrPushModal()
    {
        // Empty requests → nothing useful to import. Staging an empty
        // StagedImport would open ConfirmImportPage with no rows and a
        // Save button that can't enable.
        var json = JsonSerializer.Serialize(new
        {
            title = "Empty",
            requests = Array.Empty<object>()
        });
        var uri = BuildCompressedUrl("c", json);

        await _service.HandleAsync(uri);

        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
        await _nav.DidNotReceive().PushModalWithNavigationBarAsync(Arg.Any<Page>());
    }

    [Fact]
    public async Task HandleAsync_Request_WhitespaceOnlyTitle_NoOps()
    {
        // Mirrors HandleAsync_MissingTitle_NoOp guard — whitespace title
        // shouldn't bypass it.
        var json = JsonSerializer.Serialize(new { title = "   ", notes = "anything" });
        var uri = BuildCompressedUrl("r", json);

        await _service.HandleAsync(uri);

        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    // ── HandleAsync — Invalid URIs ──────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_InvalidUri_NoOp()
    {
        await _service.HandleAsync("https://example.com/other");

        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
        await _nav.DidNotReceive().PushModalWithNavigationBarAsync(Arg.Any<Page>());
    }

    [Fact]
    public async Task HandleAsync_EmptyUri_NoOp()
    {
        await _service.HandleAsync("");

        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleAsync_MissingTitle_NoOp()
    {
        await _service.HandleAsync("https://practicingprayerapp.com/share/r?notes=something");

        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleAsync_Card_InvalidBase64_ShowsAlert()
    {
        await _service.HandleAsync("https://practicingprayerapp.com/share/c?title=Test&requests=!!!invalid!!!");

        await _nav.Received(1).DisplayAlertAsync("Unable to Import", Arg.Any<string>(), "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleAsync_Card_InvalidJson_ShowsAlert()
    {
        var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not valid json"))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await _service.HandleAsync($"https://practicingprayerapp.com/share/c?title=Test&requests={base64}");

        await _nav.Received(1).DisplayAlertAsync("Unable to Import", Arg.Any<string>(), "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleAsync_Request_GzipPayload_CorruptData_ShowsAlert()
    {
        // z. prefix but garbage data after it
        var corruptBase64 = Convert.ToBase64String(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC })
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await _service.HandleAsync($"https://practicingprayerapp.com/share/r?v=2&d=z.{corruptBase64}");

        await _nav.Received(1).DisplayAlertAsync("Unable to Import", Arg.Any<string>(), "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleAsync_Request_FutureVersion_ShowsUpdateAlert()
    {
        // A v=3 link from a future app version should prompt the user to update
        var json = JsonSerializer.Serialize(new { title = "Future", notes = "" });
        var rawBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        await _service.HandleAsync($"https://practicingprayerapp.com/share/r?v=3&d=z.{rawBase64}");

        await _nav.Received(1).DisplayAlertAsync(
            "Update Required",
            Arg.Is<string>(s => s.Contains("newer version")),
            "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    // ── HandleFileAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleFileAsync_ValidCardStream_StagesStructured()
    {
        var json = JsonSerializer.Serialize(new
        {
            title = "Shared Card",
            requests = new[]
            {
                new { title = "Prayer 1", notes = "Details 1" },
                new { title = "Prayer 2", notes = "" }
            }
        });
        using var stream = CompressToStream(json);

        await _service.HandleFileAsync(stream);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Shared Card" && s.Prayers.Count == 2));
        await _nav.Received(1).PushModalWithNavigationBarAsync(_confirmImportPage);
    }

    [Fact]
    public async Task HandleFileAsync_ValidRequestStream_StagesStructured()
    {
        var json = JsonSerializer.Serialize(
            new { title = "Test Prayer", notes = "Details" });
        using var stream = CompressToStream(json);

        await _service.HandleFileAsync(stream);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test Prayer" &&
            s.Prayers.Count == 1 &&
            s.Prayers[0].Title == "Test Prayer" &&
            s.Prayers[0].Details == "Details"));
    }

    [Fact]
    public async Task HandleFileAsync_CorruptStream_ShowsAlert()
    {
        using var stream = new MemoryStream(new byte[] { 0xFF, 0xFE, 0xFD, 0xFC });

        await _service.HandleFileAsync(stream);

        await _nav.Received(1).DisplayAlertAsync("Unable to Import", Arg.Any<string>(), "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    [Fact]
    public async Task HandleFileAsync_EmptyStream_ShowsAlert()
    {
        using var stream = new MemoryStream();

        await _service.HandleFileAsync(stream);

        await _nav.Received(1).DisplayAlertAsync("Unable to Import", Arg.Any<string>(), "OK");
        _payloadService.DidNotReceive().StageStructured(Arg.Any<ParseResult>());
    }

    // ── Real-world URL import ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RealWorldCardUrl_StagesAllPrayers()
    {
        // Repro fixture from BUG-79 (build 94 share-import smoke test).
        // Actual URL generated by sharing a 20-prayer test card.
        var url = "https://practicingprayerapp.com/share/c?v=2&d=z.H4sIAAAAAAAAA-2Xu04DMRBFf2XkeonY8E6HqJAoIkGHKIx3SAZ5H_F4IqEo_85sItHgH1h7i218PZo9Os31wUSKHs3KvCFHeLKhgQt46QO28DywtKYyAXeiIZvV--Hv-jrYHwxQa971ETU05ykap6DpfR-AKYJtMVbg-o7RRYwSwDY0EDvqNoCe4gIePe3EtuOlvfWeGPZjACgQiIVBP6czEb5QNmR1ZL21jN5rJj4GcsjQ2IE-9aAT720FqKudk5ZtB9_CsT__gy7SYAGvwgN2DTEj7ERXnjZqdhqHvQ0kvIAMmcyx-qdxOWucGlNK49WscWpMKY3Xs8apMaU03swap8aU0nibt8YU8l15yPflIT-Uh1xfFsic-RstyZz5gybJnHn7TzJnXpWTzJn3yiRzgSWsLrCF1QXWsLrAHrbMvod9HH8B4fHoj1sXAAA";

        await _service.HandleAsync(url);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test Card - Lorem Ipsum" &&
            s.Prayers.Count == 20 &&
            s.Prayers[0].Title == "Prayer 1" &&
            s.Prayers[0].Details!.StartsWith("Lorem ipsum") &&
            s.Prayers[19].Title == "Prayer 20"));
    }

    // ── URL cleanup: trailing fallback text from messaging apps ──────────────

    [Fact]
    public async Task HandleAsync_EmailEncodedUrlFromBugReport_ImportsSuccessfully()
    {
        // Repro fixture from user bug report 2026-04: email client URL-encoded
        // the entire share text, jamming the fallback into the d= parameter.
        // SMS worked; email/Slack/Discord/Teams need this cleanup.
        var url = "https://practicingprayerapp.com/share/c?v=2&d=z.H4sIAAAAAAAAE22TQU_cMBCF_8qQc7pCHNqKWxWpSyvRonKgUtuDSSbJaB07HY_ZRoj_3hkHlgVx2o09fvPN8_N9JSQeq_Pq08zk4SOcnZ69r-qK8W_GJKk6_3V_qLlw7HHR3RAFdav6HBnaOM0eBWFE5ykM0HOcwHmvCwwTdtQ6D-2oKxgGTJvqoX6WbJqrBlzooNmmI-Erdgv0qh4zg0fXIacaZHRhB9vYHbaabV1-2zFzO9ZFaT86gSl7odlrb6EYYE_K42PcgacdgkvwjdpdKf8al9JB1ZEYBo55Bgr2Cb1Cv-JlpGE8Ir3eIwpIhIRY0GaOd9SZEaY-Z07ZPm6MwFYuHS_KSB6txQId050SadtQGNTQHISXDRxMaGMQChk7M3vKgWR5SXWd8QipOZRj32MrCWIPc8A8xUCuhp_vTFjiFJnj3thbNYDLfVm7kWZImQd8glDzVw4rc6FFO3Or0Le-_B2dTvB4osxoUdjAC8SbOGH4nU9Pzz4k-IHC6OSI-dIFdcVqoHXMi1mGU7TL0_TszXQp0kdBWg0yy3jVU4HsOyPrc6jNtqDjq9QaDNZA68fjOXoq16TrwEU7TjqmALp2NBgXXg3RlJjB976nVs0SdhRU8a3gGlZc61KJ02NtXXY1JGJoGk036EYSSDMxSbZpHfeO8VVvzelRnwt7W_rORo2yGxj1svUixGm4y5OjgCdwgX42I_ckoy7rfS_mxD9CWVZLOpzVlaQcG_giT9fzHOGkN6K-ecJ-jYa27eytxFhspFRCf1I9_Hn4D1UzDUJMBAAA%0A%0AApril%208%202026%0A+%207%20requests%0A%0A(Shared%20via%20Practicing%20Prayer)";

        await _service.HandleAsync(url);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "April 8 2026" && s.Prayers.Count == 7));
        await _nav.DidNotReceive().DisplayAlertAsync(
            "Unable to Import", Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_UrlWithTrailingNewlineInPayload_StripsAndStages()
    {
        var json = JsonSerializer.Serialize(new { title = "Test", notes = "" });
        var uri = BuildCompressedUrl("r", json);
        // Append garbage after the payload (URL-encoded newline + prose)
        uri += "%0A%0ASome%20trailing%20text";

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test"));
    }

    [Fact]
    public async Task HandleAsync_UrlWithTrailingParentheses_StripsAndStages()
    {
        var json = JsonSerializer.Serialize(new { title = "Test", notes = "" });
        var uri = BuildCompressedUrl("r", json);
        // Some apps embed the whole "(Shared via Practicing Prayer)" footer
        uri += "%28Shared%20via%20Practicing%20Prayer%29";

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test"));
    }

    [Fact]
    public async Task HandleAsync_UrlWithTrailingEmoji_StripsAndStages()
    {
        var json = JsonSerializer.Serialize(new { title = "Test", notes = "" });
        var uri = BuildCompressedUrl("r", json);
        // User reaction emoji appended (Slack/Discord behavior)
        uri += "%F0%9F%99%8F"; // 🙏

        await _service.HandleAsync(uri);

        _payloadService.Received(1).StageStructured(Arg.Is<ParseResult>(s =>
            s.SuggestedCardTitle == "Test"));
    }

    // ── TryExtractShareUrl ──────────────────────────────────────────────────

    [Theory]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("  \thttps://practicingprayerapp.com/share/r?d=abc", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc\n\nPray for Mom", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc\r\nPray", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc Pray", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc\tPray", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc\u00A0Pray", "https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc\u2028Pray", "https://practicingprayerapp.com/share/r?d=abc")]
    public void TryExtractShareUrl_ShareText_ReturnsUrlUpToFirstWhitespace(string text, string expected)
    {
        Assert.True(DeepLinkService.TryExtractShareUrl(text, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("https://example.com/share/r?d=abc")]
    [InlineData("see https://practicingprayerapp.com/share/r?d=abc")]
    public void TryExtractShareUrl_NotAShareUrl_ReturnsFalse(string? text)
    {
        Assert.False(DeepLinkService.TryExtractShareUrl(text, out _));
    }

    // ── IsImportableShareUri ────────────────────────────────────────────────

    [Theory]
    [InlineData("https://practicingprayerapp.com/share/r?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r/?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/r?title=Heal")]
    [InlineData("https://practicingprayerapp.com/share/c?d=abc")]
    [InlineData("https://practicingprayerapp.com/share/c?title=Family&requests=abc")]
    public void IsImportableShareUri_StagingShape_ReturnsTrue(string uri)
    {
        Assert.True(DeepLinkService.IsImportableShareUri(uri));
    }

    [Theory]
    [InlineData("https://practicingprayerapp.com/share/c?title=x")]
    [InlineData("https://practicingprayerapp.com/share/c?requests=abc")]
    [InlineData("https://practicingprayerapp.com/share/c?title=%20&requests=abc")]
    [InlineData("https://practicingprayerapp.com/share/r")]
    [InlineData("https://practicingprayerapp.com/share/r?notes=something")]
    [InlineData("https://practicingprayerapp.com/share/x?d=abc")]
    [InlineData("https://example.com/share/r?d=abc")]
    [InlineData("not a url")]
    [InlineData("")]
    public void IsImportableShareUri_NothingToStage_ReturnsFalse(string uri)
    {
        Assert.False(DeepLinkService.IsImportableShareUri(uri));
    }

    // ── Helper ──────────────────────────────────────────────────────────────

    private static MemoryStream CompressToStream(string json)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            gzip.Write(bytes);
        }
        return new MemoryStream(output.ToArray());
    }
}
