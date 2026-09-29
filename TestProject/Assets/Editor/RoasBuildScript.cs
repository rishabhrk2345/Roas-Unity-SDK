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

    private static void EnsureScene()
    {
        if (File.Exists(ScenePath)) return;
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, ScenePath);
    }
}
