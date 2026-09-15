using System;
using System.Globalization;
using System.Text;
using CinematicRecorder.Camera.Control;
using CinematicRecorder.Camera.Model;
using CinematicRecorder.Camera.Persistence;
using CinematicRecorder.Core;
using UnityEngine;

namespace CinematicRecorder.Camera.GateInstrumentation
{
    /// <summary>
    /// TEMPORARY debug gate harness (build-program chunk P1-C7; REMOVED in
    /// P2-C4 per the INTEGRATION_CONTRACT scaffolding list — these keybinds
    /// are replaced by the provisional CameraLibraryWindow, D-B1). Exists so
    /// the user can drive gates G-P1a…d before any camera UI ships: load the
    /// camera library, list cameras, activate/deactivate by index, and
    /// evaluate the active native camera in preview.
    ///
    /// Self-hosting per scene-generation-ownership.md (the
    /// FlightCameraSeizure lazy-DDOL pattern is the precedent):
    /// <see cref="EnsureCreated"/> builds the host once on first use, the
    /// shared static is owner-gated in OnDestroy, and the instance owns
    /// exactly the objects it creates (the P1 NativeCameraManager and
    /// CameraLibraryState instances live here for P1; P2-C4's UI takes
    /// ownership of both when it replaces this harness).
    ///
    /// Keybinds (keypad — unbound by stock KSP in flight):
    /// KeypadPlus = load <c>PluginData/CameraLibrary.cfg</c>;
    /// KeypadEnter = list cameras (ScreenMessage + KSP.log);
    /// Keypad0…Keypad9 = activate the camera at that index (deactivating any
    /// previously active camera first); KeypadPeriod = deactivate the active
    /// camera (full stock-camera restore).
    ///
    /// Preview driver (parent spec §4.3): outside capture, the active native
    /// camera evaluates once per rendered frame against real time — preview
    /// is non-deterministic by nature and never the source of persisted
    /// state. This Update call site is the ONLY place in the 0.3.0 P1 build
    /// that may read the engine frame step (plan invariant 1), and it is
    /// inert while a deterministic capture session is active (the capture
    /// path drives evaluation per captured frame instead).
    /// </summary>
    public sealed class DebugCameraGateHarness : MonoBehaviour
    {
        private const string LogPrefix = "[GateHarness] ";
        private const string NoLibraryMessage = "library empty — press KeypadPlus to load CameraLibrary.cfg";

        private static DebugCameraGateHarness s_instance;

        private NativeCameraManager _manager;
        private CameraLibraryState _library;
        private CameraDefinition _activeDefinition;

        #region Hosting (scene-generation-ownership.md rules)
        /// <summary>
        /// Creates the DontDestroyOnLoad harness host on first use. Called
        /// from SafetyMonitor.OnEnable (TEMPORARY wiring — removed with this
        /// harness in P2-C4).
        /// </summary>
        public static void EnsureCreated()
        {
            if (s_instance != null) return;

            var host = new GameObject("CinematicRecorder_GateHarness_DEBUG");
            DontDestroyOnLoad(host);
            s_instance = host.AddComponent<DebugCameraGateHarness>();
        }

        private void Awake()
        {
            _manager = new NativeCameraManager();
            _library = new CameraLibraryState();
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
        }

        private void OnDisable()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
        }

