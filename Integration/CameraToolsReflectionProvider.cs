using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Low-level reflection provider for CameraTools.
    /// Uses public field access for API v2.0+ exposed fields and reflection for remaining members.
    /// </summary>
    public static class CameraToolsReflectionProvider
    {
        #region Fields
        private static bool _initialized;
        private static bool _isAvailable;

        private static Assembly _ctAssembly;
        private static Type _camToolsType;
        private static Type _toolModesEnumType;
        private static Type _fmPivotModeEnumType;

        private static FieldInfo _fetchField;

        private static FieldInfo _toolModeField;
        private static FieldInfo _cameraToolActiveField;
        private static FieldInfo _vesselField;
        private static FieldInfo _dogfightDistanceField;
        private static FieldInfo _dogfightOffsetXField;
        private static FieldInfo _dogfightOffsetYField;
        private static FieldInfo _dogfightTargetField;
        private static FieldInfo _dogfightChasePlaneModeField;
        private static FieldInfo _autoZoomStationaryField;
        private static FieldInfo _selectedPathIndexField;
        private static FieldInfo _availablePathsField;
        private static FieldInfo _isPlayingPathField;
        private static FieldInfo _currentKeyframeIndexField;
        private static FieldInfo _useRealTimeField;
        private static FieldInfo _pathStartTimeField;
        private static FieldInfo _autoFlybyPositionField;
        private static FieldInfo _manualOffsetField;
        private static FieldInfo _manualOffsetForwardField;
        private static FieldInfo _manualOffsetRightField;
        private static FieldInfo _manualOffsetUpField;
        private static FieldInfo _autoLandingPositionField;
        private static FieldInfo _targetCoMField;
        private static FieldInfo _maintainInitialVelocityField;
        private static FieldInfo _useOrbitalField;
        private static FieldInfo _saveRotationField;
        private static FieldInfo _fmPivotModeField;
        private static FieldInfo _pathingSecondarySmoothingField;
        private static FieldInfo _autoZoomMarginStationaryField;
        private static FieldInfo _zoomExpStationaryField;
        private static FieldInfo _initialVelocityField;

        private static FieldInfo _manualPositionField;
        private static FieldInfo _manualFOVField;
        private static FieldInfo _currentFOVField;
        private static FieldInfo _hasTargetField;
        private static FieldInfo _camTargetField;
        private static FieldInfo _cameraParentField;
        private static FieldInfo _lastVesselCoMField;

        private static FieldInfo _setPresetOffsetField;
        private static FieldInfo _presetOffsetField;

        private static MethodInfo _cameraActivateMethod;
        private static MethodInfo _revertCameraMethod;
        #endregion
        #region Static Initialization
        static CameraToolsReflectionProvider()
        {
            Initialize();
        }
        private static bool Initialize()
        {
            if (_initialized) return _isAvailable;

            try
            {
                _ctAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CameraTools");

                if (_ctAssembly == null)
                {
                    _isAvailable = false;
                    _initialized = true;
                    return false;
                }

                _camToolsType = _ctAssembly.GetType("CameraTools.CamTools");
                _toolModesEnumType = _ctAssembly.GetType("CameraTools.ToolModes");
                _fmPivotModeEnumType = _ctAssembly.GetType("CameraTools.FMModeTypes");

                if (_camToolsType == null)
                {
                    _isAvailable = false;
                    _initialized = true;
                    return false;
                }

                _fetchField = _camToolsType.GetField("fetch", BindingFlags.Public | BindingFlags.Static);

                _toolModeField = _camToolsType.GetField("toolMode", BindingFlags.Public | BindingFlags.Instance);
                _cameraToolActiveField = _camToolsType.GetField("cameraToolActive", BindingFlags.Public | BindingFlags.Instance);
                _vesselField = _camToolsType.GetField("vessel", BindingFlags.Public | BindingFlags.Instance);
                _dogfightDistanceField = _camToolsType.GetField("dogfightDistance", BindingFlags.Public | BindingFlags.Instance);
                _dogfightOffsetXField = _camToolsType.GetField("dogfightOffsetX", BindingFlags.Public | BindingFlags.Instance);
                _dogfightOffsetYField = _camToolsType.GetField("dogfightOffsetY", BindingFlags.Public | BindingFlags.Instance);
                _dogfightTargetField = _camToolsType.GetField("dogfightTarget", BindingFlags.Public | BindingFlags.Instance);
                _dogfightChasePlaneModeField = _camToolsType.GetField("dogfightChasePlaneMode", BindingFlags.Public | BindingFlags.Instance);
                _autoZoomStationaryField = _camToolsType.GetField("autoZoomStationary", BindingFlags.Public | BindingFlags.Instance);
                _autoFlybyPositionField = _camToolsType.GetField("autoFlybyPosition", BindingFlags.Public | BindingFlags.Instance);
                _manualOffsetField = _camToolsType.GetField("manualOffset", BindingFlags.Public | BindingFlags.Instance);
                _manualOffsetForwardField = _camToolsType.GetField("manualOffsetForward", BindingFlags.Public | BindingFlags.Instance);
                _manualOffsetRightField = _camToolsType.GetField("manualOffsetRight", BindingFlags.Public | BindingFlags.Instance);
                _manualOffsetUpField = _camToolsType.GetField("manualOffsetUp", BindingFlags.Public | BindingFlags.Instance);
                _autoLandingPositionField = _camToolsType.GetField("autoLandingPosition", BindingFlags.Public | BindingFlags.Instance);
                _targetCoMField = _camToolsType.GetField("targetCoM", BindingFlags.Public | BindingFlags.Instance);
                _maintainInitialVelocityField = _camToolsType.GetField("maintainInitialVelocity", BindingFlags.Public | BindingFlags.Instance);
                _useOrbitalField = _camToolsType.GetField("useOrbital", BindingFlags.Public | BindingFlags.Instance);
                _saveRotationField = _camToolsType.GetField("saveRotation", BindingFlags.Public | BindingFlags.Instance);
                _fmPivotModeField = _camToolsType.GetField("fmPivotMode", BindingFlags.Public | BindingFlags.Instance);
                _pathingSecondarySmoothingField = _camToolsType.GetField("pathingSecondarySmoothing", BindingFlags.Public | BindingFlags.Instance);
                _selectedPathIndexField = _camToolsType.GetField("selectedPathIndex", BindingFlags.Public | BindingFlags.Instance);
                _availablePathsField = _camToolsType.GetField("availablePaths", BindingFlags.Public | BindingFlags.Instance);
                _isPlayingPathField = _camToolsType.GetField("isPlayingPath", BindingFlags.Public | BindingFlags.Instance);
                _currentKeyframeIndexField = _camToolsType.GetField("currentKeyframeIndex", BindingFlags.Public | BindingFlags.Instance);
                _useRealTimeField = _camToolsType.GetField("useRealTime", BindingFlags.Public | BindingFlags.Instance);
                _pathStartTimeField = _camToolsType.GetField("pathStartTime", BindingFlags.Public | BindingFlags.Instance);
                _autoZoomMarginStationaryField = _camToolsType.GetField("autoZoomMarginStationary", BindingFlags.Public | BindingFlags.Instance);
                _zoomExpStationaryField = _camToolsType.GetField("zoomExpStationary", BindingFlags.Public | BindingFlags.Instance);
                _initialVelocityField = _camToolsType.GetField("initialVelocity", BindingFlags.Public | BindingFlags.Instance);

                _manualPositionField = _camToolsType.GetField("manualPosition", BindingFlags.Public | BindingFlags.Instance);
                _manualFOVField = _camToolsType.GetField("manualFOV", BindingFlags.Public | BindingFlags.Instance);
                _currentFOVField = _camToolsType.GetField("currentFOV", BindingFlags.Public | BindingFlags.Instance);
                _hasTargetField = _camToolsType.GetField("hasTarget", BindingFlags.Public | BindingFlags.Instance);
                _camTargetField = _camToolsType.GetField("camTarget", BindingFlags.Public | BindingFlags.Instance);
                _cameraParentField = _camToolsType.GetField("cameraParent", BindingFlags.Public | BindingFlags.Instance);
                _lastVesselCoMField = _camToolsType.GetField("lastVesselCoM", BindingFlags.Public | BindingFlags.Instance);
                _setPresetOffsetField = _camToolsType.GetField("setPresetOffset", BindingFlags.Public | BindingFlags.Instance);

                _presetOffsetField = _camToolsType.GetField("presetOffset", BindingFlags.NonPublic | BindingFlags.Instance);

                _cameraActivateMethod = _camToolsType.GetMethod("CameraActivate", BindingFlags.Public | BindingFlags.Instance);
                _revertCameraMethod = _camToolsType.GetMethod("RevertCamera", BindingFlags.Public | BindingFlags.Instance);

                _isAvailable = _fetchField != null && _toolModeField != null && _cameraActivateMethod != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsReflectionProvider] Init failed: {ex}");
                _isAvailable = false;
            }

            _initialized = true;
            return _isAvailable;
        }
        #endregion
        #region Public API - Properties
        public static bool IsAvailable => _initialized ? _isAvailable : Initialize();
        #endregion
        #region Direct Field Access
        /// <summary>
        /// Direct access to manualPosition (public field in CameraTools v2.0+)
        /// </summary>
        public static Vector3 ManualPosition
        {
            get => GetField(_manualPositionField, Vector3.zero);
            set => SetField(_manualPositionField, value);
        }

        /// <summary>
        /// Direct access to manualFOV (public field in CameraTools v2.0+).
        /// Note: Prefer CameraToolsAPIManager.SetExternalFOV() for immediate FOV application without smoothing.
        /// </summary>
        public static float ManualFOV
        {
            get => GetField(_manualFOVField, 60f);
            set => SetField(_manualFOVField, value);
        }

        /// <summary>
        /// Direct access to currentFOV (public field in CameraTools v2.0+)
        /// </summary>
        public static float CurrentFOV
        {
            get => GetField(_currentFOVField, 60f);
            set => SetField(_currentFOVField, value);
        }

        /// <summary>
        /// Direct access to hasTarget (public field in CameraTools v2.0+)
        /// </summary>
        public static bool HasTarget
        {
            get => GetField(_hasTargetField, false);
            set => SetField(_hasTargetField, value);
        }

        /// <summary>
        /// Direct access to camTarget (public field in CameraTools API v2.0+)
        /// </summary>
        public static Part CamTarget
        {
            get => GetReference<Part>(_camTargetField);
            set => SetReference(_camTargetField, value);
        }

        /// <summary>
        /// Direct access to cameraParent GameObject (public field in CameraTools v2.0+)
        /// </summary>
        public static GameObject CameraParent
        {
            get => GetReference<GameObject>(_cameraParentField);
            set => SetReference(_cameraParentField, value);
        }

        /// <summary>
        /// Direct access to lastVesselCoM (public field in CameraTools v2.0+)
        /// </summary>
        public static Vector3 LastVesselCoM
        {
            get => GetField(_lastVesselCoMField, Vector3.zero);
            set => SetField(_lastVesselCoMField, value);
        }

        /// <summary>
        /// Direct access to setPresetOffset (public field in CameraTools API v2.0+)
        /// </summary>
        public static bool SetPresetOffset
        {
            get => GetField(_setPresetOffsetField, false);
            set => SetField(_setPresetOffsetField, value);
        }

        /// <summary>
        /// Direct access to isPlayingPath (public field in CameraTools v2.0+)
        /// </summary>
        public static bool IsPlayingPath => GetField(_isPlayingPathField, false);
        #endregion
        #region Generic Accessors
        // Generic Field Accessors
        public static T GetField<T>(FieldInfo field, T defaultValue = default)
        {
            object instance = GetFetchInstance();
            if (instance == null || field == null) return defaultValue;
            try { return (T)field.GetValue(instance); }
            catch { return defaultValue; }
        }
        public static void SetField<T>(FieldInfo field, T value)
        {
            object instance = GetFetchInstance();
            if (instance == null || field == null) return;
            try { field.SetValue(instance, value); }
            catch (Exception ex) { Debug.LogWarning($"[CTReflection] SetField failed: {ex.Message}"); }
        }

        // Specialized Accessors
        public static bool GetBool(FieldInfo field, bool defaultValue = false) => GetField(field, defaultValue);
        public static void SetBool(FieldInfo field, bool value) => SetField(field, value);
        public static float GetFloat(FieldInfo field, float defaultValue = 0f) => GetField(field, defaultValue);
        public static void SetFloat(FieldInfo field, float value) => SetField(field, value);
        public static int GetInt(FieldInfo field, int defaultValue = 0) => GetField(field, defaultValue);
        public static void SetInt(FieldInfo field, int value) => SetField(field, value);
        public static Vector3 GetVector3(FieldInfo field, Vector3 defaultValue = default) => GetField(field, defaultValue);
        public static void SetVector3(FieldInfo field, Vector3 value) => SetField(field, value);
        public static T GetReference<T>(FieldInfo field) where T : class => GetField<T>(field, null);
        public static void SetReference<T>(FieldInfo field, T value) where T : class => SetField(field, value);
        #endregion
        #region Public Methods
        public static object GetFetchInstance()
        {
            if (!IsAvailable || _fetchField == null) return null;
            return _fetchField.GetValue(null);
        }
        public static void Activate()
        {
            var instance = GetFetchInstance();
            if (instance != null && _cameraActivateMethod != null)
                _cameraActivateMethod.Invoke(instance, null);
        }
        public static void Revert()
        {
            var instance = GetFetchInstance();
            if (instance != null && _revertCameraMethod != null)
                _revertCameraMethod.Invoke(instance, null);
        }
        public static bool PathExists(int index)
        {
            if (index < 0 || _availablePathsField == null) return false;
            var paths = GetReference<IList>(_availablePathsField);
            return paths != null && index < paths.Count;
        }
        public static object ConvertToCameraToolsToolModes(ToolModes mode)
        {
            if (_toolModesEnumType == null) return null;
            try { return Enum.ToObject(_toolModesEnumType, (int)mode); }
            catch { return null; }
        }
        public static ToolModes ConvertToLocalToolModes(object ctEnumValue)
        {
            if (ctEnumValue == null) return ToolModes.StationaryCamera;
            try { return (ToolModes)Convert.ToInt32(ctEnumValue); }
            catch { return ToolModes.StationaryCamera; }
        }
        public static object ConvertToCameraToolsFMPivotMode(FMPivotMode mode)
        {
            if (_fmPivotModeEnumType == null) return null;
            try { return Enum.ToObject(_fmPivotModeEnumType, (int)mode); }
            catch { return null; }
        }
        public static FMPivotMode ConvertToLocalFMPivotMode(object ctEnumValue)
        {
            if (ctEnumValue == null) return FMPivotMode.Camera;
            try { return (FMPivotMode)Convert.ToInt32(ctEnumValue); }
            catch { return FMPivotMode.Camera; }
        }
        public static float ExtractPathTimeScale(int pathIndex)
        {
            if (pathIndex < 0 || _availablePathsField == null) return 1f;
            try
            {
                var paths = _availablePathsField.GetValue(GetFetchInstance()) as IList;
                if (paths != null && pathIndex < paths.Count)
                {
                    var path = paths[pathIndex];
                    if (path != null)
                    {
                        var timeScaleField = path.GetType().GetField("timeScale");
                        if (timeScaleField != null) return (float)timeScaleField.GetValue(path);
                    }
                }
            }
            catch { }
            return 1f;
        }
        public static void ApplyPathTimeScale(int pathIndex, float timeScale)
        {
            if (pathIndex < 0 || _availablePathsField == null) return;
            try
            {
                var paths = _availablePathsField.GetValue(GetFetchInstance()) as IList;
                if (paths != null && pathIndex < paths.Count)
                {
                    var path = paths[pathIndex];
                    if (path != null)
                    {
                        var timeScaleField = path.GetType().GetField("timeScale");
                        if (timeScaleField != null) timeScaleField.SetValue(path, timeScale);
                    }
                }
            }
            catch { }
        }
        #endregion
        #region Field Exporters
        // Field Accessors for external use
        public static FieldInfo ToolModeField => _toolModeField;
        public static FieldInfo CameraToolActiveField => _cameraToolActiveField;
        public static FieldInfo VesselField => _vesselField;
        public static FieldInfo DogfightDistanceField => _dogfightDistanceField;
        public static FieldInfo DogfightOffsetXField => _dogfightOffsetXField;
        public static FieldInfo DogfightOffsetYField => _dogfightOffsetYField;
        public static FieldInfo DogfightTargetField => _dogfightTargetField;
        public static FieldInfo DogfightChasePlaneModeField => _dogfightChasePlaneModeField;
        public static FieldInfo AutoZoomStationaryField => _autoZoomStationaryField;
        public static FieldInfo SetPresetOffsetField => _setPresetOffsetField;
        public static FieldInfo PresetOffsetField => _presetOffsetField;
        public static FieldInfo AutoFlybyPositionField => _autoFlybyPositionField;
        public static FieldInfo ManualOffsetField => _manualOffsetField;
        public static FieldInfo ManualOffsetForwardField => _manualOffsetForwardField;
        public static FieldInfo ManualOffsetRightField => _manualOffsetRightField;
        public static FieldInfo ManualOffsetUpField => _manualOffsetUpField;
        public static FieldInfo AutoLandingPositionField => _autoLandingPositionField;
        public static FieldInfo TargetCoMField => _targetCoMField;
        public static FieldInfo MaintainInitialVelocityField => _maintainInitialVelocityField;
        public static FieldInfo UseOrbitalField => _useOrbitalField;
        public static FieldInfo SaveRotationField => _saveRotationField;
        public static FieldInfo FmPivotModeField => _fmPivotModeField;
        public static FieldInfo PathingSecondarySmoothingField => _pathingSecondarySmoothingField;
        public static FieldInfo SelectedPathIndexField => _selectedPathIndexField;
        public static FieldInfo IsPlayingPathField => _isPlayingPathField;
        public static FieldInfo CurrentKeyframeIndexField => _currentKeyframeIndexField;
        public static FieldInfo UseRealTimeField => _useRealTimeField;
        public static FieldInfo PathStartTimeField => _pathStartTimeField;
        public static FieldInfo AutoZoomMarginStationaryField => _autoZoomMarginStationaryField;
        public static FieldInfo ZoomExpStationaryField => _zoomExpStationaryField;
        public static FieldInfo InitialVelocityField => _initialVelocityField;

        // Expose the new public field accessors for external use
        public static FieldInfo ManualPositionField => _manualPositionField;
        public static FieldInfo ManualFOVField => _manualFOVField;
        public static FieldInfo CurrentFOVField => _currentFOVField;
        public static FieldInfo HasTargetField => _hasTargetField;
        public static FieldInfo CamTargetField => _camTargetField;
        public static FieldInfo CameraParentField => _cameraParentField;
        public static FieldInfo LastVesselCoMField => _lastVesselCoMField;
        #endregion
    }
}