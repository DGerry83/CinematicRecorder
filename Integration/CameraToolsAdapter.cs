using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Provides a clean, type-safe interface to CameraTools via reflection.
    /// Encapsulates all BindingFlags voodoo and field access patterns.
    /// </summary>
    public class CameraToolsAdapter
    {
        #region Singleton & Initialization
        private static CameraToolsAdapter _instance;
        public static CameraToolsAdapter Instance
        {
            get { return _instance ?? (_instance = new CameraToolsAdapter()); }
        }

        private bool _initialized;
        private bool _isAvailable;

        // Assembly and type references
        private Assembly _ctAssembly;
        private Type _camToolsType;
        private Type _toolModesEnumType;

        // Reflection cache - Static fields
        private FieldInfo _fetchField;

        // Reflection cache - Instance fields (Public)
        private FieldInfo _toolModeField;
        private FieldInfo _cameraToolActiveField;
        private FieldInfo _vesselField;
        private FieldInfo _dogfightDistanceField;
        private FieldInfo _dogfightOffsetXField;
        private FieldInfo _dogfightOffsetYField;
        private FieldInfo _dogfightTargetField;
        private FieldInfo _dogfightChasePlaneModeField;
        private FieldInfo _autoZoomStationaryField;
        private FieldInfo _selectedPathIndexField;
        private FieldInfo _availablePathsField;
        private FieldInfo _isPlayingPathField;
        private FieldInfo _currentKeyframeIndexField;
        private FieldInfo _useRealTimeField;
        private FieldInfo _pathStartTimeField;
        private FieldInfo _autoFlybyPositionField;
        private FieldInfo _manualOffsetField;
        private FieldInfo _manualOffsetForwardField;
        private FieldInfo _manualOffsetRightField;
        private FieldInfo _manualOffsetUpField;
        private FieldInfo _autoLandingPositionField;
        private FieldInfo _targetCoMField;
        private FieldInfo _maintainInitialVelocityField;
        private FieldInfo _useOrbitalField;

        // NEW: Auto-zoom FOV calculation fields (Step 1)
        private FieldInfo _autoZoomMarginStationaryField;
        private FieldInfo _zoomExpStationaryField;


        // Reflection cache - Instance fields (Private - require NonPublic)
        private FieldInfo _manualPositionField;
        private FieldInfo _manualFOVField;
        private FieldInfo _camTargetField;
        private FieldInfo _hasTargetField;
        private FieldInfo _setPresetOffsetField;
        private FieldInfo _presetOffsetField;
        private FieldInfo _saveRotationField;
        private FieldInfo _fmPivotModeField;
        private FieldInfo _pathingSecondarySmoothingField;
        private FieldInfo _initialVelocityField;
        private FieldInfo _currentFOVField;  // NEW: Interpolated FOV value (bypasses 0.1f lerp)

        // Cached fields for post-activation fixup
        private FieldInfo _cameraParentField;
        private FieldInfo _lastVesselCoMField;

        private Type _fmPivotModeEnumType;

        // Reflection cache - Methods
        private MethodInfo _cameraActivateMethod;
        private MethodInfo _revertCameraMethod;

        private CameraToolsAdapter()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            try
            {
                _ctAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "CameraTools");

                if (_ctAssembly == null)
                {
                    Debug.Log("[CameraToolsAdapter] CameraTools assembly not found");
                    _isAvailable = false;
                    _initialized = true;
                    return;
                }

                _camToolsType = _ctAssembly.GetType("CameraTools.CamTools");
                _toolModesEnumType = _ctAssembly.GetType("CameraTools.ToolModes");

                if (_camToolsType == null || _toolModesEnumType == null)
                {
                    Debug.LogWarning("[CameraToolsAdapter] Could not find CamTools or ToolModes types");
                    _isAvailable = false;
                    _initialized = true;
                    return;
                }

                // Bind static fetch field
                _fetchField = _camToolsType.GetField("fetch", BindingFlags.Public | BindingFlags.Static);

                // Bind public instance fields
                _toolModeField = _camToolsType.GetField("toolMode", BindingFlags.Public | BindingFlags.Instance);
                _cameraToolActiveField = _camToolsType.GetField("cameraToolActive", BindingFlags.Public | BindingFlags.Instance);
                _vesselField = _camToolsType.GetField("vessel", BindingFlags.Public | BindingFlags.Instance);

                // Dogfight fields (public)
                _dogfightDistanceField = _camToolsType.GetField("dogfightDistance", BindingFlags.Public | BindingFlags.Instance);
                _dogfightOffsetXField = _camToolsType.GetField("dogfightOffsetX", BindingFlags.Public | BindingFlags.Instance);
                _dogfightOffsetYField = _camToolsType.GetField("dogfightOffsetY", BindingFlags.Public | BindingFlags.Instance);
                _dogfightTargetField = _camToolsType.GetField("dogfightTarget", BindingFlags.Public | BindingFlags.Instance);
                _dogfightChasePlaneModeField = _camToolsType.GetField("dogfightChasePlaneMode", BindingFlags.Public | BindingFlags.Instance);

                // Stationary fields (public)
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

                // Bind auto-zoom calculation fields
                _autoZoomMarginStationaryField = _camToolsType.GetField("autoZoomMarginStationary", BindingFlags.Public | BindingFlags.Instance);
                _zoomExpStationaryField = _camToolsType.GetField("zoomExpStationary", BindingFlags.Public | BindingFlags.Instance);

                // Additional settings fields
                _saveRotationField = _camToolsType.GetField("saveRotation", BindingFlags.Public | BindingFlags.Instance);
                _fmPivotModeField = _camToolsType.GetField("fmPivotMode", BindingFlags.Public | BindingFlags.Instance);
                _pathingSecondarySmoothingField = _camToolsType.GetField("pathingSecondarySmoothing", BindingFlags.Public | BindingFlags.Instance);
                _initialVelocityField = _camToolsType.GetField("initialVelocity", BindingFlags.NonPublic | BindingFlags.Instance);
                _fmPivotModeEnumType = _ctAssembly.GetType("CameraTools.FMModeTypes");

                // Pathing fields (public)
                _selectedPathIndexField = _camToolsType.GetField("selectedPathIndex", BindingFlags.Public | BindingFlags.Instance);
                _availablePathsField = _camToolsType.GetField("availablePaths", BindingFlags.Public | BindingFlags.Instance);
                _isPlayingPathField = _camToolsType.GetField("isPlayingPath", BindingFlags.Public | BindingFlags.Instance);
                _currentKeyframeIndexField = _camToolsType.GetField("currentKeyframeIndex", BindingFlags.Public | BindingFlags.Instance);
                _useRealTimeField = _camToolsType.GetField("useRealTime", BindingFlags.Public | BindingFlags.Instance);
                _pathStartTimeField = _camToolsType.GetField("pathStartTime", BindingFlags.Public | BindingFlags.Instance);

                // CRITICAL: Bind private fields with NonPublic flag
                _manualPositionField = _camToolsType.GetField("manualPosition", BindingFlags.NonPublic | BindingFlags.Instance);
                _manualFOVField = _camToolsType.GetField("manualFOV", BindingFlags.NonPublic | BindingFlags.Instance);
                _currentFOVField = _camToolsType.GetField("currentFOV", BindingFlags.NonPublic | BindingFlags.Instance);
                _camTargetField = _camToolsType.GetField("camTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                _hasTargetField = _camToolsType.GetField("hasTarget", BindingFlags.NonPublic | BindingFlags.Instance);
                _setPresetOffsetField = _camToolsType.GetField("setPresetOffset", BindingFlags.NonPublic | BindingFlags.Instance);
                _presetOffsetField = _camToolsType.GetField("presetOffset", BindingFlags.NonPublic | BindingFlags.Instance);

                // Bind fields for post-activation fixup
                _cameraParentField = _camToolsType.GetField("cameraParent", BindingFlags.NonPublic | BindingFlags.Instance);
                _lastVesselCoMField = _camToolsType.GetField("lastVesselCoM", BindingFlags.NonPublic | BindingFlags.Instance);

                // Bind methods
                _cameraActivateMethod = _camToolsType.GetMethod("CameraActivate", BindingFlags.Public | BindingFlags.Instance);
                _revertCameraMethod = _camToolsType.GetMethod("RevertCamera", BindingFlags.Public | BindingFlags.Instance);

                // Verify critical bindings
                if (_fetchField == null || _toolModeField == null || _cameraActivateMethod == null)
                {
                    Debug.LogWarning("[CameraToolsAdapter] Failed to bind critical CameraTools members");
                    _isAvailable = false;
                }
                else if (_camTargetField == null || _hasTargetField == null)
                {
                    Debug.LogWarning("[CameraToolsAdapter] Failed to bind private target fields - target persistence will fail");
                    _isAvailable = false;
                }
                else
                {
                    _isAvailable = true;
                    Debug.Log("[CameraToolsAdapter] Successfully bound to CameraTools");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraToolsAdapter] Initialization failed: {ex}");
                _isAvailable = false;
            }

            _initialized = true;
        }

        private object GetFetchInstance()
        {
            if (!_isAvailable || _fetchField == null) return null;
            return _fetchField.GetValue(null);
        }

        public bool IsAvailable => _isAvailable;
        #endregion

        #region Public Properties
        public bool IsActive
        {
            get => GetBool(_cameraToolActiveField, false);
            set => SetBool(_cameraToolActiveField, value);
        }

        public ToolModes CurrentMode
        {
            get
            {
                var instance = GetValidatedInstance();
                if (instance == null) return ToolModes.StationaryCamera;
                return ConvertToLocalToolModes(_toolModeField?.GetValue(instance));
            }
            set
            {
                var instance = GetValidatedInstance();
                if (instance == null) return;

                object enumValue = ConvertToCameraToolsToolModes(value);
                if (enumValue != null)
                {
                    SetField(_toolModeField, enumValue);
                }
            }
        }

        public Vessel CurrentVessel
        {
            get => GetReference<Vessel>(_vesselField);
        }

        // Stationary Camera Properties
        public Vector3 ManualPosition
        {
            get => GetVector3(_manualPositionField, Vector3.zero);
            set => SetVector3(_manualPositionField, value);
        }

        public float ManualFOV
        {
            get => GetFloat(_manualFOVField, 60f);
            set => SetFloat(_manualFOVField, value);
        }

        public bool AutoZoomStationary
        {
            get => GetBool(_autoZoomStationaryField, false);
            set => SetBool(_autoZoomStationaryField, value);
        }

        public bool AutoFlybyPosition
        {
            get => GetBool(_autoFlybyPositionField, false);
            set => SetBool(_autoFlybyPositionField, value);
        }

        public bool ManualOffset
        {
            get => GetBool(_manualOffsetField, false);
            set => SetBool(_manualOffsetField, value);
        }

        public float ManualOffsetForward
        {
            get => GetFloat(_manualOffsetForwardField, 500f);
            set => SetFloat(_manualOffsetForwardField, value);
        }

        public float ManualOffsetRight
        {
            get => GetFloat(_manualOffsetRightField, 50f);
            set => SetFloat(_manualOffsetRightField, value);
        }

        public float ManualOffsetUp
        {
            get => GetFloat(_manualOffsetUpField, 5f);
            set => SetFloat(_manualOffsetUpField, value);
        }

        public bool AutoLandingPosition
        {
            get => GetBool(_autoLandingPositionField, false);
            set => SetBool(_autoLandingPositionField, value);
        }

        public bool UsePresetOffset
        {
            get => GetBool(_setPresetOffsetField, false);
            set => SetBool(_setPresetOffsetField, value);
        }

        public Vector3 PresetOffset
        {
            get => GetVector3(_presetOffsetField, Vector3.zero);
            set => SetVector3(_presetOffsetField, value);
        }

        // Target Properties (Private field access)
        public Part CamTarget
        {
            get => GetReference<Part>(_camTargetField);
            set => SetReference(_camTargetField, value);
        }

        public bool HasTarget
        {
            get => GetBool(_hasTargetField, false);
            set => SetBool(_hasTargetField, value);
        }

        public bool TargetCoM
        {
            get => GetBool(_targetCoMField, false);
            set => SetBool(_targetCoMField, value);
        }

        public bool MaintainInitialVelocity
        {
            get => GetBool(_maintainInitialVelocityField, false);
            set => SetBool(_maintainInitialVelocityField, value);
        }

        public bool UseOrbital
        {
            get => GetBool(_useOrbitalField, false);
            set => SetBool(_useOrbitalField, value);
        }

        // Dogfight Properties
        public float DogfightDistance
        {
            get => GetFloat(_dogfightDistanceField, 50f);
            set => SetFloat(_dogfightDistanceField, value);
        }

        public float DogfightOffsetX
        {
            get => GetFloat(_dogfightOffsetXField, 0f);
            set => SetFloat(_dogfightOffsetXField, value);
        }

        public float DogfightOffsetY
        {
            get => GetFloat(_dogfightOffsetYField, 5f);
            set => SetFloat(_dogfightOffsetYField, value);
        }

        public Vessel DogfightTarget
        {
            get => GetReference<Vessel>(_dogfightTargetField);
            set => SetReference(_dogfightTargetField, value);
        }

        public bool DogfightChasePlaneMode
        {
            get => GetBool(_dogfightChasePlaneModeField, false);
            set => SetBool(_dogfightChasePlaneModeField, value);
        }

        // Pathing Properties
        public int SelectedPathIndex
        {
            get => GetInt(_selectedPathIndexField, -1);
            set => SetInt(_selectedPathIndexField, value);
        }

        public bool IsPlayingPath
        {
            get => GetBool(_isPlayingPathField, false);
            set => SetBool(_isPlayingPathField, value);
        }

        public int CurrentKeyframeIndex
        {
            get => GetInt(_currentKeyframeIndexField, -1);
            set => SetInt(_currentKeyframeIndexField, value);
        }

        public bool UseRealTime
        {
            get => GetBool(_useRealTimeField, true);
            set => SetBool(_useRealTimeField, value);
        }

        public float PathStartTime
        {
            get => GetFloat(_pathStartTimeField, 0f);
            set => SetFloat(_pathStartTimeField, value);
        }
        #endregion

        #region Public Methods
        public void Activate()
        {
            var instance = GetFetchInstance();
            if (instance == null || _cameraActivateMethod == null) return;
            _cameraActivateMethod.Invoke(instance, null);
        }

        public void Revert()
        {
            var instance = GetFetchInstance();
            if (instance == null || _revertCameraMethod == null) return;
            _revertCameraMethod.Invoke(instance, null);
        }

        /// <summary>
        /// Releases control without reverting camera to stock position.
        /// Use when switching directly to HullCam to avoid intermediate frame.
        /// </summary>
        public void ReleaseControlWithoutReverting()
        {
            if (!IsActive) return;
            IsActive = false;
        }

        public bool PathExists(int index)
        {
            var paths = GetReference<IList>(_availablePathsField);
            return paths != null && index >= 0 && index < paths.Count;
        }

        #region Main Capture Entry Point

        /// <summary>
        /// Captures current CameraTools settings into a serializable DTO.
        /// </summary>
        public CameraToolsSettings CaptureSettings()
        {
            if (!IsAvailable)
            {
                Debug.LogWarning("[CTAdapter] CaptureSettings called but CameraTools is not available");
                return null;
            }

            var settings = new CameraToolsSettings
            {
                Mode = CurrentMode
            };

            try
            {
                switch (CurrentMode)
                {
                    case ToolModes.DogfightCamera:
                        CaptureDogfightSettings(settings);
                        break;

                    case ToolModes.StationaryCamera:
                        CaptureStationarySettings(settings);
                        break;

                    case ToolModes.Pathing:
                        CapturePathingSettings(settings);
                        break;
                }

                return settings;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CTAdapter] Failed to capture settings: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        #endregion

        #region Mode-Specific Capture Methods

        private void CaptureDogfightSettings(CameraToolsSettings settings)
        {
            settings.DogfightDistance = GetFloat(_dogfightDistanceField, 50f);
            settings.DogfightOffsetX = GetFloat(_dogfightOffsetXField, 0f);
            settings.DogfightOffsetY = GetFloat(_dogfightOffsetYField, 5f);
            settings.DogfightChasePlaneMode = GetBool(_dogfightChasePlaneModeField, false);

            Vessel target = GetReference<Vessel>(_dogfightTargetField);
            settings.DogfightTargetId = target?.id.ToString();
        }

        private void CaptureStationarySettings(CameraToolsSettings settings)
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null)
            {
                Debug.LogWarning("[CTAdapter] Cannot capture stationary settings - no active vessel");
                return;
            }

            // Capture positioning mode flags from CameraTools
            bool autoFlyby = GetBool(_autoFlybyPositionField, false);
            bool autoLanding = GetBool(_autoLandingPositionField, false);
            bool manualOffset = GetBool(_manualOffsetField, false);

            // Determine positioning strategy based on active modes
            if (autoFlyby || autoLanding)
            {
                // Auto-positioning modes: don't capture geographic coords, just flags
                settings.AutoFlybyPosition = autoFlyby;
                settings.AutoLandingPosition = autoLanding;
                settings.UseGeographicPosition = false;
                settings.ManualOffset = false;
            }
            else if (manualOffset)
            {
                // Manual offset mode: capture input values (forward/right/up relative to vessel)
                settings.ManualOffset = true;
                settings.UseGeographicPosition = false;
                settings.AutoFlybyPosition = false;
                settings.AutoLandingPosition = false;

                settings.ManualOffsetForward = GetFloat(_manualOffsetForwardField, 500f);
                settings.ManualOffsetRight = GetFloat(_manualOffsetRightField, 50f);
                settings.ManualOffsetUp = GetFloat(_manualOffsetUpField, 5f);
            }
            else
            {
                // Geographic position capture (THE FIX)
                settings.UseGeographicPosition = true;
                settings.AutoFlybyPosition = false;
                settings.AutoLandingPosition = false;
                settings.ManualOffset = false;

                CelestialBody body = FlightGlobals.currentMainBody;
                if (body != null)
                {
                    // Get actual world position from FlightCamera for geographic conversion
                    Vector3 cameraWorldPos = FlightCamera.fetch?.transform.position ?? Vector3.zero;

                    settings.BodyName = body.name;
                    settings.Latitude = body.GetLatitude(cameraWorldPos);
                    settings.Longitude = body.GetLongitude(cameraWorldPos);
                    settings.Altitude = body.GetAltitude(cameraWorldPos); // ASL
                }
                else
                {
                    Debug.LogWarning("[CTAdapter] Cannot capture geographic coordinates - no current main body");
                    settings.UseGeographicPosition = false;
                }
            }

            // Capture common stationary settings regardless of positioning mode
            settings.SaveRotation = GetBool(_saveRotationField, false);
            settings.FmPivotMode = ConvertToLocalFMPivotMode(_fmPivotModeField?.GetValue(GetFetchInstance()));
            settings.InitialVelocity = GetVector3(_initialVelocityField, Vector3.zero);
            settings.MaintainInitialVelocity = GetBool(_maintainInitialVelocityField, false);
            settings.UseOrbital = GetBool(_useOrbitalField, false);
            settings.AutoZoom = GetBool(_autoZoomStationaryField, false);
            settings.ManualFOV = GetFloat(_manualFOVField, 60f);

            // Capture target tracking state
            CaptureTargetTrackingState(settings, currentVessel);
        }

        private void CapturePathingSettings(CameraToolsSettings settings)
        {
            settings.SelectedPathIndex = GetInt(_selectedPathIndexField, -1);
            settings.CurrentKeyframeIndex = GetInt(_currentKeyframeIndexField, -1);
            settings.IsPlayingPath = GetBool(_isPlayingPathField, false);
            settings.UseRealTime = GetBool(_useRealTimeField, true);
            settings.PathStartTime = GetFloat(_pathStartTimeField, 0f);

            // Capture secondary smoothing
            settings.PathingSecondarySmoothing = GetFloat(_pathingSecondarySmoothingField, 0f);

            if (!PathExists(settings.SelectedPathIndex))
            {
                Debug.LogWarning($"[CTAdapter] Capturing Pathing settings with invalid path index: {settings.SelectedPathIndex}");
                settings.SelectedPathIndex = -1;
            }

            object instance = GetFetchInstance();
            settings.PathTimeScale = ExtractPathTimeScale(instance, settings.SelectedPathIndex);
        }

        #endregion

        #region Target Tracking Capture

        private void CaptureTargetTrackingState(CameraToolsSettings settings, Vessel currentVessel)
        {
            settings.HasTarget = GetBool(_hasTargetField, false);
            settings.TargetCoM = GetBool(_targetCoMField, false);

            Part camTarget = GetReference<Part>(_camTargetField);

            if (camTarget != null && currentVessel != null)
            {
                if (camTarget.vessel == currentVessel)
                {
                    settings.TargetSelf = true;
                    settings.TargetPartPersistentId = 0;
                }
                else
                {
                    settings.TargetSelf = false;
                    settings.TargetPartPersistentId = camTarget.persistentId;
                }
            }
            else
            {
                settings.TargetSelf = false;
                settings.TargetPartPersistentId = 0;
            }
        }

        #endregion

        #region Pathing Helpers

        private float ExtractPathTimeScale(object fetchInstance, int pathIndex)
        {
            if (pathIndex < 0 || _availablePathsField == null) return 1f;

            try
            {
                var paths = _availablePathsField.GetValue(fetchInstance) as System.Collections.IList;
                if (paths != null && pathIndex < paths.Count)
                {
                    var path = paths[pathIndex];
                    if (path != null)
                    {
                        var timeScaleField = path.GetType().GetField("timeScale");
                        if (timeScaleField != null)
                        {
                            return (float)timeScaleField.GetValue(path);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CTAdapter] Failed to extract path timescale: {ex.Message}");
            }

            return 1f;
        }

        #endregion

        #region Enum Conversion Helpers

        /// <summary>
        /// Converts CameraTools FMModeTypes enum to our local FMPivotMode enum.
        /// </summary>
        private FMPivotMode ConvertToLocalFMPivotMode(object ctEnumValue)
        {
            if (ctEnumValue == null) return FMPivotMode.Camera;

            try
            {
                int intValue = Convert.ToInt32(ctEnumValue);
                return (FMPivotMode)intValue;
            }
            catch
            {
                return FMPivotMode.Camera;
            }
        }

        /// <summary>
        /// Converts our local FMPivotMode enum to CameraTools FMModeTypes enum value.
        /// </summary>
        private object ConvertToCameraToolsFMPivotMode(FMPivotMode mode)
        {
            if (_fmPivotModeEnumType == null) return null;

            try
            {
                return Enum.ToObject(_fmPivotModeEnumType, (int)mode);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[CameraToolsAdapter] FMPivotMode conversion failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Main Application Entry Point

        /// <summary>
        /// Applies settings from a DTO to CameraTools.
        /// </summary>
        public void ApplySettings(CameraToolsSettings settings)
        {
            if (!IsAvailable || settings == null)
            {
                Debug.LogWarning("[CTAdapter] ApplySettings called with null settings or unavailable adapter");
                return;
            }

            try
            {
                CurrentMode = settings.Mode;

                switch (settings.Mode)
                {
                    case ToolModes.DogfightCamera:
                        ApplyDogfightSettings(settings);
                        break;

                    case ToolModes.StationaryCamera:
                        ApplyStationarySettings(settings);
                        break;

                    case ToolModes.Pathing:
                        ApplyPathingSettings(settings);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CTAdapter] Failed to apply settings: {ex.Message}\n{ex.StackTrace}");
            }
        }

        #endregion

        #region Mode-Specific Application Methods

        private void ApplyDogfightSettings(CameraToolsSettings settings)
        {
            if (settings.DogfightDistance > 0)
            {
                SetFloat(_dogfightDistanceField, settings.DogfightDistance);
            }

            SetFloat(_dogfightOffsetXField, settings.DogfightOffsetX);
            SetFloat(_dogfightOffsetYField, settings.DogfightOffsetY);
            SetBool(_dogfightChasePlaneModeField, settings.DogfightChasePlaneMode);

            if (!string.IsNullOrEmpty(settings.DogfightTargetId))
            {
                var target = FlightGlobals.Vessels.FirstOrDefault(v =>
                    v.id.ToString() == settings.DogfightTargetId);

                if (target != null)
                {
                    SetReference(_dogfightTargetField, target);
                }
                else
                {
                    SetReference<Vessel>(_dogfightTargetField, null);
                }
            }
            else
            {
                SetReference<Vessel>(_dogfightTargetField, null);
            }
        }

        private void ApplyStationarySettings(CameraToolsSettings settings)
        {
            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null)
            {
                Debug.LogError("[CTAdapter] Cannot apply stationary settings - no active vessel");
                return;
            }

            // Phase 1: Pre-activation setup
            // Disable all auto-positioning modes before activation to prevent conflicts
            SetBool(_autoFlybyPositionField, false);
            SetBool(_autoLandingPositionField, false);
            SetBool(_manualOffsetField, false);

            // Resolve body for geographic coordinates
            CelestialBody body = FlightGlobals.currentMainBody;
            if (!string.IsNullOrEmpty(settings.BodyName) && settings.BodyName != body.name)
            {
                Debug.LogWarning($"[CTAdapter] Body mismatch! Saved: {settings.BodyName}, Current: {body.name}");
                body = FlightGlobals.Bodies.FirstOrDefault(b => b.name == settings.BodyName) ?? body;
            }

            // Apply common settings
            object pivotModeValue = ConvertToCameraToolsFMPivotMode(settings.FmPivotMode);
            if (pivotModeValue != null)
            {
                SetField(_fmPivotModeField, pivotModeValue);
            }

            ApplyTargetState(settings, currentVessel);

            SetBool(_maintainInitialVelocityField, settings.MaintainInitialVelocity);
            SetBool(_useOrbitalField, settings.UseOrbital);
            SetBool(_autoZoomStationaryField, settings.AutoZoom);
            SetFloat(_manualFOVField, settings.ManualFOV);

            if (settings.MaintainInitialVelocity && settings.InitialVelocity != Vector3.zero)
            {
                SetVector3(_initialVelocityField, settings.InitialVelocity);
            }

            // Handle positioning modes
            if (settings.UseGeographicPosition)
            {
                // Calculate restored world position using PQS (full precision, immune to terrain LOD)
                Vector3 restoredWorldPos = body.GetWorldSurfacePosition(settings.Latitude, settings.Longitude, settings.Altitude);

                // Phase 1 (continued): Set preset mode for initial placement
                SetBool(_setPresetOffsetField, true);
                SetVector3(_presetOffsetField, restoredWorldPos);

                // Store the correct values for the post-activation fixup (Phase 3)
                _pendingRestoredPosition = restoredWorldPos;
                _pendingGeographicSettings = settings;
            }
            else if (settings.ManualOffset)
            {
                SetBool(_manualOffsetField, true);
                SetFloat(_manualOffsetForwardField, settings.ManualOffsetForward);
                SetFloat(_manualOffsetRightField, settings.ManualOffsetRight);
                SetFloat(_manualOffsetUpField, settings.ManualOffsetUp);

                _pendingGeographicSettings = null;
            }
            else if (settings.AutoFlybyPosition || settings.AutoLandingPosition)
            {
                SetBool(_autoFlybyPositionField, settings.AutoFlybyPosition);
                SetBool(_autoLandingPositionField, settings.AutoLandingPosition);

                _pendingGeographicSettings = null;
            }
            else
            {
                SetBool(_setPresetOffsetField, false);
                _pendingGeographicSettings = null;
            }
        }

        private void ApplyPathingSettings(CameraToolsSettings settings)
        {
            if (!PathExists(settings.SelectedPathIndex))
            {
                Debug.LogError($"[CTAdapter] Cannot apply Pathing settings - path index {settings.SelectedPathIndex} does not exist");
                return;
            }

            SetInt(_selectedPathIndexField, settings.SelectedPathIndex);
            SetBool(_useRealTimeField, settings.UseRealTime);

            object instance = GetFetchInstance();
            ApplyPathTimeScale(instance, settings.SelectedPathIndex, settings.PathTimeScale);

            SetFloat(_pathingSecondarySmoothingField, settings.PathingSecondarySmoothing);

            if (settings.CurrentKeyframeIndex >= 0)
            {
                SetInt(_currentKeyframeIndexField, settings.CurrentKeyframeIndex);
            }

            SetBool(_isPlayingPathField, settings.IsPlayingPath);
            SetFloat(_pathStartTimeField, settings.PathStartTime);
        }

        #endregion

        #region Target Resolution Helpers

        private void ApplyTargetState(CameraToolsSettings settings, Vessel currentVessel)
        {
            SetBool(_hasTargetField, settings.HasTarget);
            SetBool(_targetCoMField, settings.TargetCoM);

            if (!settings.HasTarget)
            {
                SetReference<Part>(_camTargetField, null);
                return;
            }

            Part targetPart = ResolveTargetPart(settings, currentVessel);

            if (targetPart != null)
            {
                SetReference(_camTargetField, targetPart);
            }
            else
            {
                SetReference<Part>(_camTargetField, null);
                SetBool(_hasTargetField, false);
            }
        }

        private Part ResolveTargetPart(CameraToolsSettings settings, Vessel currentVessel)
        {
            Part targetPart = null;

            if (settings.TargetSelf)
            {
                if (currentVessel != null)
                {
                    targetPart = currentVessel.GetReferenceTransformPart() ?? currentVessel.rootPart;
                }
            }
            else if (settings.TargetPartPersistentId != 0 && currentVessel != null)
            {
                targetPart = currentVessel.Parts.FirstOrDefault(p =>
                    p.persistentId == settings.TargetPartPersistentId);

                if (targetPart == null)
                {
                    targetPart = currentVessel.GetReferenceTransformPart() ?? currentVessel.rootPart;
                }
            }
            else if (currentVessel != null)
            {
                targetPart = currentVessel.GetReferenceTransformPart() ?? currentVessel.rootPart;
            }

            return targetPart;
        }

        #endregion

        #region Geographic Restoration & Fixup

        // Deferred restoration state for PostActivationPositionFixup
        private Vector3 _pendingRestoredPosition;
        private CameraToolsSettings _pendingGeographicSettings;

        /// <summary>
        /// Phase 3: Post-activation position fixup to override terrain-corrupted coordinates.
        /// MUST be called on the next frame after CameraActivate() when UseGeographicPosition is true.
        /// </summary>
        public void PostActivationPositionFixup()
        {
            if (_pendingGeographicSettings == null || !IsActive)
            {
                return;
            }

            Vessel currentVessel = FlightGlobals.ActiveVessel;
            if (currentVessel == null)
            {
                Debug.LogWarning("[CT-FIXUP] Cannot fixup - null vessel");
                _pendingGeographicSettings = null;
                return;
            }

            Vector3 restoredWorldPos = _pendingRestoredPosition;
            Vector3 targetOffset = restoredWorldPos - currentVessel.CoM;

            // Access cached private fields for manual override
            GameObject cameraParent = _cameraParentField?.GetValue(GetFetchInstance()) as GameObject;

            // Phase 3: Override the terrain corruption
            // Disable preset mode so UpdateStationaryCamera uses manualPosition instead
            SetBool(_setPresetOffsetField, false);

            // Set the relative offset for UpdateStationaryCamera maintenance
            SetVector3(_manualPositionField, targetOffset);

            // Update cameraParent to prevent drift (this is the authoritative position container)
            if (cameraParent != null)
            {
                cameraParent.transform.position = restoredWorldPos;
            }

            // Force camera transform to the mathematically correct position immediately
            if (FlightCamera.fetch != null)
            {
                FlightCamera.fetch.transform.position = restoredWorldPos;

                // CRITICAL FIX: Zero out local position to prevent double-application of manualPosition offset.
                if (FlightCamera.fetch.transform.parent != null)
                {
                    FlightCamera.fetch.transform.localPosition = Vector3.zero;
                }
            }

            // Reset lastVesselCoM to prevent drift correction from jumping on next UpdateStationaryCamera call
            if (_lastVesselCoMField != null)
            {
                _lastVesselCoMField.SetValue(GetFetchInstance(), currentVessel.CoM);
            }

            // Step 3: Apply auto-zoom FOV snap immediately after position fixup
            // This bypasses CameraTools' 0.1f lerp interpolation to eliminate the "push-in" effect
            if (_pendingGeographicSettings.UseConsistentAutoZoom)
            {
                // NEW: Consistent angular-size-based zoom
                float targetFOV = CalculateConsistentAutoZoom(currentVessel, restoredWorldPos, _pendingGeographicSettings.ZoomPadding);
                EnforceAutoZoomFOVImmediate(targetFOV);
            }
            else if (_pendingGeographicSettings.AutoZoom)
            {
                // ORIGINAL: Native CameraTools auto-zoom with immediate snap (no push-in)
                float targetFOV = CalculateAutoZoomFOV(_pendingGeographicSettings, currentVessel);
                EnforceAutoZoomFOVImmediate(targetFOV);
            }

            Debug.Log($"[CT-FIXUP] Phase 3 Override - World: {restoredWorldPos}, Offset: {targetOffset}, CoM: {currentVessel.CoM}");

            // Clear pending state
            _pendingRestoredPosition = Vector3.zero;
            _pendingGeographicSettings = null;
        }

        /// <summary>
        /// Returns true if there's a pending geographic restoration awaiting fixup.
        /// </summary>
        public bool HasPendingGeographicRestoration()
        {
            return _pendingGeographicSettings != null && _pendingGeographicSettings.UseGeographicPosition;
        }

        /// <summary>
        /// Clears any pending geographic restoration state.
        /// </summary>
        public void ClearPendingRestoration()
        {
            _pendingRestoredPosition = Vector3.zero;
            _pendingGeographicSettings = null;
        }

        #endregion

        #region Consistent Auto-Zoom Implementation (Step 2)

        /// <summary>
        /// Calculates FOV using angular size formula to maintain consistent framing regardless of distance.
        /// Formula: FOV = 2 * atan((vesselRadius * paddingMultiplier) / distance) * (180/pi)
        /// </summary>
        public float CalculateConsistentAutoZoom(Vessel vessel, Vector3 cameraPosition, float paddingMultiplier)
        {
            if (vessel == null) return 60f;

            float distance = Vector3.Distance(cameraPosition, vessel.CoM);
            if (distance < 0.01f) distance = 0.01f; // Prevent division by zero

            // Calculate vessel bounding radius from parts (GetVesselSize doesn't exist in KSP API)
            float radius = CalculateVesselBoundingRadius(vessel);
            float fov = 2f * Mathf.Rad2Deg * Mathf.Atan((radius * paddingMultiplier) / distance);

            return Mathf.Clamp(fov, 2f, 120f);
        }

        /// <summary>
        /// Calculates the vessel's bounding radius from part positions relative to CoM.
        /// </summary>
        private float CalculateVesselBoundingRadius(Vessel vessel)
        {
            if (vessel == null || vessel.Parts == null || vessel.Parts.Count == 0)
                return 5f; // Default 5m radius for empty/dead vessels

            float maxDistSq = 0f;
            Vector3 com = vessel.CoM;

            foreach (Part p in vessel.Parts)
            {
                if (p == null || p.transform == null) continue;
                float distSq = (p.transform.position - com).sqrMagnitude;
                if (distSq > maxDistSq)
                    maxDistSq = distSq;
            }

            return Mathf.Sqrt(maxDistSq);
        }

        /// <summary>
        /// Applies consistent auto-zoom settings. Disables native auto-zoom when enabling custom zoom.
        /// Should be called every frame in LateUpdate when active to maintain FOV as distance changes.
        /// </summary>
        public void ApplyConsistentAutoZoom(bool enable, float padding)
        {
            if (!IsAvailable) return;

            // Disable native auto-zoom when custom is active to prevent conflicts
            SetBool(_autoZoomStationaryField, !enable);

            if (enable && FlightGlobals.ActiveVessel != null && FlightCamera.fetch != null)
            {
                Vector3 camPos = FlightCamera.fetch.transform.position;
                float targetFov = CalculateConsistentAutoZoom(FlightGlobals.ActiveVessel, camPos, padding);

                // Set manualFOV (target value)
                SetFloat(_manualFOVField, targetFov);

                // Bypass CameraTools' 0.1f lerp by setting currentFOV directly
                if (_currentFOVField != null)
                {
                    SetFloat(_currentFOVField, targetFov);
                }

                // Apply immediately to FlightCamera
                FlightCamera.fetch.SetFoV(targetFov);
            }
        }

        #endregion

        #region Auto-Zoom FOV Enforcement (Legacy)

        /// <summary>
        /// Immediately snaps FOV to target value, bypassing CameraTools' 0.1f lerp interpolation.
        /// </summary>
        public void EnforceAutoZoomFOVImmediate(float targetFOV)
        {
            var instance = GetFetchInstance();
            if (instance == null) return;

            if (_manualFOVField != null)
                _manualFOVField.SetValue(instance, targetFOV);

            if (_currentFOVField != null)
                _currentFOVField.SetValue(instance, targetFOV);

            if (FlightCamera.fetch != null)
                FlightCamera.fetch.SetFoV(targetFOV);
        }

        /// <summary>
        /// Calculates FOV using CameraTools' empirical formula (legacy).
        /// </summary>
        private float CalculateAutoZoomFOV(CameraToolsSettings settings, Vessel vessel)
        {
            if (vessel == null) return 60f;

            Vector3 targetPos = (settings.HasTarget && !settings.TargetSelf)
                ? GetCamTarget()?.transform.position ?? vessel.CoM
                : vessel.CoM;

            Vector3 cameraPos = FlightCamera.fetch?.transform.position ?? Vector3.zero;
            float distance = Vector3.Distance(targetPos, cameraPos);

            float margin = (_autoZoomMarginStationaryField != null)
                ? GetFloat(_autoZoomMarginStationaryField, 30f)
                : 30f;

            float targetFoV = (7000f / (distance + 100f)) - 14f + margin;
            return Mathf.Clamp(targetFoV, 2f, 60f);
        }

        /// <summary>
        /// Helper to retrieve CameraTools' current camTarget Part via reflection.
        /// </summary>
        private Part GetCamTarget()
        {
            return GetReference<Part>(_camTargetField);
        }

        #endregion

        #region Pathing Helpers

        private void ApplyPathTimeScale(object fetchInstance, int pathIndex, float timeScale)
        {
            if (pathIndex < 0 || _availablePathsField == null) return;

            try
            {
                var paths = _availablePathsField.GetValue(fetchInstance) as System.Collections.IList;
                if (paths != null && pathIndex < paths.Count)
                {
                    var path = paths[pathIndex];
                    if (path != null)
                    {
                        var timeScaleField = path.GetType().GetField("timeScale");
                        if (timeScaleField != null)
                        {
                            timeScaleField.SetValue(path, timeScale);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CTAdapter] Failed to apply path timescale: {ex.Message}");
            }
        }

        #endregion

        /// <summary>
        /// Activates a specific mode with optional settings.
        /// If using geographic positioning, you MUST call PostActivationPositionFixup() 
        /// on the next frame to complete the restoration.
        /// </summary>
        public void ActivateMode(ToolModes mode, CameraToolsSettings settings = null)
        {
            if (!IsAvailable) return;

            CurrentMode = mode;

            if (settings != null)
            {
                ApplySettings(settings);
            }

            Activate();
        }

        #endregion

        #region Reflection Helpers

        /// <summary>
        /// Validates that the CameraTools fetch instance is available.
        /// </summary>
        private bool ValidateInstance(object instance)
        {
            if (instance == null)
            {
                UnityEngine.Debug.LogWarning("[CameraToolsAdapter] Operation failed: CameraTools fetch instance is null");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Retrieves the current CameraTools fetch instance with validation.
        /// </summary>
        private object GetValidatedInstance()
        {
            var instance = GetFetchInstance();
            if (!ValidateInstance(instance))
            {
                return null;
            }
            return instance;
        }

        #region Generic Field Accessors

        /// <summary>
        /// Gets a field value of type T with default fallback.
        /// </summary>
        private T GetField<T>(FieldInfo field, T defaultValue = default)
        {
            object instance = GetFetchInstance();
            if (instance == null || field == null)
            {
                return defaultValue;
            }

            try
            {
                object value = field.GetValue(instance);
                if (value is T typedValue)
                {
                    return typedValue;
                }
                return defaultValue;
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[CameraToolsAdapter] GetField failed for {field.Name}: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a field value with type safety and error handling.
        /// </summary>
        private void SetField<T>(FieldInfo field, T value)
        {
            object instance = GetFetchInstance();
            if (instance == null || field == null)
            {
                return;
            }

            try
            {
                field.SetValue(instance, value);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[CameraToolsAdapter] SetField failed for {field.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a value type field, returning defaultValue if the field is unbound or instance is null.
        /// </summary>
        private T GetValueTypeField<T>(FieldInfo field, T defaultValue) where T : struct
        {
            object instance = GetFetchInstance();
            if (instance == null || field == null)
            {
                return defaultValue;
            }

            try
            {
                object result = field.GetValue(instance);
                if (result is T)
                {
                    return (T)result;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[CameraToolsAdapter] GetValueTypeField failed: {ex.Message}");
            }

            return defaultValue;
        }

        #endregion

        #region Specialized Type Accessors (Convenience Wrappers)

        private bool GetBool(FieldInfo field, bool defaultValue = false)
        {
            return GetValueTypeField(field, defaultValue);
        }

        private void SetBool(FieldInfo field, bool value)
        {
            SetField(field, value);
        }

        private float GetFloat(FieldInfo field, float defaultValue = 0f)
        {
            return GetValueTypeField(field, defaultValue);
        }

        private void SetFloat(FieldInfo field, float value)
        {
            SetField(field, value);
        }

        private int GetInt(FieldInfo field, int defaultValue = 0)
        {
            return GetValueTypeField(field, defaultValue);
        }

        private void SetInt(FieldInfo field, int value)
        {
            SetField(field, value);
        }

        private Vector3 GetVector3(FieldInfo field, Vector3 defaultValue = default)
        {
            return GetValueTypeField(field, defaultValue);
        }

        private void SetVector3(FieldInfo field, Vector3 value)
        {
            SetField(field, value);
        }

        private T GetReference<T>(FieldInfo field) where T : class
        {
            return GetField<T>(field, null);
        }

        private void SetReference<T>(FieldInfo field, T value) where T : class
        {
            SetField(field, value);
        }

        #endregion

        #region Enum Conversion Helpers

        /// <summary>
        /// Converts CameraTools ToolModes enum to our local ToolModes enum.
        /// </summary>
        private ToolModes ConvertToLocalToolModes(object ctEnumValue)
        {
            if (ctEnumValue == null) return ToolModes.StationaryCamera;

            try
            {
                int intValue = System.Convert.ToInt32(ctEnumValue);
                return (ToolModes)intValue;
            }
            catch
            {
                return ToolModes.StationaryCamera;
            }
        }

        /// <summary>
        /// Converts our local ToolModes enum to CameraTools ToolModes enum value.
        /// </summary>
        private object ConvertToCameraToolsToolModes(ToolModes mode)
        {
            if (_toolModesEnumType == null) return null;

            try
            {
                return System.Enum.ToObject(_toolModesEnumType, (int)mode);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"[CameraToolsAdapter] Enum conversion failed: {ex.Message}");
                return null;
            }
        }

        #endregion

        #endregion
    }
}