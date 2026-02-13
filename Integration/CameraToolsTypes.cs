using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Mirrors CameraTools.ToolModes enum (defined at namespace level, not inside CamTools class)
    /// </summary>
    public enum ToolModes { StationaryCamera, DogfightCamera, Pathing }

    /// <summary>
    /// Mirrors CameraTools.FMModePivotTypes for target vs camera pivot behavior
    /// </summary>
    public enum FMPivotMode { Camera, Target }

    /// <summary>
    /// Serializable DTO for CameraTools state persistence.
    /// Uses geographic coordinates (Lat/Lon/Alt) for position stability instead of world-space Vector3.
    /// </summary>
    [Serializable]
    public class CameraToolsSettings
    {
        public ToolModes Mode;

        // Dogfight params
        public float DogfightDistance = 50f;
        public float DogfightOffsetX = 0f;
        public float DogfightOffsetY = 5f;
        public bool DogfightChasePlaneMode = false;
        public string DogfightTargetId;

        // Stationary params - Positioning Mode Flags
        public bool UseGeographicPosition;          // TRUE: Use Lat/Lon/Alt (formerly PresetOffset). FALSE: Use ManualOffset or Auto modes
        public bool AutoFlybyPosition;              // Uses auto-calculated flyby position
        public bool ManualOffset;                   // Uses manualOffsetForward/Right/Up relative to vessel
        public float ManualOffsetForward = 500f;
        public float ManualOffsetRight = 50f;
        public float ManualOffsetUp = 5f;
        public bool AutoLandingPosition;            // Landing prediction mode

        // Stationary params - Geographic Position (THE FIX)
        // These replace the old PresetOffset Vector3 which was corrupted by terrain LOD
        public double Latitude;
        public double Longitude;
        public double Altitude;                     // ASL (Above Sea Level)
        public string BodyName;                     // CelestialBody name for coordinate resolution

        // Stationary params - Runtime Calculated (NOT persisted directly, calculated from Geographic + current CoM)
        // Note: ManualPosition (offset from CoM) is calculated at runtime via GetWorldSurfacePosition() - vessel.CoM

        // Target tracking
        public bool HasTarget;
        public bool TargetSelf;                     // Target the active vessel (dynamic)
        public uint TargetPartPersistentId;         // Specific part (if TargetSelf is false)
        public bool TargetCoM;

        // Camera behavior settings
        public bool AutoZoom;
        public float ManualFOV = 60f;
        public bool MaintainInitialVelocity;
        public bool UseOrbital;

        // Velocity maintenance state (captured for exact drift matching)
        public Vector3 InitialVelocity;             // Vessel velocity at capture time (for MaintainInitialVelocity)

        // Pathing params
        public int SelectedPathIndex = -1;
        public float PathTimeScale = 1f;
        public int CurrentKeyframeIndex = -1;
        public bool IsPlayingPath = false;
        public bool UseRealTime = true;
        public float PathStartTime = 0f;

        // NEW: Playback timing control for deterministic vs real-time pathing
        public bool LockPathingToPlaybackRate;      // TRUE: Path advances by video frame time (for Kraken-Time). FALSE: Physics time (default)
        public bool UseDeterministicControl;        // TRUE: Enables deterministic physics-step control mode

        // Additional CameraTools settings for accurate restoration
        public bool SaveRotation;                   // Whether to restore previous rotation on activation
        public FMPivotMode FmPivotMode;             // Camera vs Target pivot mode for free movement
        public float PathingSecondarySmoothing;     // Additional smoothing for path interpolation

        // Custom Auto-Zoom Settings
        public bool UseConsistentAutoZoom;          // Enable custom angular-size-based auto-zoom
        public float ZoomPadding;                   // Padding multiplier (0.5 = tight, 1.5 = normal, 3.0 = wide)

        public CameraToolsSettings()
        {
            // Initialize default values
            ZoomPadding = 1.5f;
            LockPathingToPlaybackRate = false;      // Default to physics time for compatibility
            UseDeterministicControl = false;
        }

        // Display helper
        public string GetDisplayName()
        {
            switch (Mode)
            {
                case ToolModes.DogfightCamera:
                    return $"Dogfight {(string.IsNullOrEmpty(DogfightTargetId) ? "(Free)" : "(Target)")}";
                case ToolModes.StationaryCamera:
                    if (UseGeographicPosition)
                        return $"Stationary (Geo: {Latitude:F2}, {Longitude:F2})";
                    if (ManualOffset)
                        return "Stationary (Manual Offset)";
                    if (AutoFlybyPosition)
                        return "Stationary (Flyby)";
                    if (AutoLandingPosition)
                        return "Stationary (Landing)";
                    return "Stationary";
                case ToolModes.Pathing:
                    return SelectedPathIndex >= 0 ? $"Path #{SelectedPathIndex}" : "Pathing";
                default:
                    return "CameraTools";
            }
        }

        public bool ApproximatelyMatches(CameraToolsSettings other)
        {
            if (other == null || other.Mode != this.Mode) return false;

            switch (Mode)
            {
                case ToolModes.StationaryCamera:
                    // If both use geographic positioning, compare coordinates
                    if (this.UseGeographicPosition && other.UseGeographicPosition)
                    {
                        return Math.Abs(this.Latitude - other.Latitude) < 0.0001 &&
                               Math.Abs(this.Longitude - other.Longitude) < 0.0001 &&
                               Math.Abs(this.Altitude - other.Altitude) < 10.0 && // 10m altitude tolerance
                               this.BodyName == other.BodyName;
                    }

                    // If both are auto-flyby, they're the same "type" of camera
                    if (this.AutoFlybyPosition && other.AutoFlybyPosition)
                        return true;

                    // If both are auto-landing
                    if (this.AutoLandingPosition && other.AutoLandingPosition)
                        return true;

                    // If both use manual offset inputs (not direct positioning)
                    if (this.ManualOffset && other.ManualOffset)
                    {
                        return Mathf.Abs(this.ManualOffsetForward - other.ManualOffsetForward) < 1.0f &&
                               Mathf.Abs(this.ManualOffsetRight - other.ManualOffsetRight) < 1.0f &&
                               Mathf.Abs(this.ManualOffsetUp - other.ManualOffsetUp) < 1.0f;
                    }

                    // Mismatched positioning modes
                    return false;

                case ToolModes.DogfightCamera:
                    return Mathf.Abs(this.DogfightDistance - other.DogfightDistance) < 5.0f &&
                           Mathf.Abs(this.DogfightOffsetX - other.DogfightOffsetX) < 1.0f &&
                           Mathf.Abs(this.DogfightOffsetY - other.DogfightOffsetY) < 1.0f;

                case ToolModes.Pathing:
                    // Include playback timing in path matching
                    return this.SelectedPathIndex == other.SelectedPathIndex &&
                           this.LockPathingToPlaybackRate == other.LockPathingToPlaybackRate;
            }
            return false;
        }

        /// <summary>
        /// Creates a deep copy of these settings.
        /// </summary>
        public CameraToolsSettings Clone()
        {
            return new CameraToolsSettings
            {
                Mode = this.Mode,
                DogfightDistance = this.DogfightDistance,
                DogfightOffsetX = this.DogfightOffsetX,
                DogfightOffsetY = this.DogfightOffsetY,
                DogfightChasePlaneMode = this.DogfightChasePlaneMode,
                DogfightTargetId = this.DogfightTargetId,
                UseGeographicPosition = this.UseGeographicPosition,
                Latitude = this.Latitude,
                Longitude = this.Longitude,
                Altitude = this.Altitude,
                BodyName = this.BodyName,
                AutoFlybyPosition = this.AutoFlybyPosition,
                ManualOffset = this.ManualOffset,
                ManualOffsetForward = this.ManualOffsetForward,
                ManualOffsetRight = this.ManualOffsetRight,
                ManualOffsetUp = this.ManualOffsetUp,
                AutoLandingPosition = this.AutoLandingPosition,
                HasTarget = this.HasTarget,
                TargetSelf = this.TargetSelf,
                TargetPartPersistentId = this.TargetPartPersistentId,
                TargetCoM = this.TargetCoM,
                AutoZoom = this.AutoZoom,
                ManualFOV = this.ManualFOV,
                MaintainInitialVelocity = this.MaintainInitialVelocity,
                UseOrbital = this.UseOrbital,
                InitialVelocity = this.InitialVelocity,
                SelectedPathIndex = this.SelectedPathIndex,
                PathTimeScale = this.PathTimeScale,
                CurrentKeyframeIndex = this.CurrentKeyframeIndex,
                IsPlayingPath = this.IsPlayingPath,
                UseRealTime = this.UseRealTime,
                PathStartTime = this.PathStartTime,
                // NEW: Copy playback timing settings
                LockPathingToPlaybackRate = this.LockPathingToPlaybackRate,
                UseDeterministicControl = this.UseDeterministicControl,
                SaveRotation = this.SaveRotation,
                FmPivotMode = this.FmPivotMode,
                PathingSecondarySmoothing = this.PathingSecondarySmoothing,
                UseConsistentAutoZoom = this.UseConsistentAutoZoom,
                ZoomPadding = this.ZoomPadding
            };
        }
    }
}