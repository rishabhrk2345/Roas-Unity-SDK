# TestProject

A minimal Unity project scaffold for building and smoke-testing the ROASSensor SDK on a real
device/simulator with an actual Unity Editor and Xcode/Android SDK available — neither of which
exists in the environment this SDK was originally written in, so none of this has been
build-verified yet. Use this to close that gap.

The SDK is referenced from `Packages/manifest.json` as a **local file path**
(`"com.roassensor.unity-sdk": "file:../../UnityPackage"`) pointing at the `UnityPackage/`
folder that sits beside `TestProject/` in this same repo — so editing SDK source and
re-opening/re-focusing the Editor picks up changes immediately, no publish/re-clone step needed.

Note it points at `UnityPackage/`, a **sibling** of this folder, not a parent of it. `TestProject`
used to live *inside* the package root itself, which broke Unity's package cache the moment this
project was opened (its own `Library`/`Temp` got copied in as if they were package content) —
see the main README's "Repo layout" section.

## Opening it

1. Install Unity via Unity Hub. `ProjectSettings/ProjectVersion.txt` names `2022.3.50f1`
   (an LTS) but any reasonably recent 2021.3+ Editor works — Unity will just offer to
   upgrade the project on open, which is fine to accept.
2. Unity Hub → Open → select this `TestProject` folder (not the repo root).
3. First open will take a while: Unity needs to import the package and resolve the module
   list in `manifest.json`.
4. `ROASSensor` menu → **Create Settings Asset**. Fill in a real public key (Android and/or
   iOS) from your ROASSensor site's setup page. This file is gitignored on purpose — it's for
   your local testing only, never commit a real key/secret here.
5. Open (or create, via File → New Scene → save as `Assets/Scenes/SmokeTest.unity`) a scene,
   add an empty GameObject, attach `RoasSmokeTest` (`Assets/Scripts/RoasSmokeTest.cs`). Leave
   its `Public Key` field blank to use the settings asset from step 4, or paste one directly.

## If the Editor window won't render (remote desktop / VM / headless host)

Some remote-display setups fail to render Unity's Metal-accelerated Editor UI at all — a solid
black or magenta window is that, not a project problem, and there is no code fix for it here.
Rather than fight it, build headless from the command line, which never opens a window:

```bash
/Applications/Unity/Hub/Editor/2022.3.50f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit -nographics -projectPath ~/Roas-Unity-SDK/TestProject \
  -executeMethod RoasBuildScript.BuildIOS -logFile ~/roas-ios-build.log
```

`Assets/Editor/RoasBuildScript.cs` creates a throwaway scene on demand (this project ships with
none checked in) and calls `BuildPipeline.BuildPlayer` targeting iOS, writing the Xcode project
to `~/roas-ios-build/` (outside the repo). Tail the log while it runs:

```bash
tail -f ~/roas-ios-build.log
```

The same script has `BuildAndroid()` for an Android `.aab`, though the Android SDK/NDK/JDK need
to already be configured in Unity's Preferences first (a one-time GUI step Unity can't skip —
if the window genuinely can't render at all, Android will need the working-GUI path once before
this shortcut helps).

You still need the settings asset from step 4 above for `Roas.Initialize` to have a public key —
create it via the `ROASSensor` menu if the GUI renders at all (even a broken-looking window
still accepts menu clicks in most cases; the corruption tends to be confined to the custom-drawn
panel content, not the native macOS menu bar), or hand-edit
`Assets/Resources/RoasSettings.asset` after running `ROASSensor → Create Settings Asset` once
to see its shape.

## What to actually check with Xcode available

1. **Switch Platform → iOS** (File → Build Settings), then **Build** (not Build & Run is fine
   too) to a folder outside this repo — or use the headless command above if the Build Settings
   window itself won't render.
2. Open the generated `.xcodeproj`/`.xcworkspace` and confirm:
   - `RoasNative.mm` compiles with no errors (this is the one file in the whole SDK that
     could not be syntax-checked without Xcode).
   - The `UnityFramework` (or main) target's **Frameworks and Libraries** list includes
     `AdSupport.framework`, `AppTrackingTransparency.framework`, `AdServices.framework`,
     `StoreKit.framework` — confirms `Editor/RoasIOSPostProcessBuild.cs` ran.
3. Player Settings → iOS → Other Settings → set **Privacy - Tracking Usage Description**
   (required or the ATT prompt crashes the app on real iOS 14+ — this is the one manual step
   the SDK can't do for you).
4. Run on a device or simulator, watch the console for `[RoasSensor]`/`[RoasSmokeTest]` log
   lines, tap through the `RoasSmokeTest` methods (wire them to uGUI buttons, or call them
   from the Inspector's context menu / a debug console), confirm:
   - The ATT prompt appears once and `Roas.AppAccountToken()` returns a UUID-shaped string
     after `Initialize` (it's derived from the visitor id, so it should never be null once
     initialized).
   - `Roas.OnDeliveryResult` fires for each beacon — check what HTTP status comes back
     (a real site's public key + a reachable collector should read `success=true`; a bad/test
     key will 4xx, which is still a useful signal that the request shape itself is fine).
5. Also try an **Android** build if you have the SDK/NDK on that machine — `RoasAndroidBridge`
   needs Play Services artifacts resolved (see the main README's Android install section) or
   the advertising id / App Set Id / install referrer calls will just log "no signal", not crash.

## Reporting back

If `RoasNative.mm` fails to compile, or the postprocess build step throws, paste the exact
Xcode/Unity console error — that's the one class of bug this SDK could not have been checked
for without the toolchain this project exists to provide.
