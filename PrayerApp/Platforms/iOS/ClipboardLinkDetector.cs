using Foundation;
using PrayerApp.Services;
using UIKit;

namespace PrayerApp.Platforms.iOS;

/// <summary>
/// Pattern detection reports whether the pasteboard holds a web URL without reading its
/// value, so iOS shows no "pasted from" notice until the user opts in and the text is read.
/// </summary>
public class ClipboardLinkDetector : IClipboardLinkDetector
{
    public async Task<bool> HasProbableLinkAsync()
    {
        // CA1416 reports the pattern constant as removed on maccatalyst 17.4+; this app has
        // no Mac Catalyst target.
#pragma warning disable CA1416
        var webUrl = UIPasteboardDetectionPattern.ProbableWebUrl.GetConstant();
#pragma warning restore CA1416
        if (webUrl is null)
            return false;

        try
        {
            var detected = await UIPasteboard.General.DetectPatternsAsync(new NSSet<NSString>(webUrl));
            return detected.Contains(webUrl);
        }
        catch (NSErrorException)
        {
            return false;
        }
    }
}
