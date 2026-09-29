#if UNITY_IOS
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;

namespace RoasSensor.Editor
{
    /// <summary>
    /// Links the frameworks <c>Runtime/Plugins/iOS/RoasNative.mm</c> needs
    /// (AdSupport, AppTrackingTransparency, AdServices, StoreKit) into the generated Xcode
    /// project automatically, so integrating this package needs no manual Xcode step beyond
    /// setting <c>NSUserTrackingUsageDescription</c> in Player Settings (Unity does not let a
    /// package inject a usage-description string on your behalf, since its wording is a
    /// product/legal decision, not this SDK's to make).
    /// </summary>
    internal sealed class RoasIOSPostProcessBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;

            var pbxPath = PBXProject.GetPBXProjectPath(report.summary.outputPath);
            var project = new PBXProject();
            project.ReadFromFile(pbxPath);

#if UNITY_2019_3_OR_NEWER
            var targetGuid = project.GetUnityFrameworkTargetGuid();
#else
            var targetGuid = project.TargetGuidByName(PBXProject.GetUnityTargetName());
#endif

            project.AddFrameworkToProject(targetGuid, "AdSupport.framework", true);
            project.AddFrameworkToProject(targetGuid, "AppTrackingTransparency.framework", true);
            project.AddFrameworkToProject(targetGuid, "AdServices.framework", true);
            project.AddFrameworkToProject(targetGuid, "StoreKit.framework", true);

            project.WriteToFile(pbxPath);

            UnityEngine.Debug.Log("[RoasSensor] Linked AdSupport/AppTrackingTransparency/AdServices/StoreKit into the Xcode project. " +
                                   "Remember to set 'Privacy - Tracking Usage Description' in Player Settings -> iOS -> Other Settings.");
        }
    }
}
#endif
