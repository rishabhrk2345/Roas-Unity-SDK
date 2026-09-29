using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoasSensor.Internal
{
    /// <summary>
    /// Emulator / root / jailbreak signals — mirrors the Android/iOS SDKs' rule exactly:
    /// the SDK sends raw signal NAMES, never a verdict. A verdict computed on-device bakes a
    /// threshold into a build that lives on devices for years; sending evidence lets the
    /// classification rule be retuned server-side on a deploy (see
    /// <c>services/integrity.py</c> in the backend) and old rows reinterpreted.
    ///
    /// This is a best-effort, pure-C# port: the native SDKs read OS build fields and system
    /// properties a native plugin would be needed for full parity on. What's checked here
    /// (common su/root-manager paths, Cydia, a sandbox write probe, the Editor/emulator
    /// screen heuristic) covers the same categories using only what Unity's sandboxed file
    /// I/O can see without one.
    /// </summary>
    internal static class RoasDeviceIntegrity
    {
        private static readonly string[] AndroidSuPaths =
        {
            "/system/bin/su", "/system/xbin/su", "/sbin/su",
            "/system/app/Superuser.apk", "/system/app/SuperSU.apk",
        };

        private static readonly string[] AndroidMagiskPaths =
        {
            "/sbin/.magisk", "/cache/magisk.log", "/data/adb/magisk",
        };

        private static readonly string[] IosCydiaPaths =
        {
            "/Applications/Cydia.app", "/Library/MobileSubstrate/MobileSubstrate.dylib",
            "/bin/bash", "/usr/sbin/sshd", "/etc/apt",
        };

        public static List<string> Signals()
        {
            var signals = new List<string>();

            if (Application.isEditor) signals.Add("editor");

            if (Application.platform == RuntimePlatform.Android)
            {
                foreach (var path in AndroidSuPaths) if (FileExists(path)) { signals.Add("su_binary"); break; }
                foreach (var path in AndroidMagiskPaths) if (FileExists(path)) { signals.Add("magisk_files"); break; }
                if (SystemInfo.deviceModel.IndexOf("sdk", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || SystemInfo.deviceModel.IndexOf("emulator", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || SystemInfo.deviceModel.IndexOf("google_sdk", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    signals.Add("hardware");
                }
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                foreach (var path in IosCydiaPaths) if (Directory.Exists(path) || FileExists(path)) { signals.Add("cydia"); break; }
                if (CanWriteOutsideSandbox()) signals.Add("sandbox_write");
            }
            else if (Application.platform == RuntimePlatform.IPhoneSimulator)
            {
                signals.Add("simulator");
            }

            return signals;
        }

        private static bool FileExists(string path)
        {
            try { return File.Exists(path); } catch { return false; }
        }

        private static bool CanWriteOutsideSandbox()
        {
            try
            {
                const string probe = "/private/roas_sandbox_probe.txt";
                File.WriteAllText(probe, "roas");
                File.Delete(probe);
                return true; // a real sandbox refuses this; only a jailbroken device allows it
            }
            catch
            {
                return false;
            }
        }
    }
}
