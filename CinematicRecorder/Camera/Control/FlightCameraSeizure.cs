using CinematicRecorder.Capture;
using System;
using UnityEngine;

namespace CinematicRecorder.Camera.Control
{
    /// <summary>
    /// Scoped acquire/release ownership of <see cref="FlightCamera.fetch"/>.
    ///
    /// This class is the single write seam for FlightCamera transform state in the
    /// native camera system: parenting, local pose, distance, mode, and near clip.
    /// Per-frame FoV driving belongs to the zoom controller; FoV is snapshotted and
    /// restored here because it is part of the parent-spec §5.2 restore set.
    ///
    /// Acquire takes a full snapshot of the seven §5.2 fields before touching the
    /// camera, then seizes it (target cleared, reparented under an owned rig, stock
    /// camera update deactivated — the CameraTools seizure pattern, rewritten).
    /// Release restores every snapshot field, re-activates the stock update, and is
    /// idempotent and safe when not holding. A flight revert or any other scene
    /// change self-releases via GameEvents.onGameSceneLoadRequested, mirroring
    /// SafetyMonitor. While holding, the owner calls <see cref="AssertControl"/> each
    /// evaluation; a foreign reparenting is re-asserted and reported through
    /// <see cref="ParentStealDetected"/> once per acquisition.
    ///
    /// Ownership rules (#009/#010/#011): the instance hosts itself (DontDestroyOnLoad),
    /// the shared static is owner-gated in OnDestroy, and only the instance's own
    /// GameObjects are ever destroyed by it. IVA rule: acquisition and re-assertion
    /// writes are gated on CaptureCameraResolver.IsIvaMode(); release restores stock
    /// state unconditionally so no failure path can leave the camera seized.
    /// </summary>
    public class FlightCameraSeizure : MonoBehaviour
    {
        #region Snapshot
        /// <summary>
        /// Full save of the seven parent-spec §5.2 camera fields, taken before seizure
        /// and restored in full on release.
        /// </summary>
        private sealed class CameraSnapshot
        {
            public Transform Parent;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public float Distance;
            public FlightCamera.Modes Mode;
            public float FieldOfView;
            public float? NearClipPlane;

            public static CameraSnapshot Capture(FlightCamera flightCamera)
            {
                return new CameraSnapshot
                {
                    Parent = flightCamera.transform.parent,
                    LocalPosition = flightCamera.transform.localPosition,
                    LocalRotation = flightCamera.transform.localRotation,
                    Distance = flightCamera.Distance,
                    Mode = flightCamera.mode,
                    FieldOfView = flightCamera.FieldOfView,
                    NearClipPlane = flightCamera.mainCamera != null
                        ? flightCamera.mainCamera.nearClipPlane
                        : (float?)null,
                };
            }
        }
        #endregion
        #region Fields
        private static FlightCameraSeizure s_instance;

