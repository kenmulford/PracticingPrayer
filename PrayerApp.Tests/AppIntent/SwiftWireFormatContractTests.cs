using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PrayerApp.Shared;
using Xunit;

namespace PrayerApp.Tests.AppIntent;

/// <summary>
/// Locks the wire-format contract between the Swift App Intents Extension
/// (PrayerApp.AppIntents/PrayerAppIntents/AppGroupWriter.swift) and the
/// host's deserializer (PrayerApp.Shared/ImportPayloadJsonContext).
///
/// To regenerate the fixture after a deliberate format change:
///   1. Trigger the M3 device test (Notes → share → "Save to Practicing Prayer").
///   2. Pull pending-import.json from the App Group container:
///        xcrun devicectl device copy from
///          --domain-type appGroupDataContainer
///          --domain-identifier group.com.multithreadedllc.prayercards
///          --source pending-import.json
///          --destination ./fixture-new.json
///          --device &lt;UDID&gt;
///   3. Replace the "ts" value with "2026-05-03T12:00:00.000Z" so the fixture
///      is reproducible.
///   4. Commit to contracts/app-group/. Re-run these tests.
/// </summary>
public class SwiftWireFormatContractTests
{
    private const string FixturePath = "contracts/app-group/pending-import.golden.json";

    [Fact]
    public void Golden_Fixture_Deserializes_Through_Host_JsonContext()
    {
        var json = File.ReadAllText(FixturePath);

        var payload = JsonSerializer.Deserialize(json, ImportPayloadJsonContext.Default.ImportPayload);

        Assert.NotNull(payload);
        Assert.False(string.IsNullOrEmpty(payload!.Raw));
        Assert.True(
            DateTime.TryParse(payload.Ts, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _),
            $"Ts '{payload.Ts}' did not parse as UTC ISO 8601");
        Assert.True(
            Encoding.UTF8.GetByteCount(payload.Raw) <= 256 * 1024,
            "Raw exceeds the 256 KiB cap enforced by both extensions");
    }

    [Fact]
    public void Golden_Fixture_Uses_Lowercase_CamelCase_Keys()
    {
        var json = File.ReadAllText(FixturePath);

        Assert.Contains("\"raw\":", json);
        Assert.Contains("\"ts\":", json);
        Assert.DoesNotContain("\"Raw\":", json);
        Assert.DoesNotContain("\"Ts\":", json);
    }

    [Fact]
    public void Golden_Fixture_Raw_Is_NFC_Normalized()
    {
        var json = File.ReadAllText(FixturePath);
        var payload = JsonSerializer.Deserialize(json, ImportPayloadJsonContext.Default.ImportPayload)!;

        // The Swift writer applies precomposedStringWithCanonicalMapping.
        // The C# Share Extension applies String.Normalize(NormalizationForm.FormC).
        // Both produce NFC. Asserting self-equality under FormC catches drift.
        Assert.Equal(payload.Raw.Normalize(NormalizationForm.FormC), payload.Raw);
    }

    [Fact]
    public void Golden_Fixture_Ts_Matches_Expected_ISO8601_Shape()
    {
        var json = File.ReadAllText(FixturePath);
        var payload = JsonSerializer.Deserialize(json, ImportPayloadJsonContext.Default.ImportPayload)!;

        // Swift's ISO8601DateFormatter with .withFractionalSeconds emits 3 fractional digits + Z.
        // C# DateTime.UtcNow.ToString("o") emits 7 digits + Z, but the fixture is the Swift form.
        // Lock the shape so a future iOS version that adds a 4th fractional digit (or changes
        // the suffix) breaks this test loudly instead of silently shifting downstream behavior.
        var pattern = @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$";
        Assert.Matches(pattern, payload.Ts);
    }

    [Fact]
    public void Csharp_NFC_Normalize_Composes_NFD_Input()
    {
        // Direct behavioral assertion of the .NET BCL call we rely on in
        // PrayerApp.ActionExtension/ShareViewController.cs at the LoadItem
        // boundary. If a future runtime change altered the semantics, this
        // would surface before the Share Extension shipped a divergent payload.
        var nfd = "José";        // 'e' + COMBINING ACUTE ACCENT
        var nfc = "José";          // 'é' precomposed

        Assert.NotEqual(nfd, nfc);                                 // bytes differ
        Assert.Equal(nfc, nfd.Normalize(NormalizationForm.FormC)); // FormC composes
        Assert.Equal(nfc, nfc.Normalize(NormalizationForm.FormC)); // idempotent on NFC
    }
}
