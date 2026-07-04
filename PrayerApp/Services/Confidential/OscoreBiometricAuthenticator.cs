using Maui.Biometric;

namespace PrayerApp.Services.Confidential;

/// <summary>
/// Real <see cref="IBiometricAuthenticator"/> — wraps Oscore.Maui.Biometric v2.5.1
/// (namespace <c>Maui.Biometric</c>; NuGet PackageId prefixes the assembly name with
/// "Oscore."). <see cref="IBiometricAuthentication.AuthenticateAsync"/> already performs its
/// own availability check internally (sensor present, enrolled, OS support) and returns a
/// non-success <see cref="AuthenticationStatus"/> for every unavailable/failed/canceled/
/// too-many-attempts/denied outcome, so this seam just needs to read
/// <see cref="AuthenticationResult.IsSuccessful"/> — every other case is a PIN-fallback signal.
/// </summary>
public class OscoreBiometricAuthenticator : IBiometricAuthenticator
{
    private readonly IBiometricAuthentication _biometric;

    public OscoreBiometricAuthenticator(IBiometricAuthentication biometric)
    {
        _biometric = biometric;
    }

    public async Task<bool> AuthenticateAsync(string reason)
    {
        var request = new AuthenticationRequest("Confidential Cards", reason);
        var result = await _biometric.AuthenticateAsync(request);
        return result.IsSuccessful;
    }
}
