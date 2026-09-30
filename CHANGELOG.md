# Changelog

## Unreleased

- Fixed `RoasDeviceIntegrity` referencing the nonexistent `RuntimePlatform.IPhoneSimulator`
  (CS0117) — Unity has no separate enum value for the Simulator; folded into a
  `SystemInfo.deviceModel` heuristic instead. Found via a real device build.
- Fixed `RoasBuildScript`'s output path resolving one directory too high
  (`Path.Combine` double-applying `..`).
- Build-verified on iOS: `RoasNative.mm` and the framework auto-linking postprocess step both
  compile and link successfully in a real Xcode project exported from Unity.
- Build-verified on Android: `RoasAndroidBridge.cs` (previously compiled on no target at all)
  compiles cleanly and produces a working `.aab` via `RoasBuildScript.BuildAndroid()`.
- Runtime-verified against a real backend on a real Android device: install/session/identify/
  track/deep-link all confirmed correct by reading actual DB rows; real GAID and App Set Id
  hashes confirmed landing correctly via Play Services reflection. Install Referrer correctly
  reports unavailable on a sideloaded build (expected -- no Play Store install to source it from).
- Fixed `RoasSensorDependencies.xml` containing illegal `--` sequences inside its XML comment,
  which silently broke EDM4U's parser (no build-time error, just missing dependencies at
  runtime) -- found by decompiling a built APK's dex and finding none of the expected classes.
- Fixed EDM4U's Android Resolver aborting entirely with `DirectoryNotFoundException` when
  `Assets/Plugins/Android` didn't already exist; that folder is now pre-created.
- Worked around EDM4U's "Resolve" menu reliably enabling Gradle templates but not reliably
  patching `**DEPS**` with dependency coordinates across repeated attempts -- `RoasBuildScript`
  now patches it directly as a safety net.
- Fixed a missing iOS app icon causing a hard build failure on newer Xcode (26+); the build
  script now generates a placeholder icon automatically.
- Runtime-verified on real iOS hardware: install/session/deferred-link/identify all confirmed
  delivered; real IDFV, real Apple Search Ads status, and real IDFA (hashed) all confirmed
  landing correctly, with the identity graph correctly merging all three into one identity.

## 0.1.0

Initial release. Unity port of the ROASSensor Android/iOS SDK contract:

- Install reporting, session tracking (30-min idle + local-midnight, on-device sequencing,
  foreground-time accumulation), identity binding, PII hashing (email/phone, byte-parity with
  the backend and every other SDK), funnel events, fast purchase verification, deep-link
  forwarding.
- HMAC-SHA256 beacon signing with server-clock-offset self-correction.
- Offline-durable delivery queue (`PlayerPrefs`-backed).
- Android: advertising id (GAID), App Set Id, Play Install Referrer, root/emulator signals.
- iOS: IDFA, IDFV, App Tracking Transparency, Apple Search Ads token, SKAdNetwork /
  AdAttributionKit conversion values, jailbreak signals.
- Editor tooling: settings asset creation, automatic Xcode framework linking, EDM4U Android
  dependency resolution.

Not yet ported: Vivo/Huawei/Xiaomi/Samsung OEM install-referrer fallbacks.
