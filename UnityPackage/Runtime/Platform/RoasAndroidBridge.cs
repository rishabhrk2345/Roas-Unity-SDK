#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using RoasSensor.Internal;
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

        // ── Meta's own install referrer ─────────────────────────────────────────

        private static readonly string[] MetaColumns = { "install_referrer", "is_ct", "actual_timestamp" };

        /// <summary>
        /// Reads Meta's own install referrer (<see cref="RoasMetaReferrer"/>) from the Facebook,
        /// Instagram and Facebook Lite apps, or says why there is none. Blocking local IPC: call it
        /// off the main thread on a thread already attached to the JVM, as the first-open path does.
        /// Never throws.
        ///
        /// The host app's manifest must carry the three <c>&lt;queries&gt;&lt;package&gt;</c> entries from
        /// this package's README (Android 11+ package visibility), or the providers are invisible and
        /// every read reports NOT_AVAILABLE, indistinguishable from "Facebook is not installed".
        /// </summary>
        public static RoasMetaReferrer.Answer ReadMetaInstallReferrer(string metaAppId)
        {
            var appId = RoasMetaReferrer.NormalizeAppId(metaAppId);
            if (appId == null) return RoasMetaReferrer.Answer.None("NO_APP_ID");
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                {
                    var present = false;
                    var answers = new List<RoasMetaReferrer.Answer>();
                    foreach (var source in RoasMetaReferrer.Sources)
                    {
                        // Only the package meant to own an authority may answer it: with Facebook
                        // absent, any app can claim the authority and hand back a forged referrer.
                        if (OwnerPackage(packageManager, source.Authority) != source.Package) continue;
                        present = true;
                        try
                        {
                            var answer = QueryMetaProvider(resolver, source.Authority, appId);
                            if (answer != null) answers.Add(answer);
                        }
                        catch (Exception)
                        {
                            // A refused query must never cost the install its other signals.
                        }
                    }
                    if (!present) return RoasMetaReferrer.Answer.None("NOT_AVAILABLE");
                    return RoasMetaReferrer.Best(answers) ?? RoasMetaReferrer.Answer.None("OK_EMPTY");
                }
            }
            catch (Exception)
            {
                return RoasMetaReferrer.Answer.None("ERROR");
            }
        }

        /// <summary>The package that owns <paramref name="authority"/>, or null when nothing resolves
        /// it (or the lookup fails).</summary>
        private static string OwnerPackage(AndroidJavaObject packageManager, string authority)
        {
            try
            {
                using (var info = packageManager.Call<AndroidJavaObject>("resolveContentProvider", authority, 0))
                {
                    return info == null ? null : info.Get<string>("packageName");
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ContentResolver.query takes three null arguments, and AndroidJavaObject.Call cannot express
        // a typed null (a null argument is signed as java.lang.Object, which matches no overload of
        // query). So this one call, and the Cursor reads after it, go through raw JNI. Raw AndroidJNI
        // calls do NOT throw a C# exception when Java throws: the exception stays pending, and the
        // next JNI call then aborts the whole process. Hence every call that can fail is followed by
        // ClearPendingException.
        private static RoasMetaReferrer.Answer QueryMetaProvider(AndroidJavaObject resolver, string authority, string appId)
        {
            using (var uriClass = new AndroidJavaClass("android.net.Uri"))
            using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", "content://" + authority + "/" + appId))
            {
                var cursor = IntPtr.Zero;
                var projection = IntPtr.Zero;
                try
                {
                    var resolverClass = AndroidJNI.FindClass("android/content/ContentResolver");
                    var query = AndroidJNI.GetMethodID(
                        resolverClass,
                        "query",
                        "(Landroid/net/Uri;[Ljava/lang/String;Ljava/lang/String;[Ljava/lang/String;Ljava/lang/String;)Landroid/database/Cursor;");
                    if (ClearPendingException() || query == IntPtr.Zero) return null;

                    var stringClass = AndroidJNI.FindClass("java/lang/String");
                    projection = AndroidJNI.NewObjectArray(MetaColumns.Length, stringClass, IntPtr.Zero);
                    for (var i = 0; i < MetaColumns.Length; i++)
                    {
                        var name = AndroidJNI.NewStringUTF(MetaColumns[i]);
                        AndroidJNI.SetObjectArrayElement(projection, i, name);
                        AndroidJNI.DeleteLocalRef(name);
                    }

                    // selection, selectionArgs and sortOrder stay IntPtr.Zero, which is Java null.
                    var args = new jvalue[5];
                    args[0].l = uri.GetRawObject();
                    args[1].l = projection;
                    cursor = AndroidJNI.CallObjectMethod(resolver.GetRawObject(), query, args);
                    if (ClearPendingException() || cursor == IntPtr.Zero) return null;

                    return ReadMetaRow(cursor);
                }
                finally
                {
                    if (cursor != IntPtr.Zero)
                    {
                        CloseCursor(cursor);
                        AndroidJNI.DeleteLocalRef(cursor);
                    }
                    if (projection != IntPtr.Zero) AndroidJNI.DeleteLocalRef(projection);
                }
            }
        }

        private static RoasMetaReferrer.Answer ReadMetaRow(IntPtr cursor)
        {
            var cursorClass = AndroidJNI.FindClass("android/database/Cursor");

            var moveToFirst = AndroidJNI.GetMethodID(cursorClass, "moveToFirst", "()Z");
            if (ClearPendingException()) return null;
            var hasRow = AndroidJNI.CallBooleanMethod(cursor, moveToFirst, new jvalue[0]);
            if (ClearPendingException() || !hasRow) return null;

            var referrerIndex = ColumnIndex(cursor, cursorClass, "install_referrer");
            if (referrerIndex < 0) return null;
            var referrer = CursorString(cursor, cursorClass, referrerIndex);
            if (!RoasMetaReferrer.IsUsableReferrer(referrer)) return null;

            var answer = new RoasMetaReferrer.Answer { Referrer = referrer, Status = "OK" };

            var ctIndex = ColumnIndex(cursor, cursorClass, "is_ct");
            if (ctIndex >= 0)
            {
                var ct = CursorInt(cursor, cursorClass, ctIndex);
                if (ct.HasValue) answer.IsClick = ct.Value == 1;
            }

            var timeIndex = ColumnIndex(cursor, cursorClass, "actual_timestamp");
            if (timeIndex >= 0)
            {
                var at = CursorLong(cursor, cursorClass, timeIndex);
                if (at.HasValue && at.Value > 0) answer.AtSeconds = at.Value;
            }

            return answer;
        }

        /// <summary>-1 when the column is absent or the call failed.</summary>
        private static int ColumnIndex(IntPtr cursor, IntPtr cursorClass, string column)
        {
            var method = AndroidJNI.GetMethodID(cursorClass, "getColumnIndex", "(Ljava/lang/String;)I");
            if (ClearPendingException()) return -1;
            var name = AndroidJNI.NewStringUTF(column);
            try
            {
                var args = new jvalue[1];
                args[0].l = name;
                var index = AndroidJNI.CallIntMethod(cursor, method, args);
                return ClearPendingException() ? -1 : index;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(name);
            }
        }

        private static string CursorString(IntPtr cursor, IntPtr cursorClass, int index)
        {
            var method = AndroidJNI.GetMethodID(cursorClass, "getString", "(I)Ljava/lang/String;");
            if (ClearPendingException()) return null;
            var args = new jvalue[1];
            args[0].i = index;
            var value = AndroidJNI.CallStringMethod(cursor, method, args);
            return ClearPendingException() ? null : value;
        }

        private static int? CursorInt(IntPtr cursor, IntPtr cursorClass, int index)
        {
            var method = AndroidJNI.GetMethodID(cursorClass, "getInt", "(I)I");
            if (ClearPendingException()) return null;
            var args = new jvalue[1];
            args[0].i = index;
            var value = AndroidJNI.CallIntMethod(cursor, method, args);
            if (ClearPendingException()) return null;
            return value;
        }

        private static long? CursorLong(IntPtr cursor, IntPtr cursorClass, int index)
        {
            var method = AndroidJNI.GetMethodID(cursorClass, "getLong", "(I)J");
            if (ClearPendingException()) return null;
            var args = new jvalue[1];
            args[0].i = index;
            var value = AndroidJNI.CallLongMethod(cursor, method, args);
            if (ClearPendingException()) return null;
            return value;
        }

        private static void CloseCursor(IntPtr cursor)
        {
            try
            {
                var cursorClass = AndroidJNI.FindClass("android/database/Cursor");
                var close = AndroidJNI.GetMethodID(cursorClass, "close", "()V");
                if (ClearPendingException()) return;
                AndroidJNI.CallVoidMethod(cursor, close, new jvalue[0]);
                ClearPendingException();
            }
            catch (Exception)
            {
                // Nothing useful to do about a cursor that will not close.
            }
        }

        /// <summary>True (after clearing it) when the last raw JNI call left a Java exception
        /// pending. Must run after every raw call that can throw, or the next JNI call aborts.</summary>
        private static bool ClearPendingException()
        {
            var pending = AndroidJNI.ExceptionOccurred();
            if (pending == IntPtr.Zero) return false;
            AndroidJNI.ExceptionClear();
            AndroidJNI.DeleteLocalRef(pending);
            return true;
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
