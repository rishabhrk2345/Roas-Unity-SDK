using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Headless build entry points for TestProject -- run these via <c>-batchmode -executeMethod</c>
/// so a build never needs a rendering GPU/display at all, which sidesteps environments (VMs,
/// remote-desktop sessions) where the interactive Editor window fails to render but the Editor
/// itself runs fine. Creates a throwaway scene on demand so the build never fails on "scene
/// list is empty" -- this project ships with none checked in.
/// </summary>
public static class RoasBuildScript
{
    private const string ScenePath = "Assets/Scenes/SmokeTest.unity";

    /// <summary>
    /// <code>
    /// /Applications/Unity/Hub/Editor/2022.3.50f1/Unity.app/Contents/MacOS/Unity \
    ///   -batchmode -quit -nographics -projectPath ~/Roas-Unity-SDK/TestProject \
    ///   -executeMethod RoasBuildScript.BuildIOS -logFile ~/roas-ios-build.log
    /// </code>
    /// Output lands at <c>~/roas-ios-build/</c> (a sibling of TestProject, not inside the repo).
    /// Open the generated <c>.xcodeproj</c> there once this finishes -- check
    /// <c>~/roas-ios-build.log</c> if it doesn't.
    /// </summary>
    /// <summary>The generic default bundle id ("com.DefaultCompany.TestProject") cannot be
    /// registered to any real Apple Developer team -- confirmed on a real attempt ("app
    /// identifier ... not available"), since it collides with whatever the countless other
    /// default Unity projects out there have already registered. This is what
    /// <see cref="EnsureIOSBundleId"/> replaces it with -- unique enough to not collide.</summary>
    private const string TestBundleId = "com.rishabhrk2345.roasunitysdktest";

    public static void BuildIOS() => Build(BuildTarget.iOS, "../../../roas-ios-build");

    /// <summary>Produces a .aab (Android App Bundle) -- the Play Store submission format.
    /// NOT directly installable via `adb install`; use <see cref="BuildAndroidApk"/> for
    /// sideloading onto a physical device/emulator during testing. Requires the Android
    /// SDK/NDK/JDK to already be configured in this Editor's Preferences (Unity normally
    /// prompts to auto-install these on first Android switch, which needs the GUI once --
    /// batchmode can't do that first-time setup for you).</summary>
    public static void BuildAndroid() => Build(BuildTarget.Android, "../../../roas-android-build/app.aab", buildAppBundle: true);

    /// <summary>
    /// Produces a plain, directly-installable .apk -- what you actually want for
    /// `adb install` onto a physical device or emulator. Same prerequisites as
    /// <see cref="BuildAndroid"/>.
    /// <code>
    /// /Applications/Unity/Hub/Editor/2022.3.50f1/Unity.app/Contents/MacOS/Unity \
    ///   -batchmode -quit -nographics -projectPath ~/Roas-Unity-SDK/TestProject \
    ///   -executeMethod RoasBuildScript.BuildAndroidApk -logFile ~/roas-android-apk-build.log
    /// </code>
    /// </summary>
    public static void BuildAndroidApk() => Build(BuildTarget.Android, "../../../roas-android-build/app.apk", buildAppBundle: false);

    private static void Build(BuildTarget target, string relativeOutputPath, bool buildAppBundle = false)
    {
        EnsureScene();
        var outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, relativeOutputPath));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? outputPath);

        if (target == BuildTarget.Android)
        {
            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
            EnsureAndroidPlayServicesDependencies();
        }
        else if (target == BuildTarget.iOS)
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.iOS, TestBundleId);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = target,
            options = BuildOptions.None,
        });

        Debug.Log($"[RoasBuildScript] {target} build -> {report.summary.result} at {outputPath}");
        if (report.summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1); // non-zero exit so a CI/terminal caller can tell it failed
        }
    }

    /// <summary>
    /// Patches Play Services dependencies directly into `mainTemplate.gradle`'s `**DEPS**`
    /// placeholder, bypassing EDM4U's own Gradle-template patch step.
    ///
    /// EDM4U's "Resolve" menu action reliably completed its FIRST phase (copying
    /// mainTemplate.gradle/gradleTemplate.properties from the Editor's template folder and
    /// enabling custom Gradle templates) but never reliably completed the SECOND phase (actually
    /// replacing `**DEPS**` with our package's `Editor/RoasSensorDependencies.xml` coordinates)
    /// across several repeated manual clicks in this environment -- confirmed by re-reading the
    /// patched file after each attempt rather than trusting the menu action's own "done" state.
    /// Since the actual dependency coordinates are simple, fixed, and already declared once in
    /// this package's `RoasSensorDependencies.xml`, doing it here directly is far more reliable
    /// than depending on EDM4U's async multi-phase resolve job completing correctly headlessly.
    ///
    /// Idempotent: a build re-run after this has already patched the file is a no-op. Keep this
    /// list in sync with `UnityPackage/Editor/RoasSensorDependencies.xml` by hand -- there is no
    /// single source of truth once EDM4U's own resolution is bypassed like this.
    /// </summary>
    private static void EnsureAndroidPlayServicesDependencies()
    {
        const string templatePath = "Assets/Plugins/Android/mainTemplate.gradle";
        if (!File.Exists(templatePath))
        {
            Debug.LogWarning($"[RoasBuildScript] {templatePath} does not exist yet -- run Assets > " +
                "External Dependency Manager > Android Resolver > Resolve once first (it creates this " +
                "file even if it doesn't finish patching it), then re-run this build.");
            return;
        }

        var content = File.ReadAllText(templatePath);
        if (content.Contains("play-services-ads-identifier"))
        {
            return; // already patched on a previous build -- nothing to do
        }

        const string marker = "**DEPS**";
        if (!content.Contains(marker))
        {
            Debug.LogWarning($"[RoasBuildScript] {templatePath} has no {marker} placeholder -- " +
                "can't patch it automatically. Add the dependencies to it by hand.");
            return;
        }

        const string patchedMarker =
            "**DEPS**\n" +
            "    implementation 'com.google.android.gms:play-services-ads-identifier:18.1.0'\n" +
            "    implementation 'com.google.android.gms:play-services-appset:16.0.2'\n" +
            "    implementation 'com.android.installreferrer:installreferrer:2.2'\n";

        File.WriteAllText(templatePath, content.Replace(marker, patchedMarker));
        AssetDatabase.Refresh();
        Debug.Log($"[RoasBuildScript] Patched {templatePath} with Play Services dependencies directly.");
    }

    private static void EnsureScene()
    {
        if (File.Exists(ScenePath)) return;
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
