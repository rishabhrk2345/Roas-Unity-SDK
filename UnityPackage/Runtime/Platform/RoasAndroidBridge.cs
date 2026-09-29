#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace RoasSensor.Platform
{
    /// <summary>
    /// Android identity-signal reads that have no cross-platform Unity API: the advertising
    /// id (GAID), the App Set Id, and the Play Install Referrer. Reached through
    /// <see cref="AndroidJavaObject"/> reflection into Play Services classes rather than a
    /// bundled .aar, so no native Kotlin/Java source ships in this package — the trade-off
    /// (mirroring the Android SDK's own doc on <c>DeviceId.kt</c>/<c>AppSetId.kt</c>) is that
    /// the HOST app's Gradle build must actually resolve these Play Services artifacts (see
    /// this package's <c>Dependencies.xml</c>, consumed by External Dependency Manager). Every
    /// call here is wrapped so a missing dependency degrades to "no signal" rather than a crash.
    /// </summary>
    internal static class RoasAndroidBridge
    {
        /// <summary>Blocking — call off the main thread, same as the Kotlin SDK's
        /// `DeviceId.advertisingId`. Null if ads personalization is off, Play Services is
        /// unavailable, or the artifact isn't in the build.</summary>
        public static string AdvertisingId()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var client = new AndroidJavaClass("com.google.android.gms.ads.identifier.AdvertisingIdClient"))
                using (var info = client.CallStatic<AndroidJavaObject>("getAdvertisingIdInfo", activity))
                {
                    var limitAdTracking = info.Call<bool>("isLimitAdTrackingEnabled");
                    if (limitAdTracking) return null; // respects the user's choice, like the Kotlin SDK
                    return info.Call<string>("getId");
                }
            }
            catch (Exception)
            {
                return null; // Play Services missing, artifact not in the build, or no network Task
            }
        }

        /// <summary>Blocking read of the App Set Id. Fraud/dedup only — NEVER attribution
        /// (Google policy); the server salt-hashes it precisely so it can't double as an
        /// ad-matching key. Uses Tasks.await under the hood via a short join since the
        /// Play Services AppSet API is callback-based; bounded so a hung Task can't wedge
        /// the caller's background thread forever.</summary>
        public static string AppSetId()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var appSetClass = new AndroidJavaClass("com.google.android.gms.appset.AppSet"))
                using (var client = appSetClass.CallStatic<AndroidJavaObject>("getClient", activity))
                using (var task = client.Call<AndroidJavaObject>("getAppSetIdInfo"))
                {
                    var latch = new System.Threading.ManualResetEventSlim(false);
                    string result = null;
                    var listener = new AppSetIdSuccessListener(info =>
                    {
                        try { result = info.Call<string>("getId"); }
                        catch { /* ignore */ }
                        finally { latch.Set(); }
                    });
                    task.Call<AndroidJavaObject>("addOnSuccessListener", listener);
                    task.Call<AndroidJavaObject>("addOnFailureListener", new AppSetIdFailureListener(() => latch.Set()));
                    latch.Wait(TimeSpan.FromSeconds(5));
                    return result;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Starts the Play Install Referrer connection and calls back with the raw
        /// referrer string (or null) plus click/install timestamps in epoch seconds. Fire and
        /// forget — the caller decides retry policy, matching the Kotlin SDK's
        /// <c>InstallReferrerReader</c>.</summary>
        public static void FetchInstallReferrer(Action<InstallReferrerResult> callback)
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var builder = new AndroidJavaObject("com.android.installreferrer.api.InstallReferrerClient$Builder", activity))
                using (var client = builder.Call<AndroidJavaObject>("build"))
                {
                    var listener = new InstallReferrerStateListener(client, callback);
                    client.Call("startConnection", listener);
                }
            }
            catch (Exception)
            {
                callback(new InstallReferrerResult { Status = "UNAVAILABLE" });
            }
        }

        public sealed class InstallReferrerResult
        {
            public string Status = "UNAVAILABLE";
            public string Referrer;
            public long? ClickTimestampSeconds;
            public long? InstallBeginTimestampSeconds;
        }

        private sealed class AppSetIdSuccessListener : AndroidJavaProxy
        {
            private readonly Action<AndroidJavaObject> _onSuccess;
            public AppSetIdSuccessListener(Action<AndroidJavaObject> onSuccess)
                : base("com.google.android.gms.tasks.OnSuccessListener") { _onSuccess = onSuccess; }
            public void onSuccess(AndroidJavaObject result) => _onSuccess(result);
        }

        private sealed class AppSetIdFailureListener : AndroidJavaProxy
        {
            private readonly Action _onFailure;
            public AppSetIdFailureListener(Action onFailure)
                : base("com.google.android.gms.tasks.OnFailureListener") { _onFailure = onFailure; }
            public void onFailure(AndroidJavaObject exception) => _onFailure();
        }

        private sealed class InstallReferrerStateListener : AndroidJavaProxy
        {
            private readonly AndroidJavaObject _client;
            private readonly Action<InstallReferrerResult> _callback;

            public InstallReferrerStateListener(AndroidJavaObject client, Action<InstallReferrerResult> callback)
                : base("com.android.installreferrer.api.InstallReferrerStateListener")
            {
                _client = client;
                _callback = callback;
            }

            // Response codes from InstallReferrerClient.InstallReferrerResponse:
            // OK=0, FEATURE_NOT_SUPPORTED=2, SERVICE_UNAVAILABLE=1, DEVELOPER_ERROR=3,
            // SERVICE_DISCONNECTED=-1, PERMISSION_ERROR=4
            public void onInstallReferrerSetupFinished(int responseCode)
            {
                var result = new InstallReferrerResult();
                try
                {
                    if (responseCode == 0)
                    {
                        using (var response = _client.Call<AndroidJavaObject>("getInstallReferrer"))
                        {
                            result.Status = "OK";
                            result.Referrer = response.Call<string>("getInstallReferrer");
                            result.ClickTimestampSeconds = response.Call<long>("getReferrerClickTimestampSeconds");
                            result.InstallBeginTimestampSeconds = response.Call<long>("getInstallBeginTimestampSeconds");
                            if (string.IsNullOrEmpty(result.Referrer)) result.Status = "OK_EMPTY";
                        }
                    }
                    else
                    {
                        result.Status = "ERROR_" + responseCode;
                    }
                }
                catch (Exception)
                {
                    result.Status = "READ_ERROR";
                }
                finally
                {
                    try { _client.Call("endConnection"); } catch { /* ignore */ }
                    _callback(result);
                }
            }

            public void onInstallReferrerServiceDisconnected()
            {
                // A retry, if one is warranted, is the caller's responsibility on a later launch.
            }
        }
    }
}
#endif
