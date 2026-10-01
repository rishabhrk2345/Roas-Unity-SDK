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
https://github.com/rishabhrk2345/Roas-Unity-SDK.git?path=UnityPackage
```

Or add directly to `Packages/manifest.json`:

```json
"com.roassensor.unity-sdk": "https://github.com/rishabhrk2345/Roas-Unity-SDK.git?path=UnityPackage"
```

(the package itself lives in the repo's `UnityPackage/` subfolder — `?path=` is UPM's standard
way of pointing a git dependency at a subdirectory of a monorepo, needed here because
`TestProject/` sits alongside it in the same repo for local build-testing; see "Repo layout"
below.)

### Android

Player Settings → the SDK reads Play Services classes by reflection
(`AndroidJavaObject`), so nothing to add there — but the *host project's* Gradle build must
resolve the advertising id, App Set Id and Install Referrer Maven artifacts for those signals
to be anything but silently absent. If your project has
[External Dependency Manager for Unity](https://github.com/googlesamples/unity-jar-resolver)
(EDM4U) installed, this package's `Editor/RoasSensorDependencies.xml` is picked up automatically
and resolves them for you. The easiest way to add EDM4U is via its OpenUPM scoped registry —
add this to `Packages/manifest.json` (see `TestProject/Packages/manifest.json` in this repo for
a working example):

```json
"scopedRegistries": [
  { "name": "package.openupm.com", "url": "https://package.openupm.com",
    "scopes": ["com.google.external-dependency-manager"] }
],
"dependencies": {
  "com.google.external-dependency-manager": "1.2.189"
}
```

Without EDM4U, add the three `androidPackage` lines in that file to your own Gradle template by
hand. Every read is guarded — a missing dependency degrades to "no signal", never a crash
(confirmed on a real device: `device_id`/`app_set_id`/`install_referrer` all read empty with
`referrer_status=UNAVAILABLE`, no crash, before EDM4U was added).

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

// iOS: ask for tracking permission at a moment of your choosing (never at cold start)
Roas.RequestTrackingAuthorization();
```

## Deep links: two halves, both required

`Roas.HandleDeepLink(url)` only parses a URL you hand it — getting that URL out of Unity needs
**both** of these, not just one, confirmed on real Android and iOS hardware:

```csharp
private void Start()
{
    // Cold start: if THIS launch opened the app (an ad click, a share link, the app wasn't
    // already running), Application.absoluteURL is already populated by the time Start()
    // runs -- empty otherwise. Skip this and the single most common deep link, the one that
    // launches the app for the first time, is silently never forwarded.
    if (!string.IsNullOrEmpty(Application.absoluteURL))
        Roas.HandleDeepLink(Application.absoluteURL);
}

private void OnEnable() => Application.deepLinkActivated += Roas.HandleDeepLink;
private void OnDisable() => Application.deepLinkActivated -= Roas.HandleDeepLink;
```

`Application.deepLinkActivated` covers a warm reopen (the app was already running); it never
fires for a cold start, which is why the `Application.absoluteURL` check above is not optional.
See `Samples~/BasicIntegration/RoasSampleUsage.cs` for both wired together in one component.

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

**Build-verified on both platforms** (2026-09-29), via `RoasBuildScript`'s headless
`-batchmode -executeMethod` builds:
- **iOS**: the generated Xcode project compiles and links successfully for the iOS Simulator
  (`RoasNative.mm` + the `RoasIOSPostProcessBuild.cs` framework auto-linking both confirmed
  working) — this was the one part of the SDK that could not be checked without a real Xcode
  toolchain.
- **Android**: `RoasAndroidBridge.cs` (GAID / App Set Id / Install Referrer via
  `AndroidJavaObject`/`AndroidJavaProxy`) compiles cleanly and produces a working `.aab` —
  this file is entirely excluded (`#if UNITY_ANDROID`) on every other target, so this was its
  first real compile too.

