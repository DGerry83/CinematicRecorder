using CinematicRecorder.Integration;
using CinematicRecorder.UI;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Manages zoom/FOV calculations and application for both HullCam and CameraTools.
    /// Handles smooth damping, auto-distance tracking, and intent decay.
    /// </summary>
    public class ZoomControlService
    {
        // State
        private float zoomIntentSlider = 0f;
        private float zoomSmoothVelocity = 0f;
        private bool autoDistanceTracking = false;
        private float autoZoomDistanceRef = 100f;
        private float targetFoV = 60f;
        private float currentFoV = 60f;
        private object zoomControlledCamera = null;

        public float CurrentFoV => currentFoV;
        public float TargetFoV => targetFoV;
        public float ZoomIntent { get => zoomIntentSlider; set => zoomIntentSlider = Mathf.Clamp(value, -1f, 1f); }
        public bool AutoDistanceTracking { get => autoDistanceTracking; set => autoDistanceTracking = value; }
        public float AutoZoomDistanceReference { get => autoZoomDistanceRef; set => autoZoomDistanceRef = value; }

        public ZoomControlService()
        {
        }

        /// <summary>
        /// Resets zoom to maximum FOV and clears intent.
        /// </summary>
        public void ResetZoom(float maxFov)
        {
            targetFoV = maxFov;
            zoomIntentSlider = 0f;
            zoomSmoothVelocity = 0f;
        }

        /// <summary>
        /// Updates zoom logic for HullCam cameras. Call from LateUpdate.
        /// </summary>
        public void UpdateHullCamZoom(float deltaTime)
        {
            if (!HullCamBridge.IsAvailable) return;

            // Access active camera through manager
            ICamera cam = CinematicCameraManager.Instance.ActiveCamera;
            var hullCam = cam as HullCamController;

            if (hullCam == null)
            {
                zoomControlledCamera = null;
                return;
            }

            object hullCamModule = HullCamBridge.GetCurrentCamera();
            if (hullCamModule == null) return;

            InitializeZoomForCamera(hullCamModule);
            ProcessZoomIntent(hullCamModule, deltaTime);
            ApplyZoom(hullCamModule);
            DecayZoomIntent(deltaTime);
        }

        /// <summary>
        /// Enforces FOV for CameraTools stationary cameras based on slot settings.
        /// Call from LateUpdate when a CameraTools slot is active.
        /// </summary>
        public void EnforceCameraToolsZoom(CameraSlot activeSlot, CameraToolsCameraController controller)
        {
            if (activeSlot?.ctSettings == null || controller == null) return;
            if (activeSlot.ctSettings.Mode != ToolModes.StationaryCamera) return;

            // Check if active camera is CT
            ICamera activeCam = CinematicCameraManager.Instance.ActiveCamera;
            if (!(activeCam is CameraToolsCamera)) return;

            if (activeSlot.ctSettings.UseConsistentAutoZoom)
            {
                controller.ApplyConsistentAutoZoom(true, activeSlot.ctSettings.ZoomPadding);
            }
            else if (activeSlot.ctSettings.AutoZoom)
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                if (vessel != null && FlightCamera.fetch != null)
                {
                    Vector3 cameraPos = FlightCamera.fetch.transform.position;
                    Vector3 targetPos = (activeSlot.ctSettings.HasTarget && !activeSlot.ctSettings.TargetSelf)
                        ? controller.CamTarget?.transform.position ?? vessel.CoM
                        : vessel.CoM;

                    float distance = Vector3.Distance(cameraPos, targetPos);
                    // Use reflection provider directly for the margin field
                    float margin = CameraToolsReflectionProvider.GetFloat(CameraToolsReflectionProvider.AutoZoomMarginStationaryField, 30f);
                    float nativeFOV = (7000f / (distance + 100f)) - 14f + margin;
                    nativeFOV = Mathf.Clamp(nativeFOV, 2f, 60f);

                    controller.ManualFOV = nativeFOV;
                    FlightCamera.fetch.SetFoV(nativeFOV);
                }
            }
        }

        /// <summary>
        /// Applies immediate FOV to HullCam camera without smooth damping.
        /// </summary>
        public void ApplyImmediateFoV(object camera, float fov)
        {
            if (camera == null) return;
            HullCamBridge.SetCameraFoV(camera, fov);
        }

        private void InitializeZoomForCamera(object activeCam)
        {
            if (activeCam == zoomControlledCamera) return;

            zoomControlledCamera = activeCam;
            currentFoV = HullCamBridge.GetCameraFoV(activeCam);
            targetFoV = currentFoV;
            zoomIntentSlider = 0f;
            zoomSmoothVelocity = 0f;
        }

        private void ProcessZoomIntent(object activeCam, float deltaTime)
        {
            float minFoV = HullCamBridge.GetCameraFoVMin(activeCam);
            float maxFoV = HullCamBridge.GetCameraFoVMax(activeCam);

            if (autoDistanceTracking && FlightGlobals.ActiveVessel != null)
            {
                ApplyAutoZoom(activeCam, minFoV, maxFoV);
            }
            else
            {
                ApplyManualZoom(minFoV, maxFoV, deltaTime);
            }

            targetFoV = Mathf.Clamp(targetFoV, minFoV, maxFoV);
            currentFoV = Mathf.SmoothDamp(currentFoV, targetFoV, ref zoomSmoothVelocity,
                CinematicUIResources.Layout.Zoom.SMOOTH_TIME, Mathf.Infinity, deltaTime);
        }

        private void ApplyAutoZoom(object activeCam, float minFoV, float maxFoV)
        {
            var camTransform = HullCamBridge.GetCameraTransform(activeCam);
            if (camTransform == null || FlightGlobals.ActiveVessel == null) return;

            float distance = Vector3.Distance(camTransform.position, FlightGlobals.ActiveVessel.transform.position);
            float t = Mathf.Clamp01(Mathf.Log(distance / 10f + 1f) / Mathf.Log(autoZoomDistanceRef / 10f + 1f));
            float autoTarget = Mathf.Lerp(maxFoV, minFoV, t);

            targetFoV = Mathf.Lerp(autoTarget, targetFoV, Mathf.Abs(zoomIntentSlider));
        }

        private void ApplyManualZoom(float minFoV, float maxFoV, float deltaTime)
        {
            if (Mathf.Abs(zoomIntentSlider) > CinematicUIResources.Layout.Zoom.INTENT_THRESHOLD)
            {
                float zoomDelta = -zoomIntentSlider * CinematicUIResources.Layout.Zoom.MAX_SPEED * deltaTime;
                targetFoV += zoomDelta;
            }
            else
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f, deltaTime * 2f);
            }
        }

        private void ApplyZoom(object activeCam)
        {
            HullCamBridge.SetCameraFoV(activeCam, currentFoV);
        }

        private void DecayZoomIntent(float deltaTime)
        {
            if (!Input.GetMouseButton(0))
            {
                zoomIntentSlider = Mathf.MoveTowards(zoomIntentSlider, 0f,
                    deltaTime * CinematicUIResources.Layout.Zoom.RETURN_SPEED);
            }
        }
    }
}