        private CameraSnapshot _snapshot;
        private GameObject _rigObject;
        private bool _holding;
        private bool _stealWarningRaised;
        #endregion
        #region Public API
        /// <summary>
        /// Shared access point; creates the DontDestroyOnLoad host on first use.
        /// </summary>
        public static FlightCameraSeizure Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var host = new GameObject("CinematicRecorder_CameraSeizure");
                    DontDestroyOnLoad(host);
                    s_instance = host.AddComponent<FlightCameraSeizure>();
                }
                return s_instance;
            }
        }

        /// <summary>
        /// True between a successful <see cref="Acquire"/> and <see cref="Release"/>.
        /// </summary>
        public bool IsHolding => _holding;

        /// <summary>
        /// Raised once per acquisition when <see cref="AssertControl"/> finds the camera
        /// reparented away from the seizure rig and re-asserts control. Event-level
        /// warning only; consumers decide how to surface it.
        /// </summary>
        public event Action ParentStealDetected;

        /// <summary>
        /// Seizes the flight camera: snapshots the seven §5.2 fields, clears the
        /// target, reparents the camera under an owned rig, and deactivates the stock
        /// camera update. No-op (returns false) when already holding, when the camera
        /// is unavailable, or while IVA is active. Every successful acquire has a
        /// guaranteed release path: <see cref="Release"/>, scene change, or
        /// <see cref="EmergencyRelease"/>.
        /// </summary>
        public bool Acquire()
        {
            if (_holding) return false;
            if (CaptureCameraResolver.IsIvaMode()) return false;

            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera == null) return false;

            CameraSnapshot snapshot = CameraSnapshot.Capture(flightCamera);

            flightCamera.SetTargetNone();
            flightCamera.transform.parent = EnsureRig();
            flightCamera.DeactivateUpdate();

            _snapshot = snapshot;
            _holding = true;
            _stealWarningRaised = false;
            return true;
        }

        /// <summary>
        /// Restores every snapshot field (parent, local pos/rot, distance, mode, FoV,
        /// near clip), re-activates the stock camera update, and drops the seizure.
        /// Idempotent and safe when not holding. Deliberately not IVA-gated: the
        /// release path must always complete (anti-latch invariant).
        /// </summary>
        public void Release()
        {
            if (!_holding) return;

            _holding = false;
            _stealWarningRaised = false;

            CameraSnapshot snapshot = _snapshot;
            _snapshot = null;

            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera != null && snapshot != null)
            {
                try
                {
                    flightCamera.transform.parent = snapshot.Parent;
                    flightCamera.transform.localPosition = snapshot.LocalPosition;
                    flightCamera.transform.localRotation = snapshot.LocalRotation;
                    flightCamera.SetDistanceImmediate(snapshot.Distance);
                    flightCamera.setModeImmediate(snapshot.Mode);
                    flightCamera.SetFoV(snapshot.FieldOfView);
                    if (snapshot.NearClipPlane.HasValue && flightCamera.mainCamera != null)
                        flightCamera.mainCamera.nearClipPlane = snapshot.NearClipPlane.Value;
                }
                finally
                {
                    flightCamera.ActivateUpdate();
                }
            }

            if (_rigObject != null)
            {
                Destroy(_rigObject);
                _rigObject = null;
            }
        }

        /// <summary>
        /// One-line hook for the SafetyMonitor emergency-reset wiring (P1-C7):
        /// releases the camera if held, no-op otherwise.
        /// </summary>
        public static void EmergencyRelease()
        {
            if (s_instance != null)
                s_instance.Release();
        }

        /// <summary>
        /// Per-evaluation ownership check the camera owner calls while active. If the
        /// camera was reparented or is being driven by something else, control is
        /// re-asserted and <see cref="ParentStealDetected"/> is raised once per
        /// acquisition. No-op while IVA is active.
        /// </summary>
        public void AssertControl()
        {
            if (!_holding) return;
            if (CaptureCameraResolver.IsIvaMode()) return;

            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera == null)
            {
                Release();
                return;
            }

            Transform rig = _rigObject != null ? _rigObject.transform : null;
            if (flightCamera.transform.parent != rig)
            {
                flightCamera.SetTargetNone();
                flightCamera.transform.parent = EnsureRig();
                flightCamera.DeactivateUpdate();

                if (!_stealWarningRaised)
                {
                    _stealWarningRaised = true;
                    ParentStealDetected?.Invoke();
                }
            }
        }
        #endregion
        #region Driving API
        /// <summary>
        /// Drives the seized rig — and through it the flight camera — to a world
        /// pose: the rig is placed at <paramref name="worldPosition"/> with
        /// <paramref name="worldRotation"/>, and the camera's local pose under
        /// the rig is reset to identity so the camera's world pose becomes
        /// exactly the requested pose. The camera controller (P1-C5) calls this
        /// once per evaluation while holding; the rig keeps the stock camera's
        /// own transform untouched except for parenting, so the snapshot's
        /// local pos/rot restore on release stays exact (G-P1c).
        ///
        /// Drive writes are gated on CaptureCameraResolver.IsIvaMode() per the
        /// P1-C3 D-2(B) ruling (drive writes gated; Release stays exempt).
        /// Returns false without writing while IVA is active or when not
        /// holding. When the flight camera has vanished while holding, releases
        /// and returns false (same self-release pattern as AssertControl).
        /// </summary>
        /// <param name="worldPosition">World position the seized camera must take.</param>
        /// <param name="worldRotation">World rotation the seized camera must take.</param>
        /// <returns>True when the pose was written; false otherwise.</returns>
        public bool SetRigWorldPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            if (!_holding) return false;
            if (CaptureCameraResolver.IsIvaMode()) return false;

            FlightCamera flightCamera = FlightCamera.fetch;
            if (flightCamera == null || _rigObject == null)
            {
                Release();
                return false;
            }

            _rigObject.transform.SetPositionAndRotation(worldPosition, worldRotation);
            flightCamera.transform.localPosition = Vector3.zero;
            flightCamera.transform.localRotation = Quaternion.identity;
            return true;
        }
        #endregion
        #region Unity Lifecycle
        void OnEnable()
        {
            GameEvents.onGameSceneLoadRequested.Add(OnGameSceneLoadRequested);
        }

        void OnDisable()
        {
            GameEvents.onGameSceneLoadRequested.Remove(OnGameSceneLoadRequested);
        }

        void OnDestroy()
        {
            Release();

            if (s_instance == this)
                s_instance = null;
        }
        #endregion
        #region Event Handlers
        private void OnGameSceneLoadRequested(GameScenes scene)
        {
            Release();
        }
        #endregion
        #region Private Helpers
        private Transform EnsureRig()
        {
            if (_rigObject == null)
            {
                _rigObject = new GameObject("CinematicRecorder_CameraRig");
                _rigObject.transform.SetParent(transform, false);
            }
            return _rigObject.transform;
        }
        #endregion
    }
}
