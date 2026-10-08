namespace PrayerApp.Services;

/// <summary>
/// Reports whether the system clipboard probably holds a web link without reading its
/// contents, so the OS paste notice is not triggered before the user opts in.
/// </summary>
public interface IClipboardLinkDetector
{
    Task<bool> HasProbableLinkAsync();
}
