using CinematicRecorder.Patches;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace CinematicRecorder.Core
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyBootstrap : MonoBehaviour
    {
        private static bool _patched;
        private const string HARMONY_ID = "com.cinematicrecorder";

        void Awake()
        {
            if (_patched) return;

            var harmony = new Harmony(HARMONY_ID);

            PatchCoreSystems(harmony);
            PatchOptionalHullCam(harmony);

            _patched = true;
            Debug.Log("[CinematicRecorder] Harmony patches applied successfully");
        }

        /// <summary>
        /// Patches CinematicRecorder's own systems (TimeWarp, etc.)
        /// </summary>
        private void PatchCoreSystems(Harmony harmony)
        {
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Debug.Log("[CinematicRecorder] Core systems patched");
        }

        /// <summary>
        /// Conditionally patches HullCam VDS if the mod is present.
        /// Uses late binding to avoid hard dependency.
        /// </summary>
        private void PatchOptionalHullCam(Harmony harmony)
        {
            Debug.Log("[CinematicRecorder] Checking for HullCam...");
            if (!IsHullCamInstalled())
            {
                Debug.Log("[CinematicRecorder] HullCam not detected, skipping optional patches");
                return;
            }

            Debug.Log("[CinematicRecorder] HullCam detected, applying patches...");
            try
            {
                ApplyHullCamPatches(harmony);
                Debug.Log("[CinematicRecorder] HullCam integration patches applied");
            }
            catch (Exception ex)
            {
                Debug.LogError("[CinematicRecorder] Failed to patch HullCam: " + ex.Message);
            }
        }

        /// <summary>
        /// Detects if HullcamVDS or HullcamVDSContinued is loaded.
        /// </summary>
        private bool IsHullCamInstalled()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "HullcamVDSContinued" ||
                          a.GetName().Name == "HullcamVDS");
        }

        /// <summary>
        /// Applies manual Harmony patches to HullCam's internal methods.
        /// Uses reflection to avoid compile-time dependency on HullCam types.
        /// </summary>
        private void ApplyHullCamPatches(Harmony harmony)
        {
            Type hullCamType = ResolveHullCamType();
            if (hullCamType == null)
                throw new InvalidOperationException("Could not resolve MuMechModuleHullCamera type");

            PatchHullCamActivate(harmony, hullCamType);
            PatchHullCamRestoreMain(harmony, hullCamType);
        }

        /// <summary>
        /// Resolves the HullCam camera module type from loaded assemblies.
        /// </summary>
        private Type ResolveHullCamType()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "HullcamVDS.MuMechModuleHullCamera");
        }

        /// <summary>
        /// Patches MuMechModuleHullCamera.Activate() to properly clear previous camera state.
        /// </summary>
        private void PatchHullCamActivate(Harmony harmony, Type hullCamType)
        {
            MethodInfo activateMethod = hullCamType.GetMethod("Activate",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (activateMethod == null)
                throw new MissingMethodException("Could not find Activate method");

            MethodInfo prefix = typeof(HullCamPatches).GetMethod("Activate_Prefix");
            if (prefix == null)
                throw new MissingMethodException("Activate_Prefix patch method not found");

            Debug.Log("[CinematicRecorder] Patching HullCam.Activate with prefix: " + prefix.Name);
            harmony.Patch(activateMethod, prefix: new HarmonyMethod(prefix));
            Debug.Log("[CinematicRecorder] HullCam.Activate patched successfully");
        }

        /// <summary>
        /// Patches MuMechModuleHullCamera.RestoreMainCamera() to prevent null reference corruption.
        /// </summary>
        private void PatchHullCamRestoreMain(Harmony harmony, Type hullCamType)
        {
            MethodInfo restoreMethod = hullCamType.GetMethod("RestoreMainCamera",
                BindingFlags.NonPublic | BindingFlags.Static);

            if (restoreMethod == null)
                throw new MissingMethodException("Could not find RestoreMainCamera method");

            MethodInfo prefix = typeof(HullCamPatches).GetMethod("RestoreMain_Prefix");
            MethodInfo postfix = typeof(HullCamPatches).GetMethod("RestoreMain_Postfix");

            harmony.Patch(restoreMethod,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix));
        }
    }
}