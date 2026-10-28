namespace PrayerApp.Helpers;

/// <summary>
/// First result wins: a popup's second button tap is ignored while its close is in flight,
/// so a double-tap cannot overwrite the result. A refused close clears the result and
/// re-arms the gate so the user can retry. Single-threaded UI dispatcher only.
/// </summary>
public sealed class PopupResultGate
{
    private bool _begun;

    public bool TryBegin()
    {
        if (_begun) return false;
        _begun = true;
        return true;
    }

    public void Reset() => _begun = false;

    public async Task CompleteAsync(Func<Task<bool>> close, Action setResult, Action? clearResult = null)
    {
        if (!TryBegin()) return;
        setResult();
        if (!await close())
        {
            clearResult?.Invoke();
            Reset();
        }
    }
}
