using System.Collections.Generic;
// Motion-controller input: SteamVR Input actions (input\actions.json + default bindings) feed a virtual
// Xbox-style InControl device, which the game's SHInput layer reads like any gamepad.
//
//   Left stick   -> move, relative to where you LOOK (menus: navigation, unrotated)
//   Right stick  -> snap turn (mod; never passed to the game)
//   Right trigger-> attack / confirm (RT)      Right A -> jump (LT)       Right B -> throw / back (B)
//   Right grip   -> nothing by default; [Controllers] GripGrabRelease = SUPERHOT VR style (squeeze A, let go R3)
//   Left trigger -> hotswitch (X)
//   Left A       -> restart (Select)           Left B -> pause (Start)
//   Both stick clicks -> recenter the view
using System;
using InControl;
using UnityEngine;
using UnityEngine.XR;
using InputTracking = UnityEngine.XR.InputTracking;
using XRNode = UnityEngine.XR.XRNode;
using Valve.VR;

namespace SHMCDVR
{
    internal static class VRControllers
    {
        public struct Hand
        {
            public bool Connected;
            public Vector2 Stick;
            public float Trigger;
            public bool A, B, Grip, StickClick;
            public bool PoseValid;          // aim ("tip") pose, seated tracking space, Unity coordinates
            public Vector3 PosePos;
            public Quaternion PoseRot;
            public Vector3 Velocity;        // m/s, same space
        }

        public static Hand Left, Right;
        public static bool Active { get { return s_ok; } }

