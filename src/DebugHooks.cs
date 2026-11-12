// Debug logging: which camera effects play and when the game swaps cameras (to find what doubles after a hit).
using HarmonyLib;
using UnityEngine;
using Utilities.CameraEffects;

namespace SHMCDVR
{
    [HarmonyPatch(typeof(CameraEffectsInterpolator), "Play", new[] { typeof(Utilities.CameraEffects.PlayMode), typeof(float), typeof(int), typeof(float) })]
    static class LogEffectPlay
    {
        static void Prefix(CameraEffectsInterpolator __instance, Utilities.CameraEffects.PlayMode playMode)
        {
            if (Plugin.LogEffects.Value)
                Plugin.Log.LogInfo("[fx] " + Time.realtimeSinceStartup.ToString("F2") + " play '" + __instance.Id + "' " + playMode);
        }
    }

    [HarmonyPatch(typeof(CameraManager), "ReplaceStandardCamera")]
    static class LogReplaceCamera
    {
        static void Prefix(Camera targetCamera)
        {
            if (Plugin.LogEffects.Value)
                Plugin.Log.LogInfo("[cam] " + Time.realtimeSinceStartup.ToString("F2") + " replace with '" + (targetCamera != null ? targetCamera.name : "null") + "'");
        }
    }

    [HarmonyPatch(typeof(CameraManager), "ReturnToStandardCamera")]
    static class LogReturnCamera
    {
        static void Prefix()
        {
            if (Plugin.LogEffects.Value)
                Plugin.Log.LogInfo("[cam] " + Time.realtimeSinceStartup.ToString("F2") + " return to standard");
        }
    }

    // NOTE: patching PlayerStats.ReceiveDamage deadlocks Mono at startup (0% CPU hang). Don't.
}
