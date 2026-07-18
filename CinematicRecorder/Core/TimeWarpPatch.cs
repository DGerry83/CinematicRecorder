using HarmonyLib;

namespace CinematicRecorder.Core
{
    [HarmonyPatch(typeof(TimeWarp))]
    [HarmonyPatch("fixedDeltaTime")]
    [HarmonyPatch(MethodType.Getter)]
    public static class TimeWarp_FixedDeltaTime_Patch
    {
        public static float OverrideValue { get; set; } = 0.02f;
        public static bool IsOverridden { get; set; } = false;
        static bool Prefix(ref float __result)
        {
            if (IsOverridden)
            {
                __result = OverrideValue;
                return false; // Skip original getter - return our value instead
            }
            return true; // Run original getter
        }
    }
}