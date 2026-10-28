using System.Runtime.CompilerServices;

namespace PrayerApp.Helpers;

/// <summary>
/// Lets a handler be attached to a native view once. Keyed weakly by reference so a
/// recycled or collected native view leaves no strong reference behind.
/// </summary>
public sealed class NativeAttachGuard
{
    private readonly ConditionalWeakTable<object, object> _marked = new();

    public bool TryMark(object nativeView) => _marked.TryAdd(nativeView, nativeView);
}
