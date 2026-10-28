namespace PrayerApp.Services;

public interface IOrientationService
{
    void LockLandscape();
    void LockPortrait();
    void Unlock();
    void ApplyDefault();
}
