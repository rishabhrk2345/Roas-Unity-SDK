using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();

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

        /// <summary>
        /// Queue work to run on Unity's main thread on the next frame. The one way back from a
        /// background thread: PlayerPrefs, StartCoroutine and UnityWebRequest all require the
        /// main thread, but the native device-signal reads that feed them (Android's GAID/App
        /// Set Id in particular -- the latter has its own 5-second internal timeout) must NOT
        /// run there directly, or a slow device turns install reporting into a startup hitch or
        /// worse. Call native reads from a background <see cref="System.Threading.Tasks.Task"/>,
        /// then hop back here to actually build and send the beacon.
        /// </summary>
        public void RunOnMainThread(Action action) => _mainThreadActions.Enqueue(action);

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

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