        private void OnDestroy()
        {
            // Ownership rule 2: only clear the shared static while it is ours.
            if (s_instance == this)
                s_instance = null;
        }

        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            // Vessel/part references die with the scene; run the full release
            // path so the stock camera is restored before the next scene.
            _manager.DeactivateActiveCamera();
        }
        #endregion

        #region Static seams for the capture path and SafetyMonitor (P1-C7)
        /// <summary>
        /// The active native camera, or null when none is active. TEMPORARY
        /// seam consumed by DeterministicCaptureSession (arm hook, native
        /// drive, pose-log driver label); moves with the manager owner in P2-C4.
        /// </summary>
        public static NativeCamera ActiveNativeCamera
        {
            get { return s_instance != null ? s_instance._manager.ActiveCamera : null; }
        }

        /// <summary>
        /// Human-readable anchor descriptor of the active camera (pose-log run
        /// header), or "-" when no native camera is active.
        /// </summary>
        public static string ActiveCameraAnchorDescriptor
        {
            get
            {
                DebugCameraGateHarness instance = s_instance;
                if (instance == null || instance._activeDefinition == null) return "-";
                return DescribeAnchor(instance._activeDefinition);
            }
        }

        /// <summary>
        /// Stop hook (parent spec §5.1): after a capture ends, the camera
        /// stays active for review. ArmForRecording only arms from Previewing,
        /// so a RecordingDriven camera is returned to preview here by a full
        /// deactivate/activate round-trip: the release restores the stock
        /// snapshot, and the re-acquire re-snapshots the identical restored
        /// state (no visible change; the velocity model re-initializes on the
        /// next recording start via the pinned arm path).
        /// </summary>
        public static void ReturnActiveCameraToPreview()
        {
            DebugCameraGateHarness instance = s_instance;
            if (instance == null) return;

            NativeCamera active = instance._manager.ActiveCamera;
            if (active == null || active.CurrentState != NativeCameraState.RecordingDriven) return;

            instance._manager.DeactivateActiveCamera();
            instance._manager.ActivateCamera(active);
        }

        /// <summary>
        /// Emergency path (SafetyMonitor.RequestEmergencyReset, gate G-P1c):
        /// deactivates the active native camera through the manager — the
        /// full release path restores the stock camera snapshot. No-op when
        /// nothing is active or the harness was never created.
        /// </summary>
        public static void EmergencyResetActiveCamera()
        {
            DebugCameraGateHarness instance = s_instance;
            if (instance == null) return;

            instance._manager.DeactivateActiveCamera();
        }
        #endregion

        #region Keybinds + preview driver
        private void Update()
        {
            // Preview driver (parent spec §4.3): real-time evaluation outside
            // capture only — inert while a capture session is active, where
            // the capture loop drives evaluation per captured frame instead.
            if (!DeterministicCaptureSession.IsRunning && _manager.HasActiveCamera)
                _manager.ActiveCamera.Evaluate(UnityEngine.Time.deltaTime);

            if (Input.GetKeyDown(KeyCode.KeypadPlus))
                LoadLibrary();
            else if (Input.GetKeyDown(KeyCode.KeypadEnter))
                ListCameras();
            else if (Input.GetKeyDown(KeyCode.KeypadPeriod))
                DeactivateCamera();
            else
            {
                for (int i = 0; i <= 9; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Keypad0 + i))
                    {
                        ActivateByIndex(i);
                        break;
                    }
                }
            }
        }

        private void LoadLibrary()
        {
            CameraLibraryLoadResult result = _library.Load();
            string message = string.Format(
                CultureInfo.InvariantCulture,
                "library loaded: {0} cameras ({1} skipped)",
                result.LoadedCount,
                result.SkippedCount);
            Post(message);
        }

        private void ListCameras()
        {
            if (_library.Cameras.Count == 0)
            {
                Post(NoLibraryMessage);
                return;
            }

            var builder = new StringBuilder(LogPrefix).Append("cameras:");
            for (int i = 0; i < _library.Cameras.Count; i++)
                builder.Append(' ').Append(i).Append(':').Append(_library.Cameras[i].Name);

            // Keypress-level logging is event-level, not per-frame (project rule).
            UnityEngine.Debug.Log(builder.ToString());
            Post("camera list posted to KSP.log (" + _library.Cameras.Count + " cameras)");
        }

        private void ActivateByIndex(int index)
        {
            if (index < 0 || index >= _library.Cameras.Count)
            {
                Post("index " + index + " out of range (" + _library.Cameras.Count + " cameras loaded)");
                return;
            }

            CameraDefinition definition = _library.Cameras[index];
            NativeCamera camera = _manager.CreateCamera(definition);
            if (_manager.ActivateCamera(camera))
            {
                _activeDefinition = definition;
                Post("activated [" + index + "] " + definition.Name);
            }
            else
            {
                Post("activation of [" + index + "] " + definition.Name + " refused: "
                    + (camera.UnavailableReason ?? "seizure unavailable (IVA active or no flight camera)"));
            }
        }

        private void DeactivateCamera()
        {
            if (!_manager.HasActiveCamera)
            {
                Post("no active native camera");
                return;
            }

            _manager.DeactivateActiveCamera();
            Post("deactivated (stock camera restored)");
        }
        #endregion

        #region Private Helpers
        private static void Post(string message)
        {
            ScreenMessages.PostScreenMessage(LogPrefix + message, 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static string DescribeAnchor(CameraDefinition definition)
        {
            CameraAnchor.Body body = definition.Anchor as CameraAnchor.Body;
            if (body != null)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "BODY {0} lat={1} lon={2} alt={3}",
                    body.Coordinate.BodyName,
                    FormatDouble(body.Coordinate.Latitude),
                    FormatDouble(body.Coordinate.Longitude),
                    FormatDouble(body.Coordinate.Altitude));
            }

            var vessel = (CameraAnchor.Vessel)definition.Anchor;
            return string.Format(
                CultureInfo.InvariantCulture,
                "VESSEL part={0} fallback={1} distance={2} forward={3} right={4} up={5}",
                vessel.AnchorPartPersistentId,
                vessel.FallbackPolicy,
                FormatDouble(vessel.Distance),
                FormatDouble(vessel.Forward),
                FormatDouble(vessel.Right),
                FormatDouble(vessel.Up));
        }

        private static string FormatDouble(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }
        #endregion
    }
}
