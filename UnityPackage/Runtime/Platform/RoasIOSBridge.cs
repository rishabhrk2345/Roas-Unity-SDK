#if UNITY_IOS && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RoasSensor.Platform
{
    /// <summary>Managed wrapper over <c>RoasNative.mm</c> — the iOS identity/ATT/SKAdNetwork
    /// signals that have no cross-platform Unity API. See that file for the framework/behaviour
    /// notes; this class only marshals.</summary>
    internal static class RoasIOSBridge
    {
        [DllImport("__Internal")] private static extern IntPtr _roas_advertisingIdentifier();
        [DllImport("__Internal")] private static extern IntPtr _roas_identifierForVendor();
        [DllImport("__Internal")] private static extern IntPtr _roas_appAccountToken(string visitorId);
        [DllImport("__Internal")] private static extern int _roas_trackingAuthorizationStatus();
        [DllImport("__Internal")] private static extern void _roas_requestTrackingAuthorization(string gameObjectName);
        [DllImport("__Internal")] private static extern IntPtr _roas_appleSearchAdsToken();
        [DllImport("__Internal")] private static extern void _roas_updateConversionValue(int value, string coarse, int lockWindow);

        public static string AdvertisingIdentifier() => MarshalAndFree(_roas_advertisingIdentifier());
        public static string IdentifierForVendor() => MarshalAndFree(_roas_identifierForVendor());
        public static string AppAccountToken(string visitorId) => MarshalAndFree(_roas_appAccountToken(visitorId));
        public static int TrackingAuthorizationStatus() => _roas_trackingAuthorizationStatus();
        public static string AppleSearchAdsToken() => MarshalAndFree(_roas_appleSearchAdsToken());

        public static void RequestTrackingAuthorization(string gameObjectName) =>
            _roas_requestTrackingAuthorization(gameObjectName);

        public static void UpdateConversionValue(int value, string coarse, bool lockWindow) =>
            _roas_updateConversionValue(value, coarse, lockWindow ? 1 : 0);

        private static string MarshalAndFree(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return null;
            var value = Marshal.PtrToStringAnsi(ptr);
            Marshal.FreeHGlobal(ptr); // matches RoasNative.mm's strdup — caller frees
            return value;
        }
    }
}
#endif
