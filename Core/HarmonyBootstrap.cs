using HarmonyLib;
using UnityEngine;

namespace CinematicRecorder.Core
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class HarmonyBootstrap : MonoBehaviour
    {
        private static bool _patched;

        void Awake()
        {
            if (_patched)
                return;

            _patched = true;

            var harmony = new Harmony("com.cinematicrecorder.deterministic");
            harmony.PatchAll();

            Debug.Log("[CinematicRecorder] Harmony patches applied.");
        }
    }
}