        const string Set = "/actions/shmcd";
        static bool s_ok, s_failed;
        static int s_lastPoll = -1;
        static ulong s_set;
        static readonly ulong[] s_stick = new ulong[2], s_click = new ulong[2], s_trig = new ulong[2], s_a = new ulong[2],
            s_b = new ulong[2], s_grip = new ulong[2], s_hap = new ulong[2], s_pose = new ulong[2];
        static VRActiveActionSet_t[] s_active;
        static float s_rumbleL, s_rumbleR;
        static readonly uint DigitalSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputDigitalActionData_t));
        static readonly uint AnalogSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputAnalogActionData_t));
        static readonly uint PoseSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(InputPoseActionData_t));
        static readonly uint ActiveSetSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(VRActiveActionSet_t));

        // Unity's built-in OpenVR owns the session; we only add an action manifest to it.
        public static void Init(string manifestPath)
        {
            try
            {
                CVRInput input = OpenVR.Input;
                if (input == null) { Plugin.Log.LogWarning("OpenVR input interface unavailable; controllers disabled."); return; }
                if (!System.IO.File.Exists(manifestPath)) { Plugin.Log.LogWarning("Action manifest missing: " + manifestPath); return; }
                EVRInputError e = input.SetActionManifestPath(manifestPath);
                if (e != EVRInputError.None) { Plugin.Log.LogWarning("SetActionManifestPath failed: " + e); return; }
                input.GetActionSetHandle(Set, ref s_set);
                string[] side = { "l", "r" };
                for (int i = 0; i < 2; i++)
                {
                    input.GetActionHandle(Set + "/in/stick_" + side[i], ref s_stick[i]);
                    input.GetActionHandle(Set + "/in/stickclick_" + side[i], ref s_click[i]);
                    input.GetActionHandle(Set + "/in/trigger_" + side[i], ref s_trig[i]);
                    input.GetActionHandle(Set + "/in/a_" + side[i], ref s_a[i]);
                    input.GetActionHandle(Set + "/in/b_" + side[i], ref s_b[i]);
                    input.GetActionHandle(Set + "/in/grip_" + side[i], ref s_grip[i]);
                    input.GetActionHandle(Set + "/out/haptic_" + side[i], ref s_hap[i]);
                    input.GetActionHandle(Set + "/in/pose_" + side[i], ref s_pose[i]);
                }
                s_active = new[] { new VRActiveActionSet_t { ulActionSet = s_set } };
                s_ok = true;
                Plugin.Log.LogInfo("SteamVR Input active (" + manifestPath + ")");
            }
            catch (Exception ex) { Plugin.Log.LogError("SteamVR Input init failed: " + ex); }
        }

        static bool Digital(ulong h)
        {
            var d = new InputDigitalActionData_t();
            return OpenVR.Input.GetDigitalActionData(h, ref d, DigitalSize, 0) == EVRInputError.None && d.bActive && d.bState;
        }

        static InputAnalogActionData_t Analog(ulong h)
        {
            var d = new InputAnalogActionData_t();
            OpenVR.Input.GetAnalogActionData(h, ref d, AnalogSize, 0);
            return d;
        }

        static void Read(int i, ref Hand h, string tag)
        {
            var st = Analog(s_stick[i]);
            var tr = Analog(s_trig[i]);
            Hand n = new Hand
            {
                Connected = st.bActive || tr.bActive,
                Stick = st.bActive ? new Vector2(st.x, st.y) : Vector2.zero,
                Trigger = tr.bActive ? Mathf.Clamp01(tr.x) : 0f,
                A = Digital(s_a[i]), B = Digital(s_b[i]), Grip = Digital(s_grip[i]), StickClick = Digital(s_click[i]),
            };
            var pd = new InputPoseActionData_t();
            if (OpenVR.Input.GetPoseActionDataForNextFrame(s_pose[i], ETrackingUniverseOrigin.TrackingUniverseSeated, ref pd, PoseSize, 0) == EVRInputError.None
                && pd.bActive && pd.pose.bPoseIsValid)
            {
                n.PoseValid = true;
                ToUnity(pd.pose.mDeviceToAbsoluteTracking, out n.PosePos, out n.PoseRot);
                n.Velocity = new Vector3(pd.pose.vVelocity.v0, pd.pose.vVelocity.v1, -pd.pose.vVelocity.v2);
            }
            if (n.Connected != h.Connected) Plugin.Log.LogInfo("Controller " + tag + (n.Connected ? " active" : " inactive"));
            if (Plugin.LogControllerButtons.Value && (n.A != h.A || n.B != h.B || n.Grip != h.Grip || n.StickClick != h.StickClick || (n.Trigger > 0.5f) != (h.Trigger > 0.5f)))
                Plugin.Log.LogInfo(string.Format("[{0}] A={1} B={2} grip={3} click={4} trig={5:0.00} stick=({6:0.00},{7:0.00})",
                    tag, n.A, n.B, n.Grip, n.StickClick, n.Trigger, n.Stick.x, n.Stick.y));
            h = n;
        }

        // OpenVR is right-handed (-Z forward), Unity left-handed: flip Z (same as SteamVR_Utils.RigidTransform).
        public static void ToUnity(HmdMatrix34_t p, out Vector3 pos, out Quaternion rot)
        {
            var m = Matrix4x4.identity;
            m[0, 0] = p.m0; m[0, 1] = p.m1; m[0, 2] = -p.m2; m[0, 3] = p.m3;
            m[1, 0] = p.m4; m[1, 1] = p.m5; m[1, 2] = -p.m6; m[1, 3] = p.m7;
            m[2, 0] = -p.m8; m[2, 1] = -p.m9; m[2, 2] = p.m10; m[2, 3] = -p.m11;
            pos = m.GetColumn(3);
            rot = Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
        }

        public static void Poll()
        {
            if (s_lastPoll == Time.frameCount || !s_ok || s_failed) return;
            s_lastPoll = Time.frameCount;
            try
            {
                OpenVR.Input.UpdateActionState(s_active, ActiveSetSize);
                Read(0, ref Left, "L");
                Read(1, ref Right, "R");
                float k = Mathf.Clamp01(Plugin.HapticStrength.Value);
                if (s_rumbleL * k > 0.05f) OpenVR.Input.TriggerHapticVibrationAction(s_hap[0], 0f, 0.02f, 160f, Mathf.Clamp01(s_rumbleL * k), 0);
                if (s_rumbleR * k > 0.05f) OpenVR.Input.TriggerHapticVibrationAction(s_hap[1], 0f, 0.02f, 160f, Mathf.Clamp01(s_rumbleR * k), 0);
            }
            catch (Exception e) { s_failed = true; Plugin.Log.LogError("Controller polling failed, controllers disabled: " + e); }
        }

        public static void SetRumble(float l, float r) { s_rumbleL = l; s_rumbleR = r; }
    }

    internal class VRControllerDevice : InControl.InputDevice
    {
        const float Lower = 0.2f, Upper = 0.9f;

        internal static VRControllerDevice Instance;

        public VRControllerDevice() : base("SHMCD-VR Motion Controllers")
        {
            Meta = "OpenVR motion controllers";
            DeviceClass = InputDeviceClass.Controller;
            DeviceStyle = InputDeviceStyle.XboxOne;
            SortOrder = 100;
            foreach (InputControlType t in new[] {
                InputControlType.LeftStickLeft, InputControlType.LeftStickRight, InputControlType.LeftStickUp, InputControlType.LeftStickDown,
                InputControlType.RightStickLeft, InputControlType.RightStickRight, InputControlType.RightStickUp, InputControlType.RightStickDown,
                InputControlType.LeftTrigger, InputControlType.RightTrigger })
                AddControl(t, t.ToString(), Lower, Upper);
            foreach (InputControlType t in new[] {
                InputControlType.Action1, InputControlType.Action2, InputControlType.Action3, InputControlType.Action4,
                InputControlType.LeftBumper, InputControlType.RightBumper, InputControlType.LeftStickButton, InputControlType.RightStickButton,
                InputControlType.Start, InputControlType.Select, InputControlType.DPadUp, InputControlType.DPadDown,
                InputControlType.DPadLeft, InputControlType.DPadRight })
                AddControl(t, t.ToString());
        }

        // True while the player is in control of a body (not paused, not in a menu): then the stick is head-relative.
        internal static bool InGameplay()
        {
            return PlayerController.Instance != null && PlayerController.Instance.isActiveAndEnabled && !Pause.PAUSED;
        }

        public override void Update(ulong tick, float dt)
        {
            VRControllers.Poll();
            VRControllers.Hand l = VRControllers.Left, r = VRControllers.Right;

            Vector2 move = l.Stick;
            if (Plugin.HeadRelativeMove.Value && InGameplay() && move.sqrMagnitude > 0.0001f)
            {
                // Body faces rig forward; rotate the stick by the head's yaw within the rig so "forward" is where you look.
                Vector3 f = Vector3.ProjectOnPlane(InputTracking.GetLocalRotation(XRNode.Head) * Vector3.forward, Vector3.up);
                if (f.sqrMagnitude > 1e-4f)
                {
                    Vector3 v = Quaternion.LookRotation(f.normalized, Vector3.up) * new Vector3(move.x, 0f, move.y);
                    move = new Vector2(v.x, v.z);
                }
            }
            Stick(InputControlType.LeftStickLeft, InputControlType.LeftStickRight, InputControlType.LeftStickUp, InputControlType.LeftStickDown, move, tick, dt);
            // Right stick is snap turn (handled by VRInputDriver); the game gets no camera input from it.
            Stick(InputControlType.RightStickLeft, InputControlType.RightStickRight, InputControlType.RightStickUp, InputControlType.RightStickDown, Vector2.zero, tick, dt);

            UpdateWithValue(InputControlType.RightTrigger, r.Trigger, tick, dt);        // attack / confirm
            UpdateWithValue(InputControlType.LeftTrigger, r.A ? 1f : 0f, tick, dt);     // jump
            UpdateWithState(InputControlType.Action2, r.B, tick, dt);                   // throw / back
            bool grab, release;
            GripLogic(r.Grip, InGameplay(), out grab, out release);
            UpdateWithState(InputControlType.Action1, grab, tick, dt);                  // grab (pick up / punch)
            UpdateWithState(InputControlType.RightStickButton, release, tick, dt);      // throw on letting go
            UpdateWithState(InputControlType.Action3, l.Trigger > 0.6f, tick, dt);      // hotswitch
            UpdateWithState(InputControlType.Select, l.A, tick, dt);                    // restart
            UpdateWithState(InputControlType.Start, l.B, tick, dt);                     // pause

            UpdateWithState(InputControlType.Action4, false, tick, dt);
            UpdateWithState(InputControlType.LeftBumper, false, tick, dt);
            UpdateWithState(InputControlType.RightBumper, false, tick, dt);
            UpdateWithState(InputControlType.LeftStickButton, false, tick, dt);
            UpdateWithState(InputControlType.DPadUp, false, tick, dt);
            UpdateWithState(InputControlType.DPadDown, false, tick, dt);
            UpdateWithState(InputControlType.DPadLeft, false, tick, dt);
            UpdateWithState(InputControlType.DPadRight, false, tick, dt);
            Commit(tick, dt);
        }

        // Right grip, SUPERHOT VR style: squeeze to pick up, let go to throw.
        // - The game picks up with its attack input, so a grip press sends attack, but only with an empty hand
        //   (with a gun in hand attack would shoot).
        // - Index "grab" mode also fires grip while you squeeze to pull the trigger, so letting go only throws once
        //   the grip has been held on an item for GripArmTime; a brief trigger squeeze never throws your gun.
        const float GripArmTime = 0.25f;
        const int ThrowFrames = 3;
        bool gripWas;
        float gripHeldOnItem;
        int throwFramesLeft, grabFramesLeft;
        Vector3 peakVel;
        float peakTime;
        const int GrabFrames = 3;

        static bool Holding()
        {
            HandManager h = HandManager.THIS;
            return h != null && h.weapon != null && !(h.weapon is TakedownWeapon);
        }

        void GripLogic(bool grip, bool gameplay, out bool grab, out bool release)
        {
            grab = false; release = false;
            if (!Plugin.GripGrabRelease.Value)
            {
                // Game scheme: the grip does nothing (Index "grab" also fires while you pull the trigger).
                gripWas = grip; gripHeldOnItem = 0f; throwFramesLeft = 0; grabFramesLeft = 0;
                return;
            }
            if (!gameplay)
            {
                grab = grip && !gripWas;   // menus: a squeeze confirms once, like the trigger
                gripWas = grip; gripHeldOnItem = 0f; throwFramesLeft = 0;
                return;
            }
            bool holding = Holding();
            if (grip && !gripWas) gripHeldOnItem = 0f;
            if (grip)
            {
                // One short attack tap on squeeze (picks up / punches). Never hold it: a held attack blocks the
                // trigger, since the game only attacks on a fresh press, and on Index the grip often stays squeezed.
                // With motion punches on, the squeeze only picks up when an item is targeted; otherwise it makes a fist.
                if (!gripWas && !holding && (!Plugin.MotionPunch.Value || PickupTargeted())) grabFramesLeft = GrabFrames;
                if (holding) gripHeldOnItem += Time.unscaledDeltaTime;
            }
            // Melee weapons: a fast swing that passes an enemy within reach of the weapon hits it.
            if (Plugin.MotionPunch.Value && holding && grip && HandManager.THIS != null && HandManager.THIS.weapon is SimpleMeleeWeapon)
            {
                Ray swing;
                if (Swing(ref rArmed, out swing)) { grabFramesLeft = GrabFrames; HandAim.OverrideAim(swing, GrabFrames + 2); }
            }
            // Motion punches: grip squeezed (fist), hand empty, a fast jab along the hand's pointing direction.
            if (Plugin.MotionPunch.Value && !holding)
            {
                Ray hit;
                if (Punch(true, grip, ref rArmed, out hit)) { grabFramesLeft = GrabFrames; HandAim.OverrideAim(hit, GrabFrames + 2); }
                if (Punch(false, VRControllers.Left.Grip, ref lArmed, out hit)) { grabFramesLeft = GrabFrames; HandAim.OverrideAim(hit, GrabFrames + 2); }
            }
            // Track the hand's recent peak velocity: at the moment of release the hand is already slowing down.
            Vector3 vel = VRControllers.Right.Velocity;
            peakVel = (Time.unscaledTime - peakTime > 0.15f || vel.sqrMagnitude > peakVel.sqrMagnitude) ? vel : peakVel;
            if (peakVel == vel) peakTime = Time.unscaledTime;
            if (!grip && gripWas && holding && gripHeldOnItem >= GripArmTime)
            {
                HandAim.NoteRelease(peakVel);
                throwFramesLeft = ThrowFrames;
                if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[grip] released -> throw");
            }
            if (!grip) gripHeldOnItem = 0f;
            if (throwFramesLeft > 0) { release = true; throwFramesLeft--; }
            if (grabFramesLeft > 0) { grab = true; grabFramesLeft--; }
            gripWas = grip;
        }

        bool rArmed = true, lArmed = true;

        static bool PickupTargeted()
        {
            return Crosshair2D.Instance != null && Crosshair2D.Instance.GetCurrentType() == CrosshairType.Hand;
        }

        // A punch, SUPERHOT VR style: with a fist made,
        //  - contact: the fist moves faster than ContactPunchSpeed and touches an enemy body part -> hit aimed at it;
        //  - jab: the fist moves faster than PunchSpeed -> hit aimed along the motion (not where the controller points).
        // Re-arms once the hand slows down.
        static bool Punch(bool right, bool fist, ref bool armed, out Ray ray)
        {
            ray = default(Ray);
            Vector3 p, v; Quaternion q;
            if (!HandAim.TryGetHandWorld(right, out p, out q, out v)) return false;
            float speed = v.magnitude;
            if (speed < 0.5f) armed = true;
            if (!fist || !armed) return false;
            Vector3 dir = speed > 1e-3f ? v / speed : q * Vector3.forward;
            if (speed >= Plugin.ContactPunchSpeed.Value)
            {
                foreach (Collider col in Physics.OverlapSphere(p, Plugin.ContactRadius.Value, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (!HandAim.IsEnemyBody(col)) continue;
                    Vector3 target = col.ClosestPoint(p);
                    if ((target - p).sqrMagnitude < 1e-6f) target = col.bounds.center;
                    Vector3 from = p - dir * 0.25f;   // start a little behind the fist so the game's ray enters the body
                    ray = new Ray(from, (target - from).normalized);
                    armed = false;
                    LogPunch((right ? "right" : "left") + " contact " + col.name + " speed " + speed.ToString("F1"));
                    return true;
                }
            }
            if (speed >= Plugin.PunchSpeed.Value)
            {
                ray = new Ray(p - dir * 0.1f, dir);
                armed = false;
                LogPunch((right ? "right" : "left") + " jab speed " + speed.ToString("F1"));
                return true;
            }
            return false;
        }

        static bool Swing(ref bool armed, out Ray ray)
        {
            ray = default(Ray);
            Vector3 p, v; Quaternion q;
            if (!HandAim.TryGetHandWorld(true, out p, out q, out v)) return false;
            float speed = v.magnitude;
            if (speed < 0.6f) armed = true;
            if (!armed || speed < Plugin.MeleeSwingSpeed.Value) return false;
            float reach = Plugin.MeleeReach.Value;
            Vector3 center = p + (q * Vector3.forward) * (reach * 0.5f);
            Collider best = null; float bestD = float.MaxValue; Vector3 bestPt = Vector3.zero;
            foreach (Collider col in Physics.OverlapSphere(center, reach * 0.5f + 0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!HandAim.IsEnemyBody(col)) continue;
                Vector3 pt = col.ClosestPoint(p);
                float d = (pt - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = col; bestPt = pt; }
            }
            if (best == null) return false;
            Vector3 from = p - (bestPt - p).normalized * 0.25f;
            ray = new Ray(from, (bestPt - from).normalized);
            armed = false;
            LogPunch("melee swing hits " + best.name + " speed " + speed.ToString("F1"));
            return true;
        }

        static void LogPunch(string what) { if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[punch] " + what); }

        void Stick(InputControlType left,InputControlType right, InputControlType up, InputControlType down, Vector2 v, ulong tick, float dt)
        {
            UpdateWithValue(left, Mathf.Max(0f, -v.x), tick, dt);
            UpdateWithValue(right, Mathf.Max(0f, v.x), tick, dt);
            float y = InputManager.InvertYAxis ? -v.y : v.y;
            UpdateWithValue(up, Mathf.Max(0f, y), tick, dt);
            UpdateWithValue(down, Mathf.Max(0f, -y), tick, dt);
        }

        public override void Vibrate(float leftMotor, float rightMotor) { VRControllers.SetRumble(leftMotor, rightMotor); }
    }

    // Keeps the device attached (InControl can reset between scenes), snap turn, and the recenter gesture.
    public class VRInputDriver : MonoBehaviour
    {
        VRControllerDevice device;
        bool loggedAttach, bothWereDown, turnArmed = true;
        string lastDeviceList;
        static readonly System.Reflection.FieldInfo FocusField = HarmonyLib.AccessTools.Field(typeof(InputManager), "applicationIsFocused");
        float nextMoveLog;

        void Start()
        {
            if (Plugin.VR) SteamSetup.RemoveOldLibraryEntry();
            if (!Plugin.VR || !Plugin.EnableControllers.Value) { enabled = false; return; }
            string manifest = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "shmcdvr_input\\actions.json");
            VRControllers.Init(System.IO.Path.GetFullPath(manifest));
        }

        void Update()
        {
            if (!VRControllers.Active) return;
            // InControl stops updating every device while the desktop window is unfocused (SuspendInBackground).
            // In VR the window loses focus all the time (SteamVR dashboard, other apps): keep input running.
            if (FocusField != null && !(bool)FocusField.GetValue(null))
            {
                FocusField.SetValue(null, true);
                Plugin.Log.LogInfo("[input] game window lost focus; kept controller input running");
            }
            if (InputManager.IsSetup)
            {
                if (device == null) { device = new VRControllerDevice(); VRControllerDevice.Instance = device; }
                if (!InputManager.Devices.Contains(device))
                {
                    InputManager.AttachDevice(device);
                    if (!loggedAttach) { Plugin.Log.LogInfo("Motion controller device attached to InControl"); loggedAttach = true; }
                }
                // The game sums the left stick over ALL devices (SHInputMovement), and Unity also exposes each Index
                // controller as a raw joystick with unrelated axes -> phantom/cancelled movement. Keep only ours.
                if (VRControllers.Left.Connected || VRControllers.Right.Connected)
                {
                    for (int i = InputManager.Devices.Count - 1; i >= 0; i--)
                    {
                        InControl.InputDevice d = InputManager.Devices[i];
                        if (d == device || d.Name == "Keyboard/Mouse" || d.Name == InputSystem.SHInput.keyboardMouse) continue;
                        if ((d.Name + " " + d.Meta).IndexOf("OpenVR", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        InputManager.DetachDevice(d);
                        Plugin.Log.LogInfo("[input] detached raw VR joystick: " + d.Name + " (" + d.Meta + ")");
                    }
                }
                string list = "";
                for (int i = 0; i < InputManager.Devices.Count; i++) list += (i > 0 ? ", " : "") + InputManager.Devices[i].Name;
                if (list != lastDeviceList) { Plugin.Log.LogInfo("[input] devices: " + list); lastDeviceList = list; }
            }

            VRControllers.Poll();
            var l = VRControllers.Left; var r = VRControllers.Right;

            bool both = l.StickClick && r.StickClick;
            if (both && !bothWereDown) { InputTracking.Recenter(); Plugin.Log.LogInfo("Recentered (stick clicks)"); }
            bothWereDown = both;
            HandAim.LogSpaceCheck();
            if (Plugin.LogControllerButtons.Value && l.Stick.sqrMagnitude > 0.25f && Time.unscaledTime > nextMoveLog && device != null)
            {
                nextMoveLog = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo(string.Format("[move] stick=({0:0.00},{1:0.00}) device=({2:0.00},{3:0.00}) active={4} gameplay={5} devices={6}",
                    l.Stick.x, l.Stick.y, device.LeftStickX.Value, device.LeftStickY.Value, InputManager.ActiveDevice.Name, VRControllerDevice.InGameplay(), InputManager.Devices.Count));
            }

            // Snap turn: flick the right stick past 0.7, re-arm once it returns under 0.35.
            float x = r.Stick.x;
            if (Mathf.Abs(x) < 0.35f) turnArmed = true;
            if (turnArmed && Mathf.Abs(x) > 0.7f && VRControllerDevice.InGameplay() && CameraLook.instance != null && !CameraLook.locked)
            {
                turnArmed = false;
                float angle = Mathf.Sign(x) * Plugin.SnapTurnAngle.Value;
                CameraLook.instance.pivotX.Rotate(0f, angle, 0f);
            }
        }
    }

    // InControl hands the game whichever device changed last. Unity also exposes each Index controller as a raw
    // joystick, so a touch or a little drift on one makes it the active device: no walking, no mapped buttons.
    // While our motion-controller device is attached and the controllers are live, keep it active.
    [HarmonyLib.HarmonyPatch(typeof(InputManager), "UpdateActiveDevice")]
    static class PinActiveDevice
    {
        static InControl.InputDevice last;
        static readonly System.Reflection.FieldInfo ActiveField = HarmonyLib.AccessTools.Field(typeof(InputManager), "activeDevice");
        static void Postfix()
        {
            VRControllerDevice d = VRControllerDevice.Instance;
            InControl.InputDevice before = InputManager.ActiveDevice;
            if (d != null && VRControllers.Active && (VRControllers.Left.Connected || VRControllers.Right.Connected)
                && InputManager.Devices.Contains(d) && before != d)
                ActiveField.SetValue(null, d);
            InControl.InputDevice now = InputManager.ActiveDevice;
            if (now != last)
            {
                Plugin.Log.LogInfo("[input] active device: " + (now != null ? now.Name : "null") + (before != now ? " (pinned; game picked " + (before != null ? before.Name : "null") + ")" : ""));
                last = now;
            }
        }
    }

    // Debug: what a punch did (ready? what was in front? how far?).
    [HarmonyLib.HarmonyPatch(typeof(TakedownWeapon), "Attack")]
    static class LogPunchAttack
    {
        // In VR the punch ray starts at the fist, which can sit inside the player's own colliders (MeleeHitbox,
        // CustomCollider, CustomColliderHead, CharacterController). The game's "is the target visible" check stops at
        // the first hit, so switch the player's colliders off for the duration of the attack.
        static readonly List<Collider> disabled = new List<Collider>();

        static void Prefix(TakedownWeapon __instance, Ray ray)
        {
            if (Plugin.VR && HandAim.MotionAttackActive) __instance.lastAttackTime = -1999f;
            disabled.Clear();
            if (Plugin.VR && Plugin.HandAim.Value && PlayerController.Instance != null)
                foreach (Collider col in PlayerController.Instance.GetComponentsInChildren<Collider>(false))
                    if (col.enabled) { col.enabled = false; disabled.Add(col); }
            if (!Plugin.LogControllerButtons.Value) return;
            RaycastHit hit;
            string first = Physics.Raycast(ray, out hit, 5f) ? hit.collider.name + " @" + hit.distance.ToString("F2") + "m" : "nothing within 5 m";
            Plugin.Log.LogInfo("[punch] game attack: ready=" + __instance.isWeaponReady() + " range=" + __instance.GetAttackPlaneRange().ToString("F2")
                + " inFront=" + __instance.objectInFront + " canAttack=" + (PlayerActions.CURRENT != null && PlayerActions.CURRENT.CanAttack())
                + " ray from " + ray.origin.ToString("F2") + " first hit " + first);
        }
        static void Finalizer(TakedownWeapon __instance)
        {
            for (int i = 0; i < disabled.Count; i++) if (disabled[i] != null) disabled[i].enabled = true;
            if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[punch] result inFront=" + __instance.objectInFront + (disabled.Count > 0 ? " (ignored " + disabled.Count + " player colliders)" : ""));
            disabled.Clear();
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(SimpleMeleeWeapon), "Attack")]
    static class MeleeIgnorePlayerColliders
    {
        static readonly List<Collider> disabled = new List<Collider>();
        static void Prefix(SimpleMeleeWeapon __instance)
        {
            // A physical swing always connects (no game-time cooldown, which barely moves in slow motion).
            if (Plugin.VR && HandAim.MotionAttackActive) __instance.lastAttackTime = -1999f;
            disabled.Clear();
            if (Plugin.VR && Plugin.HandAim.Value && PlayerController.Instance != null)
                foreach (Collider col in PlayerController.Instance.GetComponentsInChildren<Collider>(false))
                    if (col.enabled) { col.enabled = false; disabled.Add(col); }
        }
        static void Finalizer(SimpleMeleeWeapon __instance)
        {
            for (int i = 0; i < disabled.Count; i++) if (disabled[i] != null) disabled[i].enabled = true;
            if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[melee] attack ready=" + __instance.isWeaponReady() + " ignored " + disabled.Count + " player colliders");
            disabled.Clear();
        }
    }

    // Debug: what the game did with attack/throw input.
    [HarmonyLib.HarmonyPatch(typeof(HandManager), "Throw")]
    static class LogThrow
    {
        static void Prefix(HandManager __instance)
        {
            if (Plugin.LogControllerButtons.Value)
                Plugin.Log.LogInfo("[throw] HandManager.Throw weapon=" + (__instance.weapon != null ? __instance.weapon.GetType().Name : "null"));
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Gun), "Attack")]
    static class EmptyGunThrow
    {
        static readonly System.Reflection.FieldInfo Ammo = HarmonyLib.AccessTools.Field(typeof(Gun), "ammoCount");
        // Pulling the trigger on an empty gun throws it at once. The game first wants the shot cooldown to pass
        // (game time: barely moves while you stand still in VR) and an extra "no ammo" click.
        static bool Prefix(Gun __instance)
        {
            object a = Ammo != null ? Ammo.GetValue(__instance) : null;
            int ammo = a is int ? (int)a : 1;
            if (Plugin.LogControllerButtons.Value)
                Plugin.Log.LogInfo("[attack] " + __instance.GetType().Name + " ammo=" + ammo + " ready=" + __instance.isWeaponReady() + " state=" + __instance.state);
            if (Plugin.VR && Plugin.EmptyGunTriggerThrow.Value && ammo <= 0 && __instance.state != WeaponState.Thrown
                && HandManager.THIS != null && HandManager.THIS.weapon == __instance
                && PlayerActions.CURRENT != null && PlayerActions.CURRENT.CanThrow())
            {
                if (Plugin.LogControllerButtons.Value) Plugin.Log.LogInfo("[throw] empty gun -> throw");
                HandManager.THIS.Throw();
                return false;
            }
            return true;
        }
    }
}