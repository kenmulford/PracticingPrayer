namespace PrayerApp.Helpers;

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
}
