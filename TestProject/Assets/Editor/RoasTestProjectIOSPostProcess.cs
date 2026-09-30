#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// <summary>
/// TestProject-only (NOT part of the published UnityPackage/): sets a default
/// NSUserTrackingUsageDescription in the exported Info.plist if one isn't already there.
///
/// RoasSettings.requestTrackingAuthorizationOnLaunch defaults to true, so the ATT prompt fires
/// automatically on first launch -- and calling ATTrackingManager without this Info.plist key
/// present is exactly the kind of thing that only surfaces on a REAL device, not in the Simulator
/// build this SDK was first checked against. This wording is a placeholder for local testing
/// only; a real app must write its own -- see the main README, which deliberately does NOT
/// auto-inject this for real consumers of the package.
/// </summary>
internal sealed class RoasTestProjectIOSPostProcess : IPostprocessBuildWithReport
{
    private const string UsageDescriptionKey = "NSUserTrackingUsageDescription";
    private const string PlaceholderText =
        "This helps us measure which ad brought you here, so we can keep improving the app.";

    // After the package's own RoasIOSPostProcessBuild (callbackOrder 0), since both touch the
    // same exported project and order between unrelated postprocess steps shouldn't matter here,
    // but keeping it explicit avoids ever having to wonder.
    public int callbackOrder => 1;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;

        var plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        if (plist.root.values.ContainsKey(UsageDescriptionKey)) return; // don't override a real one

        plist.root.SetString(UsageDescriptionKey, PlaceholderText);
        plist.WriteToFile(plistPath);
        Debug.Log($"[RoasTestProjectIOSPostProcess] Set a placeholder {UsageDescriptionKey} for local " +
                  "testing -- write your own wording before shipping anything real.");
    }
}
#endif
