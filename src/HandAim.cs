// Hand aiming: the game aims everything along the head (StandardCamera forward / viewport-centre ray).
// In VR gameplay we substitute the right controller's aim ("tip") pose:
//   CameraManager.GetCrosshairRay  -> guns, melee, katana, pickup hover, crosshair
//   Gun.GetVirtualMuzzlePosition   -> bullets start at the hand, not beside the head
//   Weapon/Katana.SpawnThrowAway   -> camera transform briefly moved to the hand so throws leave the hand along it
using System;
using HarmonyLib;
using UnityEngine;
using Valve.VR;

namespace SHMCDVR
{
    internal static class HandAim
    {
        static bool Usable()
        {
            return Plugin.VR && Plugin.HandAim.Value && VRControllers.Right.PoseValid && VRControllerDevice.InGameplay()
                && CameraManager.Instance != null && CameraManager.Instance.StandardCameraTransform != null;
        }

        // Aim pose in world space. The tracked rig is the game camera's parent: Unity applies the HMD pose to the
        // camera's local transform in the same seated space we read the controller in.
        public static bool TryGetPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Usable()) return false;
            return TryGetPoseAny(out pos, out rot);
        }

        // Same pose without the "in gameplay / hand aim on" gate (for drawing the hand model during intros).
        public static bool TryGetPoseAny(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Plugin.VR || !VRControllers.Right.PoseValid || CameraManager.Instance == null || CameraManager.Instance.StandardCameraTransform == null) return false;
            Transform rig = CameraManager.Instance.StandardCameraTransform.parent;
            VRControllers.Hand r = VRControllers.Right;
            Quaternion local = r.PoseRot * Quaternion.Euler(Plugin.AimPitchOffset.Value, 0f, 0f);
            pos = rig != null ? rig.TransformPoint(r.PosePos) : r.PosePos;
            rot = rig != null ? rig.rotation * local : local;
            return true;
        }

        // ---- Throw by hand motion (grip release): fly the way the hand was moving ----
        static Vector3 releaseVel;
        static float releaseTime = -10f;

        public static void NoteRelease(Vector3 trackingVelocity) { releaseVel = trackingVelocity; releaseTime = Time.unscaledTime; }

        // Direction for a throw that is happening now: the hand's motion if it was just released with enough speed.
        public static bool TryGetThrowDirection(out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!Plugin.ThrowByHandMotion.Value || Time.unscaledTime - releaseTime > 0.5f || releaseVel.magnitude < Plugin.ThrowMinSpeed.Value
                || CameraManager.Instance == null || CameraManager.Instance.StandardCameraTransform == null) return false;
            Transform rig = CameraManager.Instance.StandardCameraTransform.parent;
            dir = (rig != null ? rig.rotation * releaseVel : releaseVel).normalized;
            releaseTime = -10f;   // use once
            return true;
        }

        public static bool TryGetLeftPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (!Plugin.VR || !VRControllers.Left.PoseValid || CameraManager.Instance == null || CameraManager.Instance.StandardCameraTransform == null) return false;
            Transform rig = CameraManager.Instance.StandardCameraTransform.parent;
            VRControllers.Hand l = VRControllers.Left;
            Quaternion local = l.PoseRot * Quaternion.Euler(Plugin.AimPitchOffset.Value, 0f, 0f);
            pos = rig != null ? rig.TransformPoint(l.PosePos) : l.PosePos;
            rot = rig != null ? rig.rotation * local : local;
            return true;
        }

        // A punch borrows the game's (single) attack: aim it with the punch's own ray for a few frames.
        static int overrideUntil = -1;
        static Ray overrideRay;
        public static void OverrideAim(Ray r, int frames) { overrideRay = r; overrideUntil = Time.frameCount + frames; }
        // True while an attack triggered by a physical punch/swing is being processed.
        public static bool MotionAttackActive { get { return Time.frameCount <= overrideUntil; } }

        // Enemy body parts only (the game's melee check skips enemies' own weapons).
        public static bool IsEnemyBody(Collider col)
        {
            return col.GetComponentInParent<PejAiBodyPart>() != null && col.GetComponentInParent<MeleeWeaponPejAiWeapon>() == null
                && col.GetComponentInParent<KatanaPejAiWeapon>() == null;
        }

        // Hand pose and velocity in world space.
        public static bool TryGetHandWorld(bool right, out Vector3 pos, out Quaternion rot, out Vector3 vel)
        {
            vel = Vector3.zero;
            if (!(right ? TryGetPoseAny(out pos, out rot) : TryGetLeftPose(out pos, out rot))) return false;
            Transform rig = CameraManager.Instance.StandardCameraTransform.parent;
            Vector3 v = right ? VRControllers.Right.Velocity : VRControllers.Left.Velocity;
            vel = rig != null ? rig.rotation * v : v;
            return true;
        }

        public static bool TryGetRay(out Ray ray)
        {
            Vector3 p; Quaternion q;
            if (Time.frameCount <= overrideUntil && Usable()) { ray = overrideRay; return true; }
            if (!TryGetPose(out p, out q)) { ray = default(Ray); return false; }
            ray = new Ray(p, q * Vector3.forward);
            return true;
        }

        // Debug: Unity's head pose vs OpenVR's seated head pose; they must agree for the hand to line up.
        static float nextLog;
        public static void LogSpaceCheck()
        {
            if (!Plugin.LogEffects.Value || Time.unscaledTime < nextLog || OpenVR.System == null) return;
            nextLog = Time.unscaledTime + 5f;
            var poses = new TrackedDevicePose_t[1];
            OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseSeated, 0f, poses);
            Vector3 p; Quaternion q;
            VRControllers.ToUnity(poses[0].mDeviceToAbsoluteTracking, out p, out q);
            Vector3 u = UnityEngine.XR.InputTracking.GetLocalPosition(UnityEngine.XR.XRNode.Head);
            Quaternion uq = UnityEngine.XR.InputTracking.GetLocalRotation(UnityEngine.XR.XRNode.Head);
            Plugin.Log.LogInfo(string.Format("[space] head openvr={0} unity={1} dPos={2:0.000} dAng={3:0.0} hand={4} valid={5}",
                p, u, (p - u).magnitude, Quaternion.Angle(q, uq), VRControllers.Right.PosePos, VRControllers.Right.PoseValid));
        }
    }

    [HarmonyPatch(typeof(CameraManager), "GetCrosshairRay")]
    static class HandAimRay
    {
        static void Postfix(ref Ray __result)
        {
            Ray r;
            if (HandAim.TryGetRay(out r)) __result = r;
        }
    }

    [HarmonyPatch(typeof(Gun), "GetVirtualMuzzlePosition")]
    static class HandAimMuzzle
    {
        static void Postfix(ref Vector3 __result)
        {
            Ray r;
            if (HandAim.TryGetRay(out r)) __result = r.origin + r.direction * 0.1f;
        }
    }

    // Throws read CameraManager.StandardCameraTransform position/forward directly. Move the camera onto the hand
    // for the duration of the call; Unity re-applies the HMD pose before the next render anyway.
    static class ThrowFromHand
    {
        internal struct State { public bool moved; public Vector3 pos; public Quaternion rot; }

        internal static void Begin(out State s)
        {
            s = new State();
            Vector3 p; Quaternion q;
            if (!HandAim.TryGetPose(out p, out q)) return;
            Transform cam = CameraManager.Instance.StandardCameraTransform;
            s.moved = true; s.pos = cam.localPosition; s.rot = cam.localRotation;
            Vector3 d;
            if (HandAim.TryGetThrowDirection(out d))
            {
                q = Quaternion.LookRotation(d, Vector3.up);
                if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[throw] by hand motion, dir=" + d.ToString("F2"));
            }
            cam.SetPositionAndRotation(p, q);
        }

        internal static void End(State s)
        {
            if (!s.moved) return;
            Transform cam = CameraManager.Instance.StandardCameraTransform;
            cam.localPosition = s.pos; cam.localRotation = s.rot;
        }
    }

    [HarmonyPatch(typeof(Weapon), "SpawnThrowAway")]
    static class ThrowFromHandWeapon
    {
        static void Prefix(out ThrowFromHand.State __state) { ThrowFromHand.Begin(out __state); }
        static void Finalizer(ThrowFromHand.State __state) { ThrowFromHand.End(__state); }
    }

    [HarmonyPatch(typeof(Katana), "SpawnThrowAway")]
    static class ThrowFromHandKatana
    {
        static void Prefix(out ThrowFromHand.State __state) { ThrowFromHand.Begin(out __state); }
        static void Finalizer(ThrowFromHand.State __state) { ThrowFromHand.End(__state); }
    }
}
