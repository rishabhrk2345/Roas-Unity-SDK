using System.Globalization;
using UnityEngine;

namespace RoasSensor.Internal
{
    /// <summary>
    /// The device fields stamped onto every touchpoint beacon — the Unity analogue of the
    /// Kotlin SDK's <c>DeviceInfo.describe()</c> and the Swift SDK's <c>DeviceContext</c>.
    /// Pulled from <see cref="SystemInfo"/>/<see cref="Application"/>/<see cref="Screen"/>
    /// rather than a native plugin, so it works identically on every Unity target — the
    /// platform-specific identity keys (GAID/App Set Id on Android, IDFA/IDFV on iOS,
    /// install referrer / Apple Search Ads) live in <c>RoasSensor.Platform</c> instead,
    /// since those genuinely have no cross-platform API.
    /// </summary>
    internal static class RoasDeviceInfo
    {
        public const string SdkVersion = "0.1.0";

        public static void Describe(RoasJson body)
        {
            body.Put("sdk_version", SdkVersion);
            body.Put("os", CurrentOs());
            body.Put("os_version", SystemInfo.operatingSystem);
            body.Put("device_manufacturer", SafeSplit(SystemInfo.deviceModel, 0));
            body.Put("device_model", SystemInfo.deviceModel);
            body.Put("device_type", DeviceType());
            body.Put("screen", Screen.width + "x" + Screen.height);
            body.Put("viewport", Screen.width + "x" + Screen.height);
            body.Put("screen_density", Mathf.RoundToInt(Screen.dpi));
            body.Put("language", Application.systemLanguage.ToString());
            body.Put("timezone", System.TimeZoneInfo.Local.Id);
            body.Put("total_ram_mb", SystemInfo.systemMemorySize);
            body.Put("cpu_cores", SystemInfo.processorCount);
            body.Put("network_type", NetworkType());

            if (SystemInfo.batteryLevel >= 0f)
            {
                body.Put("battery_level", Mathf.RoundToInt(SystemInfo.batteryLevel * 100f));
            }
            body.Put("battery_charging",
                SystemInfo.batteryStatus == BatteryStatus.Charging || SystemInfo.batteryStatus == BatteryStatus.Full);
        }

        public static string AppVersion() => Application.version;

        private static string CurrentOs()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                default: return "Unity";
            }
        }

        /// <summary>Rough phone/tablet split from the smaller screen dimension in inches —
        /// good enough for reporting colour; unlike the native SDKs this has no OS API to
        /// defer to. &gt;= 7" reads as a tablet.</summary>
        private static string DeviceType()
        {
            if (Screen.dpi <= 0f) return "mobile";
            var shortSidePx = Mathf.Min(Screen.width, Screen.height);
            var shortSideInches = shortSidePx / Screen.dpi;
            return shortSideInches >= 4.5f ? "tablet" : "mobile";
        }

        private static string NetworkType()
        {
            switch (Application.internetReachability)
            {
                case NetworkReachability.ReachableViaLocalAreaNetwork: return "wifi";
                case NetworkReachability.ReachableViaCarrierDataNetwork: return "cellular";
                default: return "none";
            }
        }

        private static string SafeSplit(string value, int index)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var parts = value.Split(' ');
            return index < parts.Length ? parts[index] : value;
        }
    }
}
