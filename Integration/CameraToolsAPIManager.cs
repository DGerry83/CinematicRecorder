using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Wrapper for CameraTools.ModIntegration.CinematicRecorderIntegration API.
    /// Provides type-safe access and event management for CinematicRecorder.
    /// </summary>
    public static class CameraToolsAPIManager
    {

        #region Fields
        private static Type _integrationType;
        private static Type _toolModesType;
        private static Type _cameraToolsStateType;

        private static System.Reflection.MethodInfo _setToolModeMethod;
        private static System.Reflection.MethodInfo _setStationaryPositionMethod;
        private static System.Reflection.MethodInfo _setStationaryFlagsMethod;
        private static System.Reflection.MethodInfo _setManualOffsetMethod;
        private static System.Reflection.MethodInfo _setStationaryAdvancedMethod;
        private static System.Reflection.MethodInfo _setTargetMethod;
        private static System.Reflection.MethodInfo _setDogfightConfigMethod;
        private static System.Reflection.MethodInfo _setDogfightTargetMethod;
        private static System.Reflection.MethodInfo _setPathStateMethod;
        private static System.Reflection.MethodInfo _setPathTimingMethod;
        private static System.Reflection.MethodInfo _selectPathMethod;
        private static System.Reflection.MethodInfo _setPathingStartKeyframeMethod;
        private static System.Reflection.MethodInfo _setLockPathingToPlaybackRateMethod;
        private static System.Reflection.MethodInfo _setCinematicRecorderControlMethod;
        private static System.Reflection.MethodInfo _setExternalFOVMethod;
        private static System.Reflection.MethodInfo _isAvailableMethod;

        private static System.Reflection.MethodInfo _getToolModeMethod;
        private static System.Reflection.MethodInfo _isCameraActiveMethod;
        private static System.Reflection.MethodInfo _getActualFOVMethod;
        private static System.Reflection.MethodInfo _getManualFOVMethod;
        private static System.Reflection.MethodInfo _getCurrentPathTimeMethod;
        private static System.Reflection.MethodInfo _getCurrentStateMethod;
        private static System.Reflection.MethodInfo _pathExistsMethod;

        private static System.Reflection.MethodInfo _activateCameraMethod;
        private static System.Reflection.MethodInfo _deactivateCameraMethod;
        private static System.Reflection.MethodInfo _startPathPlaybackMethod;
        private static System.Reflection.MethodInfo _stopPathPlaybackMethod;
        private static System.Reflection.MethodInfo _physicsStepUpdateMethod;
        private static System.Reflection.MethodInfo _switchCameraMethod;

        private static System.Reflection.EventInfo _onCameraActivatedEvent;
        private static System.Reflection.EventInfo _onCameraDeactivatedEvent;
        private static System.Reflection.EventInfo _onPathingStartedEvent;
        private static System.Reflection.EventInfo _onPathingStoppedEvent;
        private static System.Reflection.EventInfo _onCinematicRecorderControlTakenEvent;

        private static Delegate _cameraActivatedHandler;
        private static Delegate _cameraDeactivatedHandler;
        private static Delegate _pathingStartedHandler;
        private static Delegate _pathingStoppedHandler;
        private static Delegate _cinematicRecorderControlTakenHandler;

        private static bool _initialized;
        private static bool _isAvailable;
        private static object _lastState;
        #endregion
        #region Events
        public static event Action OnCameraActivated;
        public static event Action OnCameraDeactivated;
        public static event Action OnPathingStarted;
        public static event Action OnPathingStopped;
        public static event Action OnCinematicRecorderControlTaken;
        #endregion
        #region Static Initialization
        private static void Initialize()
        {
            if (_initialized) return;

            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                System.Reflection.Assembly ctAssembly = null;

                foreach (var asm in assemblies)
                {
                    if (asm.GetName().Name == "CameraTools")
                    {
                        ctAssembly = asm;
                        break;
                    }
                }

                if (ctAssembly == null)
                {
                    _isAvailable = false;
                    _initialized = true;
                    return;
                }

                _integrationType = ctAssembly.GetType("CameraTools.ModIntegration.CinematicRecorderIntegration");
                _toolModesType = ctAssembly.GetType("CameraTools.ToolModes");
                _cameraToolsStateType = ctAssembly.GetType("CameraTools.ModIntegration.CameraToolsState");

                if (_integrationType == null)
                {
                    Debug.LogWarning("[CameraToolsAPIManager] CinematicRecorderIntegration type not found.");
                    _isAvailable = false;
                    _initialized = true;
                    return;
                }

                _isAvailableMethod = _integrationType.GetProperty("IsAvailable")?.GetGetMethod();
                _setToolModeMethod = _integrationType.GetMethod("SetToolMode", new[] { _toolModesType });
                _setStationaryPositionMethod = _integrationType.GetMethod("SetStationaryPosition", new[] { typeof(Vector3), typeof(Part) });
                _setStationaryFlagsMethod = _integrationType.GetMethod("SetStationaryFlags", new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                _setManualOffsetMethod = _integrationType.GetMethod("SetManualOffset", new[] { typeof(float), typeof(float), typeof(float) });
                _setStationaryAdvancedMethod = _integrationType.GetMethod("SetStationaryAdvanced", new[] { typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                _setTargetMethod = _integrationType.GetMethod("SetTarget", new[] { typeof(Part), typeof(bool) });
                _setDogfightConfigMethod = _integrationType.GetMethod("SetDogfightConfig", new[] { typeof(float), typeof(float), typeof(float), typeof(bool) });
                _setDogfightTargetMethod = _integrationType.GetMethod("SetDogfightTarget", new[] { typeof(Vessel) });
                _setPathStateMethod = _integrationType.GetMethod("SetPathState", new[] { typeof(int), typeof(int), typeof(bool), typeof(float) });
                _setPathTimingMethod = _integrationType.GetMethod("SetPathTiming", new[] { typeof(bool), typeof(float) });
                _selectPathMethod = _integrationType.GetMethod("SelectPath", new[] { typeof(int) });
                _pathExistsMethod = _integrationType.GetMethod("PathExists", new[] { typeof(int) });
                _setPathingStartKeyframeMethod = _integrationType.GetMethod("SetPathingStartKeyframe", new[] { typeof(int) });
                _setLockPathingToPlaybackRateMethod = _integrationType.GetMethod("SetLockPathingToPlaybackRate", new[] { typeof(bool) });
                _setCinematicRecorderControlMethod = _integrationType.GetMethod("SetCinematicRecorderControl", new[] { typeof(bool), typeof(bool) });
                _setExternalFOVMethod = _integrationType.GetMethod("SetExternalFOV", new[] { typeof(float) });

                _getToolModeMethod = _integrationType.GetMethod("GetToolMode", Type.EmptyTypes);
                _isCameraActiveMethod = _integrationType.GetMethod("IsCameraActive", Type.EmptyTypes);
                _getActualFOVMethod = _integrationType.GetMethod("GetActualFOV", Type.EmptyTypes);
                _getManualFOVMethod = _integrationType.GetMethod("GetManualFOV", Type.EmptyTypes);
                _getCurrentPathTimeMethod = _integrationType.GetMethod("GetCurrentPathTime", Type.EmptyTypes);
                _getCurrentStateMethod = _integrationType.GetMethod("GetCurrentState", Type.EmptyTypes);

                _activateCameraMethod = _integrationType.GetMethod("ActivateCamera", Type.EmptyTypes);
                _deactivateCameraMethod = _integrationType.GetMethod("DeactivateCamera", Type.EmptyTypes);
                _startPathPlaybackMethod = _integrationType.GetMethod("StartPathPlayback", Type.EmptyTypes);
                _stopPathPlaybackMethod = _integrationType.GetMethod("StopPathPlayback", Type.EmptyTypes);
                _physicsStepUpdateMethod = _integrationType.GetMethod("PhysicsStepUpdate", new[] { typeof(float), typeof(float) });
                _switchCameraMethod = _integrationType.GetMethod("SwitchCamera", new[] { _toolModesType });

                _onCameraActivatedEvent = _integrationType.GetEvent("OnCameraActivated");
                _onCameraDeactivatedEvent = _integrationType.GetEvent("OnCameraDeactivated");
                _onPathingStartedEvent = _integrationType.GetEvent("OnPathingStarted");
                _onPathingStoppedEvent = _integrationType.GetEvent("OnPathingStopped");
                _onCinematicRecorderControlTakenEvent = _integrationType.GetEvent("OnCinematicRecorderControlTaken");

                SubscribeToEvents();

                _isAvailable = _isAvailableMethod != null;

                if (_isAvailable)
                {
                    Debug.Log("[CameraToolsAPIManager] CameraTools API v2.0+ detected and bound successfully.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] Initialization failed: {ex}");
                _isAvailable = false;
            }

            _initialized = true;
        }
        private static void SubscribeToEvents()
        {
            if (_onCameraActivatedEvent != null)
            {
                _cameraActivatedHandler = Delegate.CreateDelegate(_onCameraActivatedEvent.EventHandlerType,
                    null, typeof(CameraToolsAPIManager).GetMethod("OnCameraActivatedInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _onCameraActivatedEvent.AddEventHandler(null, _cameraActivatedHandler);
            }

            if (_onCameraDeactivatedEvent != null)
            {
                _cameraDeactivatedHandler = Delegate.CreateDelegate(_onCameraDeactivatedEvent.EventHandlerType,
                    null, typeof(CameraToolsAPIManager).GetMethod("OnCameraDeactivatedInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _onCameraDeactivatedEvent.AddEventHandler(null, _cameraDeactivatedHandler);
            }

            if (_onPathingStartedEvent != null)
            {
                _pathingStartedHandler = Delegate.CreateDelegate(_onPathingStartedEvent.EventHandlerType,
                    null, typeof(CameraToolsAPIManager).GetMethod("OnPathingStartedInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _onPathingStartedEvent.AddEventHandler(null, _pathingStartedHandler);
            }

            if (_onPathingStoppedEvent != null)
            {
                _pathingStoppedHandler = Delegate.CreateDelegate(_onPathingStoppedEvent.EventHandlerType,
                    null, typeof(CameraToolsAPIManager).GetMethod("OnPathingStoppedInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _onPathingStoppedEvent.AddEventHandler(null, _pathingStoppedHandler);
            }

            if (_onCinematicRecorderControlTakenEvent != null)
            {
                _cinematicRecorderControlTakenHandler = Delegate.CreateDelegate(_onCinematicRecorderControlTakenEvent.EventHandlerType,
                    null, typeof(CameraToolsAPIManager).GetMethod("OnControlTakenInternal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _onCinematicRecorderControlTakenEvent.AddEventHandler(null, _cinematicRecorderControlTakenHandler);
            }
        }
        private static void OnCameraActivatedInternal() => OnCameraActivated?.Invoke();
        private static void OnCameraDeactivatedInternal() => OnCameraDeactivated?.Invoke();
        private static void OnPathingStartedInternal() => OnPathingStarted?.Invoke();
        private static void OnPathingStoppedInternal() => OnPathingStopped?.Invoke();
        private static void OnControlTakenInternal() => OnCinematicRecorderControlTaken?.Invoke();
        #endregion
        public static bool IsAvailable
        {
            get
            {
                if (!_initialized) Initialize();
                return _isAvailable;
            }
        }
        #region Public API - State Getters
        public static ToolModes GetToolMode()
        {
            if (!IsAvailable || _getToolModeMethod == null) return ToolModes.StationaryCamera;
            try
            {
                object result = _getToolModeMethod.Invoke(null, null);
                return (ToolModes)Convert.ToInt32(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] GetToolMode failed: {ex.Message}");
                return ToolModes.StationaryCamera;
            }
        }
        public static bool IsCameraActive()
        {
            if (!IsAvailable || _isCameraActiveMethod == null) return false;
            try
            {
                return (bool)_isCameraActiveMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] IsCameraActive failed: {ex.Message}");
                return false;
            }
        }
        public static float GetActualFOV()
        {
            if (!IsAvailable || _getActualFOVMethod == null) return 60f;
            try
            {
                return (float)_getActualFOVMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] GetActualFOV failed: {ex.Message}");
                return 60f;
            }
        }
        public static float GetManualFOV()
        {
            if (!IsAvailable || _getManualFOVMethod == null) return 60f;
            try
            {
                return (float)_getManualFOVMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] GetManualFOV failed: {ex.Message}");
                return 60f;
            }
        }
        public static float GetCurrentPathTime()
        {
            if (!IsAvailable || _getCurrentPathTimeMethod == null) return 0f;
            try
            {
                return (float)_getCurrentPathTimeMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] GetCurrentPathTime failed: {ex.Message}");
                return 0f;
            }
        }
        #endregion
        #region Public API - Configuration Setters
        public static void SetToolMode(ToolModes mode)
        {
            if (!IsAvailable || _setToolModeMethod == null || _toolModesType == null) return;
            try
            {
                object ctMode = Enum.ToObject(_toolModesType, (int)mode);
                _setToolModeMethod.Invoke(null, new[] { ctMode });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetToolMode failed: {ex.Message}");
            }
        }
        public static void SetStationaryPosition(Vector3 position, Part target = null)
        {
            if (!IsAvailable || _setStationaryPositionMethod == null) return;
            try
            {
                _setStationaryPositionMethod.Invoke(null, new object[] { position, target });
                Debug.Log($"[CameraToolsAPIManager] SetStationaryPosition: {position}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetStationaryPosition failed: {ex.Message}");
            }
        }
        public static void SetStationaryFlags(bool presetOffset, bool autoFlyby, bool autoLanding, bool manualOffset)
        {
            if (!IsAvailable || _setStationaryFlagsMethod == null) return;
            try
            {
                _setStationaryFlagsMethod.Invoke(null, new object[] { presetOffset, autoFlyby, autoLanding, manualOffset });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetStationaryFlags failed: {ex.Message}");
            }
        }
        public static void SetManualOffset(float forward, float right, float up)
        {
            if (!IsAvailable || _setManualOffsetMethod == null) return;
            try
            {
                _setManualOffsetMethod.Invoke(null, new object[] { forward, right, up });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetManualOffset failed: {ex.Message}");
            }
        }
        public static void SetStationaryAdvanced(bool saveRot, bool maintainVel, bool useOrb, bool autoZoom)
        {
            if (!IsAvailable || _setStationaryAdvancedMethod == null) return;
            try
            {
                _setStationaryAdvancedMethod.Invoke(null, new object[] { saveRot, maintainVel, useOrb, autoZoom });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetStationaryAdvanced failed: {ex.Message}");
            }
        }
        public static void SetTarget(Part target, bool useCoM)
        {
            if (!IsAvailable || _setTargetMethod == null) return;
            try
            {
                _setTargetMethod.Invoke(null, new object[] { target, useCoM });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetTarget failed: {ex.Message}");
            }
        }
        public static void SetDogfightConfig(float distance, float offsetX, float offsetY, bool chasePlane)
        {
            if (!IsAvailable || _setDogfightConfigMethod == null) return;
            try
            {
                _setDogfightConfigMethod.Invoke(null, new object[] { distance, offsetX, offsetY, chasePlane });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetDogfightConfig failed: {ex.Message}");
            }
        }
        public static void SetDogfightTarget(Vessel target)
        {
            if (!IsAvailable || _setDogfightTargetMethod == null) return;
            try
            {
                _setDogfightTargetMethod.Invoke(null, new object[] { target });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetDogfightTarget failed: {ex.Message}");
            }
        }
        public static void SelectPath(int index)
        {
            if (!IsAvailable || _selectPathMethod == null) return;
            try
            {
                _selectPathMethod.Invoke(null, new object[] { index });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SelectPath failed: {ex.Message}");
            }
        }
        public static bool PathExists(int index)
        {
            if (!IsAvailable || _pathExistsMethod == null) return false;
            try
            {
                return (bool)_pathExistsMethod.Invoke(null, new object[] { index });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] PathExists failed: {ex.Message}");
                return false;
            }
        }
        public static void SetPathState(int pathIndex, int keyframeIndex, bool isPlaying, float startTime)
        {
            if (!IsAvailable || _setPathStateMethod == null) return;
            try
            {
                _setPathStateMethod.Invoke(null, new object[] { pathIndex, keyframeIndex, isPlaying, startTime });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetPathState failed: {ex.Message}");
            }
        }
        public static void SetPathTiming(bool useRealTime, float smoothing)
        {
            if (!IsAvailable || _setPathTimingMethod == null) return;
            try
            {
                _setPathTimingMethod.Invoke(null, new object[] { useRealTime, smoothing });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetPathTiming failed: {ex.Message}");
            }
        }
        public static void SetPathingStartKeyframe(int index)
        {
            if (!IsAvailable || _setPathingStartKeyframeMethod == null) return;
            try
            {
                _setPathingStartKeyframeMethod.Invoke(null, new object[] { index });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetPathingStartKeyframe failed: {ex.Message}");
            }
        }
        public static void SetLockPathingToPlaybackRate(bool usePlaybackTime)
        {
            if (!IsAvailable || _setLockPathingToPlaybackRateMethod == null) return;
            try
            {
                _setLockPathingToPlaybackRateMethod.Invoke(null, new object[] { usePlaybackTime });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetLockPathingToPlaybackRate failed: {ex.Message}");
            }
        }
        public static void SetCinematicRecorderControl(bool enabled, bool deterministicMode)
        {
            if (!IsAvailable || _setCinematicRecorderControlMethod == null) return;
            try
            {
                _setCinematicRecorderControlMethod.Invoke(null, new object[] { enabled, deterministicMode });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetCinematicRecorderControl failed: {ex.Message}");
            }
        }
        public static void SetExternalFOV(float fov)
        {
            if (!IsAvailable || _setExternalFOVMethod == null) return;
            try
            {
                _setExternalFOVMethod.Invoke(null, new object[] { fov });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SetExternalFOV failed: {ex.Message}");
            }
        }
        #endregion
        #region Public API - Activation
        public static void ActivateCamera()
        {
            if (!IsAvailable || _activateCameraMethod == null) return;
            try
            {
                _activateCameraMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] ActivateCamera failed: {ex.Message}");
            }
        }
        public static void SwitchCamera(ToolModes mode)
        {
            if (!IsAvailable || _switchCameraMethod == null || _toolModesType == null) return;
            try
            {
                object ctMode = Enum.ToObject(_toolModesType, (int)mode);
                _switchCameraMethod.Invoke(null, new[] { ctMode });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] SwitchCamera failed: {ex.Message}");
            }
        }
        public static void DeactivateCamera()
        {
            if (!IsAvailable || _deactivateCameraMethod == null) return;
            try
            {
                _deactivateCameraMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] DeactivateCamera failed: {ex.Message}");
            }
        }
        public static void StartPathPlayback()
        {
            if (!IsAvailable || _startPathPlaybackMethod == null) return;
            try
            {
                _startPathPlaybackMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] StartPathPlayback failed: {ex.Message}");
            }
        }
        public static void StopPathPlayback()
        {
            if (!IsAvailable || _stopPathPlaybackMethod == null) return;
            try
            {
                _stopPathPlaybackMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] StopPathPlayback failed: {ex.Message}");
            }
        }
        public static void PhysicsStepUpdate(float physicsDeltaTime, float playbackDeltaTime)
        {
            if (!IsAvailable || _physicsStepUpdateMethod == null) return;
            try
            {
                _physicsStepUpdateMethod.Invoke(null, new object[] { physicsDeltaTime, playbackDeltaTime });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] PhysicsStepUpdate failed: {ex.Message}");
            }
        }
        #endregion
        #region Public API - Legacy State
        public static object GetCurrentState()
        {
            if (!IsAvailable || _getCurrentStateMethod == null) return null;
            try
            {
                _lastState = _getCurrentStateMethod.Invoke(null, null);
                return _lastState;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAPIManager] GetCurrentState failed: {ex.Message}");
                return null;
            }
        }
        public static ToolModes GetCurrentModeFromState()
        {
            if (_lastState == null || _cameraToolsStateType == null) return ToolModes.StationaryCamera;
            try
            {
                var modeField = _cameraToolsStateType.GetField("Mode");
                if (modeField == null) return ToolModes.StationaryCamera;
                object modeValue = modeField.GetValue(_lastState);
                return (ToolModes)Convert.ToInt32(modeValue);
            }
            catch
            {
                return ToolModes.StationaryCamera;
            }
        }
        public static bool GetIsPlayingPathFromState()
        {
            if (_lastState == null || _cameraToolsStateType == null) return false;
            try
            {
                var field = _cameraToolsStateType.GetField("IsPlayingPath");
                if (field == null) return false;
                return (bool)field.GetValue(_lastState);
            }
            catch
            {
                return false;
            }
        }
        public static float GetCurrentPathTimeFromState()
        {
            if (_lastState == null || _cameraToolsStateType == null) return 0f;
            try
            {
                var field = _cameraToolsStateType.GetField("PathStartTime") ?? _cameraToolsStateType.GetField("CurrentPathTime");
                if (field == null) return 0f;
                return (float)field.GetValue(_lastState);
            }
            catch
            {
                return 0f;
            }
        }
        #endregion
        #region Public API - Lifecycle
        public static void Shutdown()
        {
            if (!_initialized) return;
            try
            {
                if (_cameraActivatedHandler != null && _onCameraActivatedEvent != null)
                    _onCameraActivatedEvent.RemoveEventHandler(null, _cameraActivatedHandler);
                if (_cameraDeactivatedHandler != null && _onCameraDeactivatedEvent != null)
                    _onCameraDeactivatedEvent.RemoveEventHandler(null, _cameraDeactivatedHandler);
                if (_pathingStartedHandler != null && _onPathingStartedEvent != null)
                    _onPathingStartedEvent.RemoveEventHandler(null, _pathingStartedHandler);
                if (_pathingStoppedHandler != null && _onPathingStoppedEvent != null)
                    _onPathingStoppedEvent.RemoveEventHandler(null, _pathingStoppedHandler);
                if (_cinematicRecorderControlTakenHandler != null && _onCinematicRecorderControlTakenEvent != null)
                    _onCinematicRecorderControlTakenEvent.RemoveEventHandler(null, _cinematicRecorderControlTakenHandler);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CameraToolsAPIManager] Error during shutdown: {ex.Message}");
            }
        }
        #endregion
    }
}