**Runtime-verified against a real backend** (2026-09-30), on a real Android device (not just a
compile check):
- Install reporting, session tracking, `Identify` (email hash matched a manually-computed
  SHA-256), `Track`, and `HandleDeepLink` called directly with a URL (full query string
  forwarded intact) all confirmed by reading the actual database rows they produced, not just
  trusting a 200 response. **This did not yet test a real OS-level deep link tap** — see
  2026-10-01 below for that.
- Real advertising id (GAID) and App Set Id confirmed reading correctly via
  `AndroidJavaObject`/`AndroidJavaProxy` reflection into Play Services — both landed as real,
  non-empty hashes in the backend.
- Play Install Referrer correctly reports unavailable on a sideloaded (`adb install`) build,
  since Install Referrer only has data for a Play Store install — this is expected, not a bug,
  and remains unverified pending a real Play Store (internal testing track) install.
- **iOS runtime-verified too** (2026-09-30), on real iPhone hardware: install/session reporting
  (`HTTP 201`), deferred-link probe (`HTTP 200`), and `Identify` (`HTTP 200`) all confirmed
  delivered. Real IDFV (`28510405-D3F1-4590-A91C-5704DEF99962`, sent raw per spec — never
  hashed), real Apple Search Ads status (`referrer_status=OK`, `referrer_source=asa` — the
  AdServices API call genuinely succeeded), and real IDFA (confirmed via a 64-char SHA-256 hash
  in the identity graph) all landed correctly — and the backend correctly merged all three
  (IDFA + IDFV + vid) into a single identity, not three separate ones.

**Real OS-level deep link verified end-to-end** (2026-10-01), a genuine tap on a registered URL
scheme — not a direct `HandleDeepLink()` call — confirmed on real Android **and** iOS hardware,
by reading the resulting backend rows, both cold-start (`Application.absoluteURL`) and warm
reopen (`Application.deepLinkActivated`). This had never actually been exercised before: every
prior device build this project made packaged the wrong, auto-generated blank test scene (see
`TestProject/CHANGELOG`-equivalent notes in the SDK's own `CHANGELOG.md` 0.1.3 entry), so none
of the test harness's code — deep link handling included — had ever genuinely run on a device
until this fix. Also found and fixed in the same pass: a real race in `RoasTransport` where a
beacon enqueued while another flush was already running could sit stuck in the offline queue
for a full extra app cycle. See `CHANGELOG.md` 0.1.3 for both.

**A real EDM4U bug/quirk was found and worked around** during this pass: EDM4U's "Resolve" menu
action reliably completed enabling custom Gradle templates (copying `mainTemplate.gradle`) but
did not reliably complete patching `**DEPS**` with the actual dependency coordinates, across
several repeated manual attempts. `TestProject/Assets/Editor/RoasBuildScript.cs` now patches
`**DEPS**` directly and idempotently as a safety net, rather than depending solely on EDM4U's
async resolve job completing correctly. If you integrate this package into your own project and
EDM4U's Resolve isn't populating your Gradle template either, check `**DEPS**` by hand the same
way.

## Repo layout

The package itself lives in `UnityPackage/` (Runtime, Editor, Tests, Samples~, package.json) —
**not** at the repo root — specifically so `TestProject/`, which sits beside it in the same
repo for convenience, is never mistaken for part of the package's own content. Unity's `file:`
package resolution copies everything under the referenced folder into its package cache; earlier
versions of this repo had the package at the repo root with `TestProject/` nested *inside* it,
which meant `TestProject`'s own `Library`/`Temp` folders got dragged into the package cache too
and produced "Library folder embedded in an asset folder" errors the moment `TestProject` was
opened. Splitting them into sibling folders (`UnityPackage/` and `TestProject/`) fixes that for
good — if you're pulling an older clone and hit that error, re-clone or `git pull` to pick up
this layout.

## License

See [LICENSE](LICENSE).
