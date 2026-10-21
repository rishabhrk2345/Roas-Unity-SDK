#if UNITY_IOS
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace RoasSensor.Editor
{
    /// <summary>
    /// Links the frameworks <c>Runtime/Plugins/iOS/RoasNative.mm</c> needs
    /// (AdSupport, AppTrackingTransparency, AdServices, StoreKit) into the generated Xcode
    /// project and strips the In-App Purchase capability that <c>com.unity.purchasing</c>
    /// adds automatically.
    ///
    /// callbackOrder 100 — runs after Unity IAP's own post-process (which rewrites the
    /// pbxproj at a lower order), so our framework additions are not overwritten.
    /// </summary>
    internal sealed class RoasIOSPostProcessBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            var pbxPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            var project = new PBXProject();
            project.ReadFromFile(pbxPath);

            var targetGuid = project.GetUnityFrameworkTargetGuid();

            project.AddFrameworkToProject(targetGuid, "AdSupport.framework", true);
            project.AddFrameworkToProject(targetGuid, "AppTrackingTransparency.framework", true);
            project.AddFrameworkToProject(targetGuid, "AdServices.framework", true);
            project.AddFrameworkToProject(targetGuid, "StoreKit.framework", false);

            project.WriteToFile(pbxPath);

            // com.unity.purchasing adds the In-App Purchase capability to every target's
            // systemCapabilities block. A free Personal Team cannot sign an app with IAP,
            // and this SDK verifies receipts server-side anyway. Strip it via a text pass
            // after WriteToFile because PBXProject has no public RemoveCapability API.
            var pbxContent = File.ReadAllText(pbxPath);
            var patched = Regex.Replace(
                pbxContent,
                @"[ \t]+com\.apple\.InAppPurchase = \{[^}]+\};\n",
                "");
            if (patched != pbxContent)
            {
                File.WriteAllText(pbxPath, patched);
                UnityEngine.Debug.Log("[RoasSensor] Removed In-App Purchase capability from all targets.");
            }

            UnityEngine.Debug.Log("[RoasSensor] iOS post-process complete: AdSupport/AppTrackingTransparency/AdServices/StoreKit linked.");
        }
    }
}
#endif
