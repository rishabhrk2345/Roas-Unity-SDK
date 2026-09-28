# ROASSensor SDK for Unity

Attribution and revenue tracking for Unity games and apps — installs, sessions, identity,
funnel events and fast purchase verification against the ROASSensor collector. This is a
from-scratch Unity port of the same wire contract the native
[Android (Kotlin)](https://github.com/harsh-vasundhara/roas-android-sdk) and
[iOS (Swift)](https://github.com/rishabhrk2345/Roas-ios-SDK) ROASSensor SDKs speak — same
endpoints, same session rules, same HMAC signing, same PII hashing. A customer comparing a
native app and a Unity game in one dashboard sees one consistent definition of a session and
an install, not three.

## Install

Package Manager → Add package from git URL:

```
https://github.com/rishabhrk2345/Roas-Unity-SDK.git
```

Or add directly to `Packages/manifest.json`:

```json
"com.roassensor.unity-sdk": "https://github.com/rishabhrk2345/Roas-Unity-SDK.git"
```

### Android

Player Settings → the SDK reads Play Services classes by reflection
(`AndroidJavaObject`), so nothing to add there — but the *host project's* Gradle build must
resolve the advertising id, App Set Id and Install Referrer Maven artifacts for those signals
to be anything but silently absent. If your project has
[External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver)
installed, this package's `Editor/RoasSensorDependencies.xml` is picked up automatically and
resolves them for you. Without EDM4U, add the three `androidPackage` lines in that file to
your own Gradle template by hand. Every read is guarded — a missing dependency degrades to
"no signal", never a crash.

### iOS

A post-build step (`Editor/RoasIOSPostProcessBuild.cs`) links `AdSupport`,
`AppTrackingTransparency`, `AdServices` and `StoreKit` into the generated Xcode project
automatically. The one thing you must still do yourself: set **Privacy - Tracking Usage
Description** in Player Settings → iOS → Other Settings (its wording is a product/legal
decision this package can't make for you) — iOS terminates the app on the ATT prompt without it.

## Setup

`ROASSensor → Create Settings Asset` (menu) creates `Assets/Resources/RoasSettings.asset`.
Fill in the public key (and, optionally, the beacon-signing secret) for whichever
platforms you ship — **Android and iOS are separate app records with separate keys, even
for "the same app"**, because they're two different store listings. Leave
`Auto Initialize` on and the SDK starts itself as early as possible; turn it off to call
`Roas.Initialize(...)` yourself.

```csharp
using RoasSensor;

// Manual init (skip this if RoasSettings.autoInitialize is on)
Roas.Initialize("YOUR-SITE-PUBLIC-KEY");

// When the user is known
Roas.Identify(email: "buyer@example.com");

// Pass the visitor id to RevenueCat / your billing SDK so a purchase attributes to this install
// Purchases.SharedInstance.LogIn(Roas.VisitorId());

// A funnel event -- NEVER revenue, see below
Roas.Track(RoasEvent.AddToCart, properties: new Dictionary<string, object> {
    { RoasProps.ProductId, "starter_pack" },
    { RoasProps.Price, 499 }, // minor units -- reporting colour only
    { RoasProps.Currency, "USD" },
});

// Ask the collector to verify a purchase immediately, instead of waiting on the store's
// async server notification (RTDN / App Store Server Notifications)
Roas.VerifyPurchase(purchaseToken: token, productId: sku, isSubscription: false); // Android
Roas.VerifyPurchase(transactionId: transactionId);                                // iOS

// Forward a deep/universal link that reopened an already-installed app
Roas.HandleDeepLink(url);

// iOS: ask for tracking permission at a moment of your choosing (never at cold start)
Roas.RequestTrackingAuthorization();
```

## Purchase attribution: two different platform APIs, on purpose

Android's Play Billing `obfuscatedAccountId` accepts any string, so `Roas.ObfuscatedAccountId()`
is just the visitor id unchanged. **StoreKit's `appAccountToken` requires a real `UUID`** — passing
the Android-style `"rs..."` string on iOS **fails silently**; StoreKit just drops it and the
purchase attributes to nothing. Use `Roas.AppAccountToken()` (iOS only, returns a UUID string) for
that path instead. There is no shared "purchase id" helper on purpose — the platforms are not
interchangeable here.

## Revenue never comes from the client

`Roas.Track` and `Roas.VerifyPurchase` never assert an amount — a game binary can be tampered
with, so no ROAS numerator can trust a client-reported price. `VerifyPurchase` only *names* a
receipt; the collector calls the Play Developer API / App Store Server API itself and books
whatever amount **the store** reports back. The primary revenue path is still the store-to-server
webhook (RevenueCat / App Store Server Notifications / Play RTDN) configured on your ROASSensor
site — `VerifyPurchase` only closes the gap between "the player just paid" and "the async
notification eventually lands," which can be minutes.

## What's ported, what isn't (yet)

Ported: install reporting, session tracking (30-minute idle + local-midnight boundary, on-device
sequencing, foreground-time accumulation), identity binding + PII hashing, funnel events, fast
purchase verification, deep-link forwarding, Android GAID/App Set Id/Play Install Referrer, iOS
IDFA/IDFV/ATT/Apple Search Ads token/SKAdNetwork conversion values, HMAC beacon signing with
clock-skew self-correction, an offline-durable delivery queue.

Not yet ported from the native SDKs: OEM install-referrer fallbacks for Vivo/Huawei/Xiaomi/Samsung
(mainland-China-adjacent Android markets where Play's referrer is unavailable) — these are
Android-market-specific and left for a follow-up PR rather than blocking a first release.

## Session model

Mirrors every other ROASSensor SDK exactly, and must not drift:

- A session ends after **30 minutes of inactivity**.
- A session also ends at the visitor's **local midnight** (device timezone, not UTC).
- `sequence` (an event's index within its session) is assigned **on the device**, never by the
  server — beacons are fire-and-forget and race each other, so arrival order isn't event order.
- Session state is persisted (`PlayerPrefs`), not held in memory, since Unity's own domain
  reloads and a mobile OS backgrounding/killing the process both routinely tear down anything
  RAM-only — a session that lived only in memory would restart every time, inflating counts and
  destroying the retention numbers sessions exist to produce.

## Testing

`Tests/Editor` has NUnit parity tests for hashing, HMAC signing and session-rollover behaviour —
open via Window → General → Test Runner → EditMode.

`TestProject/` is a minimal Unity project scaffold (referencing this package by local path) for
building on a real device with Xcode/an Android SDK available — see `TestProject/README.md`.
This SDK has not yet been build-verified in an actual Unity Editor; treat a first integration
as needing that pass.

## License

See [LICENSE](LICENSE).
