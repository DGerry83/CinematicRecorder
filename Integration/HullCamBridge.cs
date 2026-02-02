using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    public static class HullCamBridge
    {
        private static Type s_HullCamType;
        private static MethodInfo s_ActivateMethod;
        private static MethodInfo s_RestoreMethod;
        private static FieldInfo s_CamerasField;
        private static FieldInfo s_CurrentCameraField;
        private static FieldInfo s_CamEnabledField;
        private static FieldInfo s_CameraNameField;

        //FOV Controls
        private static FieldInfo s_CameraFoVField;
        private static FieldInfo s_CameraFoVMinField;
        private static FieldInfo s_CameraFoVMaxField;

        public static bool IsAvailable => s_HullCamType != null;

        static HullCamBridge()
        {
            Initialize();
        }

        private static void Initialize()
        {
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "HullcamVDSContinued" || a.GetName().Name == "HullcamVDS");

                if (asm == null) return;

                s_HullCamType = asm.GetType("HullcamVDS.MuMechModuleHullCamera");
                if (s_HullCamType == null) return;

                s_ActivateMethod = s_HullCamType.GetMethod("Activate", BindingFlags.NonPublic | BindingFlags.Instance);
                s_RestoreMethod = s_HullCamType.GetMethod("RestoreMainCamera", BindingFlags.NonPublic | BindingFlags.Static);
                s_CamerasField = s_HullCamType.GetField("sCameras", BindingFlags.Public | BindingFlags.Static);
                s_CurrentCameraField = s_HullCamType.GetField("sCurrentCamera", BindingFlags.Public | BindingFlags.Static);
                s_CamEnabledField = s_HullCamType.GetField("camEnabled");
                s_CameraNameField = s_HullCamType.GetField("cameraName");

                s_CameraFoVField = s_HullCamType.GetField("cameraFoV", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                s_CameraFoVMinField = s_HullCamType.GetField("cameraFoVMin", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                s_CameraFoVMaxField = s_HullCamType.GetField("cameraFoVMax", BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);

                if (s_HullCamType != null)
                    Debug.Log("[HullCamBridge] HullCam VDS detected and bound");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HullCamBridge] Init failed: " + ex.Message);
                s_HullCamType = null;
            }
        }

        public static void Activate(object cam)
        {
            if (!IsAvailable || cam == null || s_ActivateMethod == null) return;
            if (!IsCameraAvailable(cam)) return;

            try
            {
                s_ActivateMethod.Invoke(cam, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HullCamBridge] Activate failed: {ex.Message}");
            }
        }

        public static void RestoreMain()
        {
            if (!IsAvailable || s_RestoreMethod == null) return;

            try
            {
                s_RestoreMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogError("[HullCamBridge] RestoreMain failed: " + ex.Message);
            }
        }

        public static object GetCurrentCamera()
        {
            if (!IsAvailable || s_CurrentCameraField == null) return null;
            return s_CurrentCameraField.GetValue(null);
        }

        public static bool IsAnyCameraActive()
        {
            return GetCurrentCamera() != null;
        }

        public static IEnumerable<object> GetAllCameras()
        {
            if (!IsAvailable || s_CamerasField == null)
                return Enumerable.Empty<object>();

            var list = s_CamerasField.GetValue(null) as System.Collections.IEnumerable;
            return list?.Cast<object>() ?? Enumerable.Empty<object>();
        }

        public static bool IsCameraActive(object cam)
        {
            if (cam == null) return false;
            return ReferenceEquals(cam, GetCurrentCamera());
        }

        public static bool IsCameraAvailable(object cam)
        {
            if (cam == null || s_CamEnabledField == null) return false;

            try
            {
                bool enabled = (bool)s_CamEnabledField.GetValue(cam);
                if (!enabled) return false;

                Component comp = cam as Component;
                if (comp != null)
                {
                    Part part = comp.GetComponentInParent<Part>();
                    if (part == null || part.State == PartStates.DEAD) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        //Camera FOV Controls

        public static float GetCameraFoV(object cam)
        {
            if (cam == null || s_CameraFoVField == null) return 60f;
            return (float)(s_CameraFoVField.GetValue(cam) ?? 60f);
        }

        public static void SetCameraFoV(object cam, float fov)
        {
            if (cam == null || s_CameraFoVField == null) return;
            float min = GetCameraFoVMin(cam);
            float max = GetCameraFoVMax(cam);
            s_CameraFoVField.SetValue(cam, Mathf.Clamp(fov, min, max));
        }

        public static float GetCameraFoVMin(object cam) =>
            cam != null && s_CameraFoVMinField != null ? (float)s_CameraFoVMinField.GetValue(cam) : 10f;

        public static float GetCameraFoVMax(object cam) =>
            cam != null && s_CameraFoVMaxField != null ? (float)s_CameraFoVMaxField.GetValue(cam) : 120f;

        public static Transform GetCameraTransform(object cam)
        {
            if (cam == null) return null;
            var comp = cam as Component;
            return comp?.transform;
        }

        public static string GetCameraName(object cam)
        {
            if (cam == null || s_CameraNameField == null) return "Unknown";
            return s_CameraNameField.GetValue(cam) as string ?? "Unknown";
        }

        /// <summary>
        /// Resolves slot to camera instance using persistence-safe matching.
        /// </summary>
        public static object ResolveCameraSlot(CameraSlot slot, Vessel currentVessel = null)
        {
            if (!IsAvailable || slot == null) return null;

            var cameras = GetAllCameras().ToList();
            if (cameras.Count == 0) return null;

            foreach (var cam in cameras)
            {
                Component comp = cam as Component;
                if (comp == null) continue;

                Part part = comp.GetComponentInParent<Part>();
                if (part == null) continue;

                if (part.persistentId != slot.partPersistentId) continue;

                if (part.vessel == null || part.State == PartStates.DEAD)
                    continue;

                return cam;
            }

            if (slot.allowAnyVessel && currentVessel != null && !string.IsNullOrEmpty(slot.cameraName))
            {
                return cameras.FirstOrDefault(cam =>
                {
                    Component comp = cam as Component;
                    if (comp == null) return false;

                    Part part = comp.GetComponentInParent<Part>();
                    if (part == null || part.vessel != currentVessel) return false;

                    return GetCameraName(cam) == slot.cameraName && IsCameraAvailable(cam);
                });
            }

            return null;
        }

        public static void ClearHullCamStaticState()
        {
            if (s_HullCamType == null) return;
            try
            {
                var sCurrentCameraField = s_HullCamType.GetField("sCurrentCamera",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var sOrigParentField = s_HullCamType.GetField("sOrigParent",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                sCurrentCameraField?.SetValue(null, null);
                sOrigParentField?.SetValue(null, null);

                Debug.Log("[HullCamBridge] Cleared static state for scene change");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HullCamBridge] Failed to clear static state: {ex.Message}");
            }
        }

        /// <summary>
        /// Emergency fallback - keep this as a parachute for when HullCam's native restore fails.
        /// </summary>
        public static void EmergencyResetCamera()
        {
            try
            {
                RestoreMain();

                FlightCamera fc = FlightCamera.fetch;
                if (fc != null && FlightGlobals.ActiveVessel != null)
                {
                    fc.transform.parent = null;
                    fc.SetTarget(FlightGlobals.ActiveVessel.transform, FlightCamera.TargetMode.Vessel);
                    fc.ActivateUpdate();
                    fc.SetFoV(60f);
                }

                if (Camera.main != null)
                    Camera.main.nearClipPlane = 0.1f;

                Debug.Log("[HullCamBridge] Emergency camera reset executed");
            }
            catch (Exception ex)
            {
                Debug.LogError("[HullCamBridge] Emergency reset failed: " + ex);
            }
        }
    }

    [Serializable]
    public class CameraSlot
    {
        public string buttonID;
        public string cameraName;
        public uint partPersistentId;
        public string vesselId;
        public bool allowAnyVessel;

        public SlotStatus GetStatus(Vessel currentVessel = null)
        {
            if (!HullCamBridge.IsAvailable) return SlotStatus.Unavailable;

            // Gray: No assignment
            if (partPersistentId == 0 && string.IsNullOrEmpty(cameraName))
                return SlotStatus.Unassigned;

            var cam = HullCamBridge.ResolveCameraSlot(this, currentVessel);

            // Red: Assigned but not found or destroyed
            if (cam == null)
                return SlotStatus.Unavailable;

            if (!HullCamBridge.IsCameraAvailable(cam))
                return SlotStatus.Unavailable;

            // Green: Currently viewing
            if (HullCamBridge.IsCameraActive(cam))
                return SlotStatus.Active;

            // Aqua: Different vessel but still usable (add this check)
            if (currentVessel != null)
            {
                Component comp = cam as Component;
                if (comp != null)
                {
                    Part part = comp.GetComponentInParent<Part>();
                    if (part != null && part.vessel != null && part.vessel != currentVessel)
                        return SlotStatus.Remote;
                }
            }

            // Yellow: Assigned, available, on this vessel, but inactive
            return SlotStatus.Assigned;
        }

        public enum SlotStatus
        {
            Unassigned,
            Assigned,
            Active,
            Remote,
            Unavailable
        }
    }
}