# Changelog

## 0.1.4

- **Fixed a crash on every Android launch** (found and fixed by a customer integrating
  this SDK into a production game, not by this repo's own testing). The 0.1.2 fix for
  the main-thread-blocking issue below moved `AdvertisingId()`/`AppSetId()` onto a
  `Task.Run` background thread — but a `.NET` ThreadPool thread is not attached to the
  JVM, and any `AndroidJavaObject` call from an unattached thread aborts the process
  (`SIGABRT`), which no C# `try`/`catch` can intercept. Fixed by wrapping the JNI work
  in each background task with `AndroidJNI.AttachCurrentThread()` /
  `DetachCurrentThread()`, the standard Unity pattern for calling into Android from a
  thread Unity didn't create. This repo's own device testing (Mono scripting backend)
  never hit this; it's unclear whether that's luck, a Mono-vs-IL2CPP difference, or
  device-specific — the fix is correct regardless of why our own pass missed it.
- **Fixed `Roas.Initialize`/`Identify`/`ReportFirstOpen` sending `external_id: ""`**
  (same report): a `RoasSettings` asset serializes an *unset* string field as `""`,
  never `null`, so `customerUserId != null` was true even for a blank field — sending
  an empty `external_id` the collector rejects with HTTP 400 on every launch after the
  first. Fixed by checking `!string.IsNullOrEmpty(customerUserId)` in all three places
  instead.

## 0.1.3

- **Fixed a real delivery-ordering bug** in `RoasTransport`: `Send()` enqueues a beacon
  unconditionally and then calls `Flush()`, but `Flush()` is a no-op while a previous flush
  is already running (`if (_flushing) return;`), and that earlier flush only ever processes
  the queue snapshot it took when it started. Confirmed on a real iOS device: a deep link's
  beacon, enqueued while the ordinary session-resume `identify()` calls were already
  mid-flush, sat in the persisted queue for an entire extra app background/foreground cycle
  before anything unrelated happened to call `Send()`/`Flush()` again and finally pushed it
  out. `FlushCoroutine` now loops until a pass neither delivers anything nor finds the queue
  empty, instead of a single pass, so nothing enqueued mid-flush is left stranded.
- Real OS-level deep link delivery (a genuine tap on a registered URL scheme, not a direct
  `HandleDeepLink()` call) is now verified end-to-end on both Android and iOS, confirmed by
  reading the resulting `TouchPoint` rows from the backend, not just a device log line. This
  had never actually been exercised before: every previous device build this project did
  packaged the wrong (blank, auto-generated) scene -- see the TestProject-only
  `RoasBuildScript` fix below -- so `RoasSmokeTest`'s code, deep-link handling included, was
  never running on-device at all until now, on either platform.

### TestProject (not part of the published package)

- Fixed `RoasBuildScript.ScenePath` pointing at a path that never existed
  (`Assets/Scenes/SmokeTest.unity`); `EnsureScene()` silently auto-created a blank scene
  there on every build, so every Android and iOS device build this project ever made
  packaged that blank scene instead of the real one with `SmokeTest` (`RoasSmokeTest` +
  `RoasPurchaseTest`) attached. Found by noticing `RoasSmokeTest`'s own baseline log line
  had never once appeared in any on-device log this project produced, despite working
  correctly every time in Editor Play Mode (which uses whatever scene is actually open,
  bypassing this constant entirely).

## 0.1.2

- **Fixed a real production-safety bug**: Android's `RoasAndroidBridge.AdvertisingId()` and
  `AppSetId()` were being called synchronously on Unity's main thread during `Initialize()`
  and `Identify()`. Both make a blocking Binder IPC call to Play Services — `AppSetId()`
  specifically has its own 5-second internal timeout — so on a slow device this risked a
  startup hitch or, in the worst case, brushing against Android's ANR threshold. The native
  Kotlin SDK has always backgrounded this for exactly this reason; the Unity port never got
  that treatment until now. Fixed by adding a thread-safe main-thread dispatcher
  (`RoasRuntime.RunOnMainThread`) and running both native reads on a background
  `System.Threading.Tasks.Task` before hopping back to the main thread to build and send the
  beacon (PlayerPrefs/UnityWebRequest/StartCoroutine all require the main thread, so only the
  blocking native calls themselves move off it). Found by direct code review when asked
  point-blank whether the SDK was safe to integrate into a real production game — not found
  by testing, since neither device used in this session's verification pass was slow enough
  to make the hitch obvious.
- iOS's equivalent native reads (IDFA/IDFV/Apple Search Ads token) were left as-is: they are
  documented as fast, local, non-network calls with no comparable blocking risk, unlike
  Android's App Set Id.

## 0.1.1

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
