using RoasSensor;
using UnityEngine;

/// <summary>
/// Test-project hook for <see cref="Roas.SetGoogleConsent"/>: relays a fixed answer (data to Google
/// allowed, personalisation refused) before the SDK starts, so a device run can check that BOTH flags
/// ride on the very first beacon and that a refusal arrives as <c>false</c>, not as an absent key.
/// SubsystemRegistration runs before any BeforeSceneLoad hook, so it precedes the SDK's own auto-initialise;
/// the call is safe before Initialize by design (it is held and applied when storage exists).
/// </summary>
public static class RoasConsentTest
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RelayConsent()
    {
        Roas.SetGoogleConsent(adUserData: true, adPersonalization: false);
    }
}
