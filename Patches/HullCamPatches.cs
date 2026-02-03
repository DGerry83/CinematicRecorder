using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Patches
{
    public static class HullCamPatches
    {
        public static void Activate_Prefix(object __instance)
        {
            try
            {
                var hullCamType = __instance.GetType();

                // Use FlattenHierarchy so derived types (like MuMechModuleHullCameraZoom) can find base class fields
                var sCurrentCameraField = hullCamType.GetField("sCurrentCamera",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                if (sCurrentCameraField == null) return;

                var currentCam = sCurrentCameraField.GetValue(null);

                if (currentCam != null && !ReferenceEquals(currentCam, __instance))
                {
                    // Clear previous camera's active flag
                    var camActiveField = hullCamType.GetField("camActive",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                    camActiveField?.SetValue(currentCam, false);

                    // Force-clear HullCam's static reference to prevent state confusion
                    sCurrentCameraField.SetValue(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[HullCamPatch] Activate_Prefix error: " + ex);
            }
        }

        public static bool RestoreMain_Prefix()
        {
            try
            {
                // Use SelectMany (now with System.Linq) to find the type
                Type hullCamType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .FirstOrDefault(t => t.Name == "MuMechModuleHullCamera");

                if (hullCamType == null) return true;

                var sOrigParentField = hullCamType.GetField("sOrigParent",
                    BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                var sOrigParent = sOrigParentField?.GetValue(null);

                if (sOrigParent == null)
                {
                    Debug.Log("[HullCamPatch] Blocking RestoreMainCamera - sOrigParent is null");

                    // Clear current camera reference to unstick the state
                    var sCurrentCameraField = hullCamType.GetField("sCurrentCamera",
                        BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    var currentCam = sCurrentCameraField?.GetValue(null);

                    if (currentCam != null)
                    {
                        var camActiveField = hullCamType.GetField("camActive",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
                        camActiveField?.SetValue(currentCam, false);
                        sCurrentCameraField?.SetValue(null, null);
                    }

                    return false; // Skip original method to prevent NRE
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HullCamPatch] RestoreMain_Prefix error: " + ex.Message);
            }

            return true; // Run original method
        }
    }
}