using System.IO;
using RoasSensor;
using UnityEditor;
using UnityEngine;

namespace RoasSensor.Editor
{
    internal static class RoasSetupMenu
    {
        private const string AssetDir = "Assets/Resources";
        private const string AssetPath = AssetDir + "/RoasSettings.asset";

        [MenuItem("ROASSensor/Create Settings Asset", priority = 0)]
        private static void CreateSettingsAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<RoasSettings>(AssetPath);
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                EditorUtility.DisplayDialog("ROASSensor", "A settings asset already exists at " + AssetPath, "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var settings = ScriptableObject.CreateInstance<RoasSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("ROASSensor/Documentation", priority = 100)]
        private static void OpenDocs()
        {
            Application.OpenURL("https://github.com/rishabhrk2345/Roas-Unity-SDK#readme");
        }
    }
}
