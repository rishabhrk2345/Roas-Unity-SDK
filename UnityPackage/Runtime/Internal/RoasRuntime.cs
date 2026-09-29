using System;
using UnityEngine;

namespace RoasSensor.Internal
{
    /// <summary>
    /// The hidden <see cref="MonoBehaviour"/> that gives <c>Roas</c> the two things a static
    /// class cannot have on its own: a coroutine host for <see cref="RoasTransport"/>'s
    /// network flushes, and the foreground/background lifecycle callbacks a session boundary
    /// is derived from (the Unity analogue of the Android SDK's
    /// <c>Application.ActivityLifecycleCallbacks</c> and the iOS SDK's
    /// <c>UIApplication.didEnterBackgroundNotification</c> observer).
    ///
    /// Also the target GameObject for <c>RoasNative.mm</c>'s <c>UnitySendMessage</c> callback,
    /// since the ATT prompt result on iOS arrives asynchronously from native code with no
    /// other way back onto the C# side.
    /// </summary>
    internal sealed class RoasRuntime : MonoBehaviour
    {
        public Action<bool> OnPauseChanged; // true = entering background
        public Action OnQuit;
        public Action<int> OnTrackingAuthorizationStatus;

        private static RoasRuntime _instance;

        public static RoasRuntime GetOrCreate()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("RoasSensorRuntime");
            go.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<RoasRuntime>();
            return _instance;
        }

        public string GameObjectName => gameObject.name;

        private void OnApplicationPause(bool pauseStatus)
        {
            OnPauseChanged?.Invoke(pauseStatus);
        }

        private void OnApplicationQuit()
        {
            OnQuit?.Invoke();
        }

        /// <summary>Called via UnitySendMessage from RoasNative.mm's ATT completion handler.
        /// The message body is the ATTrackingManagerAuthorizationStatus raw value as a string.
        /// Must stay public — SendMessage cannot reach a private method.</summary>
        public void OnTrackingAuthorizationResult(string statusString)
        {
            if (int.TryParse(statusString, out var status)) OnTrackingAuthorizationStatus?.Invoke(status);
        }
    }
}
