using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// Unified interface for camera controllers (HullCam and CameraTools).
    /// Enables multi camera management in the UI and capture pipeline.
    /// </summary>
    public interface ICamera
    {
        bool IsActive { get; }

        /// <summary>
        /// Camera identifier for UI display
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Unique identifier for persistence (partPersistentId for HullCam, mode+hash for CT)
        /// </summary>
        string CameraId { get; }

        void Activate();
        void Deactivate();

        /// <summary>
        /// Deactivate without reverting to main camera (for switching between cameras)
        /// </summary>
        void ReleaseControl();

        Vector3 Position { get; set; }
        float FieldOfView { get; set; }

        /// <summary>
        /// Maximum FOV supported by this camera
        /// </summary>
        float MaxFieldOfView { get; }

        /// <summary>
        /// Minimum FOV supported by this camera
        /// </summary>
        float MinFieldOfView { get; }

        /// <summary>
        /// For zoom services - applies immediate FOV without smoothing
        /// </summary>
        void SetFieldOfViewImmediate(float fov);

        event Action OnActivated;
        event Action OnDeactivated;

        /// <summary>
        /// Called every LateUpdate when this camera is active
        /// </summary>
        void Update(float deltaTime);
    }
}