// SHMCD-VR: VR comfort + motion-driven time for SUPERHOT: MIND CONTROL DELETE.
// Relies on Unity 2018's built-in OpenVR (enabled by the globalgamemanagers/boot.config patch).
// C# 5 (framework csc), net35 / Mono 2.0.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;
using Utilities.CameraEffects;

namespace SHMCDVR
{
    [BepInPlugin("shmcdvr.mod", "SHMCD-VR", "0.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> LockPitch, DisableCameraEffects, MotionTime, NoFallTime;
        internal static ConfigEntry<float> IntroTimeScale, HandSpeedFull, HeadSpeedFull, HeadAngularFull, MotionDeadzone, RiseRate, FallRate;
        internal static ConfigEntry<KeyCode> RecenterKey, DumpKey, ScreenshotKey;
        internal static ConfigEntry<string> ScreenshotTool, ScreenshotDir, MeleeFlipNames;
        internal static ConfigEntry<string> ScreenWarpProperties;
        internal static ConfigEntry<float> AutoDumpAt, UIDistance, UIWidth, UIFollowAngle, CrosshairSize;
        internal static ConfigEntry<bool> WorldCrosshair, FlipHUD;
        internal static ConfigEntry<bool> UIPanelEnabled, UIHidePrecomposed, LogEffects;
        internal static ConfigEntry<bool> EnableControllers, HeadRelativeMove, LogControllerButtons;
        internal static ConfigEntry<float> SnapTurnAngle, HapticStrength, AimPitchOffset;
        internal static ConfigEntry<bool> HandAim, GripGrabRelease, EmptyGunTriggerThrow, ThrowByHandMotion, MotionPunch, FistOnGrip, MeleeFlip, KnifeFlip, OneHitKills, Hands3D, HandModelOnController, HideArms, CapWrists;
        internal static ConfigEntry<float> HandModelScale, ForearmCut, WristStub, ThrowMinSpeed, FistThumbLength, PunchSpeed, ContactPunchSpeed, ContactRadius, MeleeSwingSpeed, MeleeReach, MeleeGripAt, MeleeAngle, MeleeHandRoll, KnifeHandRoll;
        internal static ConfigEntry<Vector3> HandModelOffset;

        internal static bool VR { get { return XRSettings.enabled && XRDevice.isPresent; } }

        private void Awake()
        {
            Log = Logger;
            LockPitch = Config.Bind("Comfort", "LockPitch", true, "Stick/mouse only turns left/right; your head does pitch.");
            DisableCameraEffects = Config.Bind("Comfort", "DisableCameraEffects", true, "Disable camera shake, roll, rotation and position-offset effects that move your view.");
            ScreenWarpProperties = Config.Bind("Comfort", "ScreenWarpProperties",
                "_FishEyeIntensityX,_FishEyeIntensityY,_MenuFishEyeIntensity,_ScaleFactor,_MenuScaleFactor",
                "Full-screen shader values held neutral in VR (they warp each eye differently -> double vision). Add _MosaicFactorX,_MosaicFactorY to also remove the pixelation.");
            MotionTime = Config.Bind("Time", "MotionTime", true, "Head and hand movement make time move (like SUPERHOT VR).");
            NoFallTime = Config.Bind("Time", "NoFallTime", true, "Do not speed up time while falling unless you jumped (stops the speed-up at level start).");
            IntroTimeScale = Config.Bind("Time", "IntroTimeScale", 0.5f, "Speed of the scripted full-speed time at level starts (1 = as the game, lower = slower intro).");
            HandSpeedFull = Config.Bind("Time", "HandSpeedFull", 1.5f, "Hand speed (m/s) that gives full-speed time.");
            HeadSpeedFull = Config.Bind("Time", "HeadSpeedFull", 1.0f, "Head speed (m/s) that gives full-speed time.");
            HeadAngularFull = Config.Bind("Time", "HeadAngularFull", 240f, "Head turn speed (deg/s) that gives full-speed time.");
            MotionDeadzone = Config.Bind("Time", "MotionDeadzone", 0.04f, "Fraction of full speed ignored (tracking jitter, breathing).");
            RiseRate = Config.Bind("Time", "RiseRate", 25f, "How fast time speeds up when you move (1/s).");
            FallRate = Config.Bind("Time", "FallRate", 6f, "How fast time slows back down when you stop (1/s).");
            RecenterKey = Config.Bind("Keys", "Recenter", KeyCode.F8, "Recenter the headset view.");
            UIPanelEnabled = Config.Bind("UI", "FloatingPanel", true, "Show the game's 2D UI on a floating panel instead of pasted on each eye.");
            UIHidePrecomposed = Config.Bind("UI", "HidePrecomposed", true, "Also hide the screen-sized precomposed menu texture from the per-eye effects (title text otherwise doubles).");
            UIDistance = Config.Bind("UI", "Distance", 2.0f, "Panel distance from your head (m).");
            UIWidth = Config.Bind("UI", "Width", 2.4f, "Panel width (m).");
            UIFollowAngle = Config.Bind("UI", "FollowAngle", 25f, "Panel re-centres in front of you when you look more than this many degrees away.");
            WorldCrosshair = Config.Bind("UI", "WorldCrosshair", true, "Replace the game's screen-painted crosshair (lands off-centre per eye on canted/asymmetric headsets) with one 3D crosshair on the surface you look at.");
            FlipHUD = Config.Bind("UI", "FlipHUD", true, "Show the HUD panel (hearts, tags, big tutorial text) the right way up.");
            CrosshairSize = Config.Bind("UI", "CrosshairSize", 4f, "World crosshair texture size in degrees (the dot fills about a tenth of it).");
            EnableControllers = Config.Bind("Controllers", "Enabled", true, "Use motion controllers (SteamVR Input) as a gamepad.");
            HeadRelativeMove = Config.Bind("Controllers", "HeadRelativeMove", true, "Left stick moves toward where you are looking.");
            SnapTurnAngle = Config.Bind("Controllers", "SnapTurnAngle", 30f, "Degrees per right-stick snap turn.");
            HapticStrength = Config.Bind("Controllers", "HapticStrength", 1f, "Controller rumble strength (0-1).");
            GripGrabRelease = Config.Bind("Controllers", "GripGrabRelease", true, "SUPERHOT VR style right grip: squeeze to pick up, let go to throw. Off = the game's own scheme (right trigger attacks/picks up, right B throws), which matches the tutorials.");
            EmptyGunTriggerThrow = Config.Bind("Controllers", "EmptyGunTriggerThrow", true, "Right trigger on an empty gun throws it immediately (no cooldown wait / no-ammo click).");
            Hands3D = Config.Bind("Controllers", "Hands3D", true, "Draw your arms and weapon as real 3D objects (the game's flat always-on-top viewmodel causes double vision in VR).");
            HandModelOnController = Config.Bind("Controllers", "HandModelOnController", true, "Show the arms and weapon at your right controller instead of in front of your face.");
            HideArms = Config.Bind("Controllers", "HideArms", true, "Show only hands and weapons (no forearms reaching back to the body).");
            CapWrists = Config.Bind("Controllers", "CapWrists", true, "Close the open wrists left after hiding the arms.");
            WristStub = Config.Bind("Controllers", "WristStub", 0.07f, "Bare hands: how much wrist/forearm to keep around the wrist joint (metres).");
            ForearmCut = Config.Bind("Controllers", "ForearmCut", 0.18f, "Pistol: how much of the wrist to keep behind the grip (metres).");
            HandModelScale = Config.Bind("Controllers", "HandModelScale", 1f, "Size of the arms/weapon model in your hand.");
            HandModelOffset = Config.Bind("Controllers", "HandModelOffset", Vector3.zero, "Shift of the hand model relative to the controller (metres; x right, y up, z forward).");
            ThrowByHandMotion = Config.Bind("Controllers", "ThrowByHandMotion", true, "Grip-release throws fly the way your hand was moving (else where it points).");
            ThrowMinSpeed = Config.Bind("Controllers", "ThrowMinSpeed", 1.0f, "Hand speed (m/s) needed for a motion throw; slower releases throw where the hand points.");
            MotionPunch = Config.Bind("Controllers", "MotionPunch", true, "Empty hand + grip squeezed = fist; a fast forward jab punches (either hand). The grip squeeze alone then only picks up targeted items.");
            FistOnGrip = Config.Bind("Controllers", "FistOnGrip", true, "Bare hands close into a fist while you squeeze that hand's grip.");
            ContactPunchSpeed = Config.Bind("Controllers", "ContactPunchSpeed", 1.0f, "Fist speed (m/s) at which touching an enemy counts as a punch.");
            ContactRadius = Config.Bind("Controllers", "ContactRadius", 0.18f, "How close (m) the fist must get to an enemy body part for a contact punch.");
            FistThumbLength = Config.Bind("Controllers", "FistThumbLength", 0.7f, "Thumb length while making a fist (1 = normal).");
            OneHitKills = Config.Bind("Gameplay", "OneHitKills", true, "A punch or melee hit takes an enemy down at once, like SUPERHOT VR (bosses and armour keep their own rules).");
            MeleeFlip = Config.Bind("Controllers", "MeleeFlip", true, "Hold melee weapons through the closed fist, pointing up out of the thumb side.");
            MeleeHandRoll = Config.Bind("Controllers", "MeleeHandRoll", 180f, "Roll of the hand (degrees, around the pointing direction) while holding a melee weapon.");
            MeleeFlipNames = Config.Bind("Controllers", "MeleeFlipNames", "bat,baseball,kij", "Melee weapons (model name contains any of these, comma separated) held with the knife hand pose (KnifeHandRoll). The log line [meleepos] shows each model name.");
            KnifeHandRoll = Config.Bind("Controllers", "KnifeHandRoll", 0f, "Hand roll (degrees) while holding a knife.");
            KnifeFlip = Config.Bind("Controllers", "KnifeFlip", true, "Hold knives the other way up (blade/handle swapped) compared with other melee weapons.");
            MeleeAngle = Config.Bind("Controllers", "MeleeAngle", 50f, "Melee weapons point this many degrees above where the controller points.");
            MeleeGripAt = Config.Bind("Controllers", "MeleeGripAt", 0.2f, "Where your fist holds a melee weapon, from its bottom end (0) to its top (1).");
            MeleeSwingSpeed = Config.Bind("Controllers", "MeleeSwingSpeed", 1.5f, "Hand speed (m/s) for a melee weapon swing to hit.");
            MeleeReach = Config.Bind("Controllers", "MeleeReach", 0.9f, "How far (m) in front of your hand a melee weapon swing can hit.");
            PunchSpeed = Config.Bind("Controllers", "PunchSpeed", 1.8f, "Forward hand speed (m/s) that counts as a punch.");
            HandAim = Config.Bind("Controllers", "HandAim", true, "Aim guns, punches and throws with the right controller instead of your head.");
            AimPitchOffset = Config.Bind("Controllers", "AimPitchOffset", 0f, "Tilt of the aim line relative to the controller (degrees, positive = down).");
            LogControllerButtons = Config.Bind("Debug", "LogControllerButtons", true, "Log controller button changes.");
            LogEffects = Config.Bind("Debug", "LogEffects", true, "Log camera effects, camera swaps and player damage.");
            AutoDumpAt = Config.Bind("Debug", "AutoDumpAt", 0f, "If > 0, log cameras and image effects once this many seconds after startup.");
            ScreenshotKey = Config.Bind("Keys", "StereoScreenshot", KeyCode.F12, "Save a stereo screenshot of the headset view.");
            ScreenshotTool = Config.Bind("Keys", "ScreenshotTool", "", "Tool used for stereo screenshots (empty = BepInEx\\plugins\\shmcdvr_tools\\VrProbe.exe).");
            ScreenshotDir = Config.Bind("Keys", "ScreenshotDir", "", "Where stereo screenshots are saved (empty = BepInEx\\shmcdvr_shots).");
            DumpKey = Config.Bind("Keys", "DumpCameraHierarchy", KeyCode.F9, "Log the camera hierarchy (debug).");

            // Patch one class at a time with a flushed trace, so a hang/failure points at its patch.
            var harmony = new Harmony("shmcdvr.mod");
            string trace = System.IO.Path.Combine(Paths.BepInExRootPath, "shmcdvr_patch_trace.txt");
            System.IO.File.WriteAllText(trace, "");
            foreach (System.Type t in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                System.IO.File.AppendAllText(trace, "patching " + t.Name + "\n");
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (System.Exception e) { Log.LogError("Patch " + t.Name + " failed: " + e); }
                System.IO.File.AppendAllText(trace, "  done " + t.Name + "\n");
            }
            var go = new GameObject("SHMCDVR");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<VRDriver>();
            go.AddComponent<UIPanel>();
            go.AddComponent<VRInputDriver>();
            go.AddComponent<VRHands>();
            go.AddComponent<TimeStartLog>();
            SteamSetup.CheckSteamEntry();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Log.LogInfo("[scene] loaded '" + s.name + "' (" + m + ")");
            Log.LogInfo("SHMCD-VR loaded. XR enabled=" + XRSettings.enabled + " device=" + XRSettings.loadedDeviceName + " present=" + XRDevice.isPresent);
        }
    }

    public class VRDriver : MonoBehaviour
    {
        private InstantTimeShifter shifter;
        private TimeControl shifterOwner;
        private float motion;
        private bool havePrev;
        private Vector3 prevHead, prevLeft, prevRight;
        private Quaternion prevHeadRot;
        private int recenterIn = -1;
        private float logTimer;
        private bool autoDumped;

        private void Start()
        {
            if (!Plugin.VR) { Plugin.Log.LogWarning("VR not active; plugin idle."); return; }
            XRDevice.SetTrackingSpaceType(TrackingSpaceType.Stationary);
            recenterIn = 30;
        }

        private void Update()
        {
            if (!Plugin.VR) return;

            if (recenterIn > 0 && --recenterIn == 0) Recenter("startup");
            if (Input.GetKeyDown(Plugin.RecenterKey.Value)) Recenter("key");
            if (Input.GetKeyDown(Plugin.DumpKey.Value)) { DumpCameras(); EffectsDebug.DumpImageEffects(); EffectsDebug.DumpInterpolators(); EffectsDebug.SaveGlobalTextures(System.IO.Path.Combine(Paths.BepInExRootPath, "shmcdvr_dump")); DumpHands(); }
            if (Input.GetKeyDown(KeyCode.F10)) EffectsDebug.Play("PlayerHit");
            // The desktop window is rarely focused in VR, so Unity misses keys: poll the key globally (Windows).
            bool shotDown = (GetAsyncKeyState(VirtualKey(Plugin.ScreenshotKey.Value)) & 0x8000) != 0;
            if ((shotDown && !shotWasDown) || Input.GetKeyDown(Plugin.ScreenshotKey.Value)) { if (Time.unscaledTime > nextShot) { nextShot = Time.unscaledTime + 1f; TakeStereoShot(); } }
            shotWasDown = shotDown;
            if (Input.GetKeyDown(KeyCode.F11))
            {
                // Debug: force real-time so enemies act without the player moving.
                TimeControl.forcedTimeScale = TimeControl.forcedTimeScale == 1f ? -1f : 1f;
                Plugin.Log.LogInfo("forcedTimeScale = " + TimeControl.forcedTimeScale);
            }
            if (Plugin.AutoDumpAt.Value > 0f && !autoDumped && Time.realtimeSinceStartup > Plugin.AutoDumpAt.Value)
            {
                autoDumped = true;
                Plugin.Log.LogInfo("--- auto dump at " + Time.realtimeSinceStartup.ToString("F0") + "s");
                DumpCameras(); EffectsDebug.DumpImageEffects();
                EffectsDebug.SaveGlobalTextures(System.IO.Path.Combine(Paths.BepInExRootPath, "shmcdvr_dump"));
                var names = new System.Collections.Generic.List<string>();
                foreach (Shader s in Resources.FindObjectsOfTypeAll<Shader>()) names.Add(s.name);
                names.Sort();
                System.IO.File.WriteAllLines(System.IO.Path.Combine(Paths.BepInExRootPath, "shmcdvr_dump\\shaders.txt"), names.ToArray());
            }

            UpdateMotionTime();
        }

        private static void Recenter(string why)
        {
            InputTracking.Recenter();
            Plugin.Log.LogInfo("Recentered (" + why + ")");
        }

        private void UpdateMotionTime()
        {
            float dt = Time.unscaledDeltaTime;
            Vector3 head = InputTracking.GetLocalPosition(XRNode.Head);
            Quaternion headRot = InputTracking.GetLocalRotation(XRNode.Head);
            Vector3 left = InputTracking.GetLocalPosition(XRNode.LeftHand);
            Vector3 right = InputTracking.GetLocalPosition(XRNode.RightHand);

            float target = 0f;
            if (havePrev && dt > 0.0001f && dt < 0.25f)
            {
                float headLin = (head - prevHead).magnitude / dt / Plugin.HeadSpeedFull.Value;
                float headAng = Quaternion.Angle(headRot, prevHeadRot) / dt / Plugin.HeadAngularFull.Value;
                // A hand that is not tracked reports a frozen position, which reads as zero speed.
                float hands = Mathf.Max((left - prevLeft).magnitude, (right - prevRight).magnitude) / dt / Plugin.HandSpeedFull.Value;
                target = Mathf.Max(headLin, Mathf.Max(headAng, hands));
                float dz = Plugin.MotionDeadzone.Value;
                target = Mathf.Clamp01((target - dz) / (1f - dz));
            }
            prevHead = head; prevHeadRot = headRot; prevLeft = left; prevRight = right; havePrev = true;

            float rate = target > motion ? Plugin.RiseRate.Value : Plugin.FallRate.Value;
            motion = Mathf.Lerp(motion, target, 1f - Mathf.Exp(-rate * dt));

            if (!Plugin.MotionTime.Value || TimeControl.Instance == null) return;
            // TimeControl resets its registry per level (Awake) and on TerminateAllTimeShifters: re-register when missing.
            if (shifter == null || shifterOwner != TimeControl.Instance || !TimeControl._timeShifterRegistry.Contains(shifter))
            {
                shifter = new InstantTimeShifter();
                TimeControl._timeShifterRegistry.Add(shifter);
                shifterOwner = TimeControl.Instance;
                Plugin.Log.LogInfo("Motion time shifter registered");
            }
            shifter.CurrentTimeModifier = motion;

            logTimer += dt;
            if (logTimer > 10f)
            {
                logTimer = 0f;
                Plugin.Log.LogInfo("motion="+ motion.ToString("F2") + " timeScale=" + Time.timeScale.ToString("F3"));
            }
        }

        // Where the first-person arms / weapon hang, relative to the game camera.
        internal static void DumpHands()
        {
            HandManager h = HandManager.THIS;
            if (h == null || CameraManager.Instance == null) { Plugin.Log.LogInfo("[hands] no HandManager"); return; }
            Transform cam = CameraManager.Instance.StandardCameraTransform;
            Plugin.Log.LogInfo("[hands] HandManager at " + PathOf(h.transform) + " weapon=" + (h.weapon != null ? PathOf(h.weapon.transform) : "null") + " weaponBone=" + (h.weaponBone != null ? PathOf(h.weaponBone.transform) : "null"));
            Transform root = h.transform;
            while (root.parent != null && root.parent != cam && root.parent != cam.parent) root = root.parent;
            DumpTree(root, cam, 0, 6);
            Transform standard = h.weapon != null ? h.weapon.transform.parent : null;
            if (standard == null) return;
            Plugin.Log.LogInfo("[hands] Standard parent=" + PathOf(standard) + " local=" + standard.localPosition.ToString("F3") + " rot=" + standard.localEulerAngles.ToString("F0") + " scale=" + standard.localScale.ToString("F3"));
            foreach (Transform t in standard.GetComponentsInChildren<Transform>(true))
                if (t.name.Contains("Wrist") || t.name.Contains("Aux") || t.name == "jnt_Root" || t.name.Contains("muzzle") || t.name.Contains("Muzzle"))
                    Plugin.Log.LogInfo("[hands] bone " + t.name + " camLocal=" + cam.InverseTransformPoint(t.position).ToString("F3") + " rot=" + (Quaternion.Inverse(cam.rotation) * t.rotation).eulerAngles.ToString("F0") + " lossy=" + t.lossyScale.ToString("F3"));
            foreach (MeshFilter mf in standard.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m == null) continue;
                string ext = "";
                if (m.isReadable)
                {
                    Vector3[] v = m.vertices;
                    Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
                    foreach (Vector3 p in v) { Vector3 s = standard.InverseTransformPoint(mf.transform.TransformPoint(p)); mn = Vector3.Min(mn, s); mx = Vector3.Max(mx, s); }
                    ext = " standardSpace min=" + mn.ToString("F2") + " max=" + mx.ToString("F2");
                }
                Plugin.Log.LogInfo("[hands] mesh " + mf.name + "/" + m.name + " readable=" + m.isReadable + " verts=" + m.vertexCount + " sub=" + m.subMeshCount + " localBounds=" + m.bounds.center.ToString("F2") + "/" + m.bounds.size.ToString("F2") + ext + " path=" + PathOf(mf.transform));
            }
            foreach (SkinnedMeshRenderer sm in standard.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Plugin.Log.LogInfo("[hands] skinned " + sm.name + " mesh=" + (sm.sharedMesh != null ? sm.sharedMesh.name + " readable=" + sm.sharedMesh.isReadable : "null") + " bones=" + sm.bones.Length + " root=" + (sm.rootBone != null ? sm.rootBone.name : "null") + " path=" + PathOf(sm.transform));
            foreach (SkinnedMeshRenderer sm in standard.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform b in sm.bones)
                    if (b != null && b.name == "jnt_R_Arm_Wrist")
                        foreach (Transform t in b.GetComponentsInChildren<Transform>(true))
                            Plugin.Log.LogInfo("[hands] finger(" + PathOf(sm.transform).Replace("Core Mechanics/_Player/Hands/", "") + ") " + t.name + " parent=" + (t.parent != null ? t.parent.name : "-") + " localPos=" + t.localPosition.ToString("F3") + " localRot=" + t.localEulerAngles.ToString("F0"));
            foreach (Animator an in standard.GetComponentsInChildren<Animator>(true))
                Plugin.Log.LogInfo("[hands] animator " + PathOf(an.transform) + " ctrl=" + (an.runtimeAnimatorController != null ? an.runtimeAnimatorController.name : "null") + " on=" + an.isActiveAndEnabled);
            foreach (Renderer r in standard.GetComponentsInChildren<Renderer>(true))
                Plugin.Log.LogInfo("[hands] renderer " + r.name + " " + r.GetType().Name + " on=" + r.enabled + "/" + r.gameObject.activeInHierarchy + " layer=" + r.gameObject.layer + " boundsCamLocal=" + cam.InverseTransformPoint(r.bounds.center).ToString("F3") + " size=" + r.bounds.size.ToString("F2") + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none"));
        }

        static string PathOf(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }

        static void DumpTree(Transform t, Transform cam, int depth, int max)
        {
            Vector3 lp = cam.InverseTransformPoint(t.position);
            Plugin.Log.LogInfo("[hands] " + new string(' ', depth * 2) + t.name + (t.gameObject.activeInHierarchy ? "" : " (off)")
                + " camLocal=" + lp.ToString("F2") + " rot=" + (Quaternion.Inverse(cam.rotation) * t.rotation).eulerAngles.ToString("F0")
                + " scale=" + t.lossyScale.ToString("F2") + (t.GetComponent<Renderer>() != null ? " [R layer " + t.gameObject.layer + "]" : ""));
            if (depth >= max) return;
            for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), cam, depth + 1, max);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        bool shotWasDown;
        float nextShot;

        static int VirtualKey(KeyCode k)
        {
            if (k >= KeyCode.F1 && k <= KeyCode.F15) return 0x70 + (k - KeyCode.F1);
            if (k >= KeyCode.A && k <= KeyCode.Z) return 0x41 + (k - KeyCode.A);
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return 0x30 + (k - KeyCode.Alpha0);
            return 0x7B;   // F12
        }

        // F12: stereo screenshot of what the headset shows, via the external VrProbe tool (SteamVR screenshot API).
        static void TakeStereoShot()
        {
            try
            {
                string tool = Plugin.ScreenshotTool.Value, dir = Plugin.ScreenshotDir.Value;
                if (string.IsNullOrEmpty(tool)) tool = System.IO.Path.Combine(System.IO.Path.Combine(Paths.PluginPath, "shmcdvr_tools"), "VrProbe.exe");
                if (string.IsNullOrEmpty(dir)) dir = System.IO.Path.Combine(Paths.BepInExRootPath, "shmcdvr_shots");
                if (!System.IO.File.Exists(tool)) { Plugin.Log.LogWarning("[shot] tool missing: " + tool); return; }
                System.IO.Directory.CreateDirectory(dir);
                string name = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");   // no dots: SteamVR then writes only the preview
                var psi = new System.Diagnostics.ProcessStartInfo(tool, "\"" + System.IO.Path.Combine(dir, name) + "\" fast") { UseShellExecute = false, CreateNoWindow = true };
                System.Diagnostics.Process.Start(psi);
                Plugin.Log.LogInfo("[shot] " + System.IO.Path.Combine(dir, name) + "_vr.png");
            }
            catch (System.Exception e) { Plugin.Log.LogWarning("[shot] failed: " + e.Message); }
        }

        internal static void DumpCameras()
        {
            foreach (Camera c in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (c.gameObject.scene.name == null) continue; // prefab assets
                string path = c.name;
                for (Transform t = c.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                Plugin.Log.LogInfo("Camera " + path + (c.isActiveAndEnabled ? " [on]" : " [off]") + " depth=" + c.depth +
                    " stereo=" + c.stereoEnabled + " eye=" + c.stereoTargetEye + " sep=" + c.stereoSeparation + " ortho=" + c.orthographic +
                    (c.orthographic ? " size=" + c.orthographicSize : " fov=" + c.fieldOfView) +
                    " rt=" + (c.targetTexture != null ? c.targetTexture.name + " " + c.targetTexture.width + "x" + c.targetTexture.height : "none") +
                    " clear=" + c.clearFlags + " mask=0x" + c.cullingMask.ToString("X") + " near=" + c.nearClipPlane +
                    " pos=" + c.transform.position + " rot=" + c.transform.eulerAngles + " scale=" + c.transform.lossyScale);
            }
        }
    }

    // ---- Comfort patches ----

    // Unity refuses FOV changes in VR and logs a warning every frame.
    [HarmonyPatch(typeof(CameraProxy), "LateUpdate")]
    static class NoFovWhileVR
    {
        static bool Prefix() { return !Plugin.VR; }
    }

    [HarmonyPatch(typeof(CameraLook), "Update")]
    static class LockPitchPatch
    {
        static void Postfix(CameraLook __instance)
        {
            if (!Plugin.VR || !Plugin.LockPitch.Value || __instance.pivotY == null) return;
            __instance.rotationY = 0f;
            Vector3 e = __instance.pivotY.localEulerAngles;
            __instance.pivotY.localEulerAngles = new Vector3(0f, e.y, 0f);
        }
    }

    static class EffectGate
    {
        internal static bool Allow { get { return !Plugin.VR || !Plugin.DisableCameraEffects.Value; } }
    }

    [HarmonyPatch(typeof(CameraEffectRoll), "Update")]
    static class NoRoll { static bool Prefix() { return EffectGate.Allow; } }

    [HarmonyPatch(typeof(CameraEffectRotation), "Update")]
    static class NoEffectRotation { static bool Prefix() { return EffectGate.Allow; } }

    [HarmonyPatch(typeof(CameraEffectPositionOffset), "Update")]
    static class NoEffectOffset { static bool Prefix() { return EffectGate.Allow; } }

    [HarmonyPatch(typeof(CameraShaker), "Shake")]
    static class NoShake { static bool Prefix() { return EffectGate.Allow; } }

    [HarmonyPatch(typeof(CameraShaker), "KatanaShake")]
    static class NoKatanaShake { static bool Prefix() { return EffectGate.Allow; } }

    // On death/restart the game renders ONE flat screenshot (Screen-sized, e.g. 3200x1080) and blends it into the
    // new view. Pasted per eye that mono image can't be fused -> double vision after every hit. Pass through in VR.
    [HarmonyPatch(typeof(XulmReduxImageRevealEffect), "OnRenderImage")]
    static class NoImageReveal
    {
        static bool Prefix(RenderTexture source, RenderTexture destination)
        {
            if (!Plugin.VR || !Plugin.DisableCameraEffects.Value) return true;
            Graphics.Blit(source, destination);
            return false;
        }
    }

    // Full-screen warps (fisheye, zoom) are applied per eye in screen space, so the eyes disagree -> double vision.
    // Hold those shader values at the shader's own defaults right before the blit; other effects (mosaic, darken) still play.
    [HarmonyPatch(typeof(CustomMaterialOnScreen), "OnRenderImage")]
    static class NoScreenWarps
    {
        static readonly System.Reflection.FieldInfo MaterialField = AccessTools.Field(typeof(CustomMaterialOnScreen), "material");
        static readonly System.Collections.Generic.Dictionary<Shader, float[]> Neutral = new System.Collections.Generic.Dictionary<Shader, float[]>();
        static string[] names;
        static int[] ids;
        static string configured;

        static void Prefix(CustomMaterialOnScreen __instance)
        {
            if (!Plugin.VR || !Plugin.DisableCameraEffects.Value) return;
            Material m = (Material)MaterialField.GetValue(__instance);
            if (m == null) return;
            if (configured != Plugin.ScreenWarpProperties.Value)
            {
                configured = Plugin.ScreenWarpProperties.Value;
                var list = new System.Collections.Generic.List<string>();
                foreach (string s in configured.Split(',')) if (s.Trim().Length > 0) list.Add(s.Trim());
                names = list.ToArray();
                ids = new int[names.Length];
                for (int i = 0; i < names.Length; i++) ids[i] = Shader.PropertyToID(names[i]);
                Neutral.Clear();
            }
            float[] neutral;
            if (!Neutral.TryGetValue(m.shader, out neutral))
            {
                var fresh = new Material(m.shader);
                neutral = new float[ids.Length];
                var sb = new System.Text.StringBuilder("Screen-warp neutral values for " + m.shader.name + ":");
                for (int i = 0; i < ids.Length; i++)
                {
                    neutral[i] = fresh.HasProperty(ids[i]) ? fresh.GetFloat(ids[i]) : float.NaN;
                    if (!float.IsNaN(neutral[i])) sb.Append(" ").Append(names[i]).Append("=").Append(neutral[i]);
                }
                UnityEngine.Object.Destroy(fresh);
                Neutral[m.shader] = neutral;
                Plugin.Log.LogInfo(sb.ToString());
            }
            for (int i = 0; i < ids.Length; i++)
                if (!float.IsNaN(neutral[i])) m.SetFloat(ids[i], neutral[i]);
        }
    }
}
