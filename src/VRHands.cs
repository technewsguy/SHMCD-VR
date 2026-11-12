// First-person arms / weapons in VR.
// The game draws them with "SELF-CONTAINED FOV-INDEPENDENT ALWAYOS-ON-TOP BLACK-CRYSTAL": a shader with its own
// fixed projection that ignores the per-eye stereo view, so both eyes get the same flat image (double vision).
// We swap those materials to the normal "XULM FULLFORWARD/Black Crystal" so they render as real 3D objects.
using System.Collections.Generic;
using UnityEngine;

namespace SHMCDVR
{
    [DefaultExecutionOrder(32000)]   // after the game's LateUpdates (DOTween, animation helpers)
    public class VRHands : MonoBehaviour
    {
        const string ViewmodelShader = "XULM FULLFORWARD/SELF-CONTAINED FOV-INDEPENDENT ALWAYOS-ON-TOP BLACK-CRYSTAL, version 200423";
        static Shader normalShader;
        readonly Dictionary<Material, Material> swapped = new Dictionary<Material, Material>();
        readonly HashSet<Material> ours = new HashSet<Material>();
        float nextScan, nextLog;

        void Start()
        {
            if (!Plugin.VR || !Plugin.Hands3D.Value) { enabled = false; return; }
            normalShader = Shader.Find("XULM FULLFORWARD/Black Crystal");
            Plugin.Log.LogInfo("VRHands: normal shader " + (normalShader != null ? "found" : "MISSING"));
            if (normalShader == null) { enabled = false; return; }
            Camera.onPreCull += OnAnyPreCull;
            StartCoroutine(RestoreAtEndOfFrame());
        }

        void OnDestroy() { Camera.onPreCull -= OnAnyPreCull; Restore(); }

        // ---- Put the arms rig (".../Hands/Standard") on the right controller for rendering ----
        // In the rest pose the gun barrel (Muzzle) points along Standard's forward, so Standard takes the controller's
        // aim rotation and is placed so its grip point sits in the hand. Applied right before the game camera culls
        // (after all animation) and restored at the end of the frame, so game logic and tweens see the original pose.
        static Transform moved;
        static Vector3 savedLocalPos;
        static Quaternion savedLocalRot;
        static Vector3 savedLocalScale;

        static Transform cachedRoot;
        static HandManager cachedFor;

        // ".../Hands/Standard": the weapon's parent, or found by name while no weapon is assigned yet (level start).
        static Transform ArmsRoot()
        {
            HandManager h = HandManager.THIS;
            if (h == null) return null;
            if (h.weapon != null && h.weapon.transform.parent != null) return h.weapon.transform.parent;
            if (cachedFor != h || cachedRoot == null)
            {
                cachedFor = h; cachedRoot = null;
                foreach (Transform t in h.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Standard" && t.parent != null && t.parent.name == "Hands") { cachedRoot = t; break; }
            }
            return cachedRoot;
        }

        static void OnAnyPreCull(Camera c)
        {
            if (CameraManager.Instance == null || c != CameraManager.Instance.CurrentCamera) return;
            Place();
        }

        // Runs at the end of LateUpdate (skinned meshes are skinned right after it, so bone edits must happen here)
        // and again before the game camera culls (latest controller pose for the rigid weapon models).
        static void Place()
        {
            if (!Plugin.HandModelOnController.Value || CameraManager.Instance == null) return;
            Transform root = ArmsRoot();
            if (root == null) return;
            if (moved != root)
            {
                Restore();
                moved = root; savedLocalPos = root.localPosition; savedLocalRot = root.localRotation; savedLocalScale = root.localScale;
            }
            Vector3 p; Quaternion q;
            if (!HandAim.TryGetPoseAny(out p, out q))
            {
                root.localScale = Vector3.zero;   // controller not tracked: hide rather than float in front of the face
                return;
            }
            float s = Plugin.HandModelScale.Value;
            root.localScale = savedLocalScale * s;
            Vector3 offset = q * Plugin.HandModelOffset.Value;

            // Each weapon's model is built differently (see NOTES "Arms / weapon"):
            Transform grip = FindGripAnchor(root);
            if (grip != null)
            {
                // One-handed gun with its own gun mesh (pistol: baked hand+gun, child "glock" pivot = grip, +Z = barrel).
                // Move the rig so that pivot sits in the hand, barrel along the aim. Index tip pose is at the front of
                // the controller, the grip ~8 cm behind and a bit below it.
                Quaternion relRot = Quaternion.Inverse(root.rotation) * grip.rotation;
                Vector3 relPos = Quaternion.Inverse(root.rotation) * (grip.position - root.position);
                Quaternion newRot = q * Quaternion.Inverse(relRot);
                root.rotation = newRot;
                root.position = p + q * GunGripFromTip * s + offset - newRot * relPos;
            }
            else if (FistsActive(root))
            {
                // Bare hands: full-body arm rig whose origin is ~1.3 m below the hands (hips). Put the rest-pose right
                // fist at the controller, then pin each hand to its own controller.
                root.rotation = q;
                root.position = p + offset - q * (FistRestLocal * s);
                PinFists(root, p + offset, q, s);
            }
            else
            {
                // Two-handed / other baked weapons: rig origin is the gun, arms reach back to the body centre.
                root.rotation = q;
                root.position = p + offset - q * (GripLocal * s);
                FreezeDrawAnimation();
            }
        }

        // ---- Bare hands: each hand on its controller, arms hidden ----
        // The rig's animations swing the hands around the hip pivot (throw = "helicopter"). Keep only a little of the
        // animated offset (so a punch still shows) and collapse everything between the spine and the wrists.
        const float MaxAnimOffset = 0.25f;
        static Transform rEnd, lEnd, skelRoot;
        // Fist centre / fist line as fixed points on each wrist, from the bind pose. The game's Pistol_Aux helper bones
        // are animated per weapon (for the knife they sit ~0.33 m from the hand), so they cannot be used as anchors.
        static Vector3 rAuxL, rEndL, lAuxL, lEndL;
        static bool virtOk;

        static Vector3 BindPos(Matrix4x4[] bind, int i) { return bind[i].inverse.MultiplyPoint3x4(Vector3.zero); }

        static void SetupVirtual(SkinnedMeshRenderer smr)
        {
            virtOk = false;
            Transform[] bones = smr.bones;
            Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            if (bind == null) return;
            int rw = System.Array.IndexOf(bones, rWrist), ra = System.Array.IndexOf(bones, rFist), re = System.Array.IndexOf(bones, rEnd);
            int lw = System.Array.IndexOf(bones, lWrist), la = System.Array.IndexOf(bones, lFist), le = System.Array.IndexOf(bones, lEnd);
            if (rw < 0 || ra < 0 || re < 0 || lw < 0 || la < 0 || le < 0)
            {
                // Helpers are not skinned: use their rest offsets on the wrist (from the [hands] finger dump: Pistol_Aux at
                // (0.096, 0.023, -0.064), PistolEnd_Aux 0.138 further along its X).
                // Prefer offsets measured on this run with bare fists (where the helpers sit at rest); else the right
                // hand's dump values, mirrored (negated) for the left hand's mirrored skeleton.
                if (measured) { rAuxL = mRAux; rEndL = mREnd; lAuxL = mLAux; lEndL = mLEnd; }
                else
                {
                    rAuxL = new Vector3(0.096f, 0.023f, -0.064f); rEndL = new Vector3(0.234f, 0.023f, -0.065f);
                    lAuxL = -rAuxL; lEndL = -rEndL;
                }
                virtOk = true;
                Plugin.Log.LogInfo("VRHands: fist anchors from rest offsets (helpers not skinned)");
                return;
            }
            rAuxL = bind[rw].MultiplyPoint3x4(BindPos(bind, ra)); rEndL = bind[rw].MultiplyPoint3x4(BindPos(bind, re));
            lAuxL = bind[lw].MultiplyPoint3x4(BindPos(bind, la)); lEndL = bind[lw].MultiplyPoint3x4(BindPos(bind, le));
            virtOk = true;
            Plugin.Log.LogInfo("VRHands: fist anchors from bind pose r=" + rAuxL.ToString("F3") + " l=" + lAuxL.ToString("F3"));
        }

        // Bare fists: the helpers are at rest, so measure them once and reuse for every weapon (both hands).
        static bool measured;
        static Vector3 mRAux, mREnd, mLAux, mLEnd;

        static void MeasureRestAnchors()
        {
            HandManager h = HandManager.THIS;
            if (measured || h == null || !(h.weapon is TakedownWeapon) || rWrist == null || lWrist == null) return;
            mRAux = rWrist.InverseTransformPoint(rFist.position); mREnd = rWrist.InverseTransformPoint(rEnd.position);
            mLAux = lWrist.InverseTransformPoint(lFist.position); mLEnd = lWrist.InverseTransformPoint(lEnd.position);
            measured = true;
            if (virtOk) { rAuxL = mRAux; rEndL = mREnd; lAuxL = mLAux; lEndL = mLEnd; }
            Plugin.Log.LogInfo("VRHands: measured fist anchors r=" + mRAux.ToString("F3") + "/" + mREnd.ToString("F3") + " l=" + mLAux.ToString("F3") + "/" + mLEnd.ToString("F3"));
        }

        static Vector3 RAux() { return virtOk ? rWrist.TransformPoint(rAuxL) : rFist.position; }
        static Vector3 REnd() { return virtOk ? rWrist.TransformPoint(rEndL) : rEnd.position; }
        static Vector3 LAux() { return virtOk ? lWrist.TransformPoint(lAuxL) : lFist.position; }
        static Vector3 LEnd() { return virtOk ? lWrist.TransformPoint(lEndL) : lEnd.position; }

        // ---- Fist on grip: bend each finger joint about its local Z (positive = into the palm) ----
        // Finger_01 = thumb, 02..05 = index..little; joints _01 knuckle, _02 middle, _03 tip (from the [hands] dump).
        struct FingerJoint { public Transform t; public float bend, shrink; public Quaternion saved, open; public bool hasOpen; public Vector3 savedScale; }
        static FingerJoint[] rFingers = new FingerJoint[0], lFingers = new FingerJoint[0];
        static float rCurl, lCurl;
        static bool fingersSaved;

        static Quaternion BindRot(Matrix4x4[] bind, int i)
        {
            Matrix4x4 m = bind[i].inverse;   // bone in mesh space at bind time
            return Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1));
        }

        static FingerJoint[] Fingers(Transform skel, string side, SkinnedMeshRenderer smr)
        {
            Transform[] bones = smr.bones;
            Matrix4x4[] bind = smr.sharedMesh != null ? smr.sharedMesh.bindposes : new Matrix4x4[0];
            var list = new List<FingerJoint>();
            for (int f = 1; f <= 5; f++)
                for (int j = 1; j <= 3; j++)
                {
                    Transform t = FindBone(skel, "jnt_" + side + "_Finger_0" + f + "_0" + j);
                    if (t == null) continue;
                    // Extra bend on top of the neutral pose (which is already ~6-21 deg curled).
                    // Thumb: only a light fold and a little shorter, so it rests against the fingers instead of wrapping
                    // around the fingertips.
                    float bend = f == 1 ? 0f
                                        : (j == 1 ? 70f : j == 2 ? 80f : 60f);
                    float shrink = f == 1 && j == 2 ? Plugin.FistThumbLength.Value : 1f;   // scales the tip segment with it
                    // Open (relaxed) pose = the bind pose, so the game's melee/grab animation does not shape the fingers.
                    var fj = new FingerJoint { t = t, bend = bend, shrink = shrink };
                    int bi = System.Array.IndexOf(bones, t), pi = System.Array.IndexOf(bones, t.parent);
                    if (bi >= 0 && pi >= 0 && bi < bind.Length && pi < bind.Length)
                    {
                        fj.open = Quaternion.Inverse(BindRot(bind, pi)) * BindRot(bind, bi);
                        fj.hasOpen = true;
                    }
                    list.Add(fj);
                }
            return list.ToArray();
        }

        static void Curl(FingerJoint[] fingers, float k)
        {
            for (int i = 0; i < fingers.Length; i++)
            {
                Vector3 e = (fingers[i].hasOpen ? fingers[i].open : fingers[i].t.localRotation).eulerAngles;
                float z = e.z + fingers[i].bend * k;
                fingers[i].t.localRotation = Quaternion.Euler(e.x, e.y, z);
                if (fingers[i].shrink < 1f) fingers[i].t.localScale = fingers[i].savedScale * Mathf.Lerp(1f, fingers[i].shrink, k);
            }
        }

        static void SaveFingers(FingerJoint[] fingers)
        {
            for (int i = 0; i < fingers.Length; i++) if (fingers[i].t != null) { fingers[i].saved = fingers[i].t.localRotation; fingers[i].savedScale = fingers[i].t.localScale; }
        }

        // ---- Melee weapons come out of the bottom of the fist; flip them 180 deg about the hand's side axis at the grip ----
        static readonly List<Transform> flipped = new List<Transform>();
        static readonly List<Vector3> flipPos = new List<Vector3>();
        static readonly List<Quaternion> flipRot = new List<Quaternion>();

        // Melee weapons: put the weapon through the closed right fist. Direction = little-finger knuckle -> index knuckle
        // (the line a held stick runs through a fist, out of the thumb side); grip point = 20 % up from the weapon's
        // bottom end, placed in the gap between the knuckles and the curled middle fingertip.
        static Transform k02, k05, m01, m03, meleeSkel;
        static bool meleeListed;   // current melee weapon is on MeleeFlipNames (set while placing it; used next frame for the hand)
        static readonly System.Reflection.FieldInfo MeleePickup = HarmonyLib.AccessTools.Field(typeof(SimpleMeleeWeapon), "weaponPickup");

        static bool NameListed(string name, string list)
        {
            if (string.IsNullOrEmpty(list) || string.IsNullOrEmpty(name)) return false;
            foreach (string s in list.Split(','))
            {
                string t = s.Trim();
                if (t.Length > 0 && name.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        internal static bool IsKnife(Weapon w)
        {
            if (!(w is SimpleMeleeWeapon) || MeleePickup == null) return false;
            GunPickup p = MeleePickup.GetValue(w) as GunPickup;
            return p != null && p.isKnife;
        }
        static float nextMeleeLog;

        static void PlaceMeleeWeapon(Transform skel)
        {
            HandManager h = HandManager.THIS;
            if (!Plugin.MeleeFlip.Value || h == null || !(h.weapon is SimpleMeleeWeapon) || rWrist == null || skel == null) return;
            // Every melee weapon brings its own copy of the hand skeleton: look the bones up again when it changes.
            if (k02 == null || meleeSkel != skel)
            {
                meleeSkel = skel;
                k02 = FindBone(skel, "jnt_R_Finger_02_01"); k05 = FindBone(skel, "jnt_R_Finger_05_01");
                m01 = FindBone(skel, "jnt_R_Finger_03_01"); m03 = FindBone(skel, "jnt_R_Finger_03_03");
            }
            if (k02 == null || k05 == null || m01 == null || m03 == null) return;
            // Direction from the controller (the knuckle line of this model points the other way): the controller's
            // pointing direction tilted up by MeleeAngle, like a bat held ready in a gun-style grip.
            Vector3 cp; Quaternion cq;
            if (!HandAim.TryGetPoseAny(out cp, out cq)) return;
            Vector3 up = cq * (Quaternion.Euler(-Plugin.MeleeAngle.Value, 0f, 0f) * Vector3.forward);
            Vector3 hole = (m01.position + m03.position) * 0.5f;
            bool knife = IsKnife(h.weapon);
            foreach (MeshRenderer r in rWrist.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled) continue;
                bool nested = false;
                for (Transform t = r.transform.parent; t != null && t != rWrist; t = t.parent)
                    if (t.GetComponent<MeshRenderer>() != null || flipped.Contains(t)) { nested = true; break; }
                if (nested || flipped.Contains(r.transform)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Transform w = r.transform;
                flipped.Add(w); flipPos.Add(w.localPosition); flipRot.Add(w.localRotation);
                Bounds b = mf.sharedMesh.bounds;
                Vector3 s = b.size;
                Vector3 la = s.z >= s.x && s.z >= s.y ? Vector3.forward : (s.y >= s.x ? Vector3.up : Vector3.right);
                float half = Vector3.Dot(s, la) * 0.5f;
                // Point the weapon's long axis along "up" (whichever end currently points closer to it stays the top).
                Vector3 axis = w.TransformDirection(la).normalized;
                float sign = Vector3.Dot(axis, up) >= 0f ? 1f : -1f;
                if (knife && Plugin.KnifeFlip.Value) sign = -sign;   // the other end of the knife goes up
                string meshName = mf.sharedMesh.name;
                meleeListed = !knife && NameListed(meshName, Plugin.MeleeFlipNames.Value);
                // Listed weapons get the knife hand roll; with that roll the end chosen above is already the right one.
                w.rotation = Quaternion.FromToRotation(axis * sign, up) * w.rotation;
                Vector3 top = w.TransformPoint(b.center + la * half * sign), bottom = w.TransformPoint(b.center - la * half * sign);
                Vector3 grip = Vector3.Lerp(bottom, top, Plugin.MeleeGripAt.Value);
                w.position += hole - grip;
                if (Plugin.LogEffects.Value && Time.unscaledTime > nextMeleeLog)
                {
                    nextMeleeLog = Time.unscaledTime + 2f;
                    Transform cam = CameraManager.Instance != null ? CameraManager.Instance.StandardCameraTransform : null;
                    System.Func<Vector3, string> cl = v => cam != null ? cam.InverseTransformPoint(v).ToString("F2") : v.ToString("F2");
                    Plugin.Log.LogInfo("[meleepos] " + r.name + " mesh='" + mf.sharedMesh.name + "' knife=" + knife + " parent=" + w.parent.name + " hole=" + cl(hole) + " top=" + cl(w.TransformPoint(b.center + la * half * sign))
                        + " bottom=" + cl(w.TransformPoint(b.center - la * half * sign)) + " len=" + (half * 2f).ToString("F2") + " rFist=" + cl(rFist.position)
                        + " ctrl=" + cl(rWrist.position));
                }
            }
        }

        static void RestoreFlip()
        {
            for (int i = 0; i < flipped.Count; i++)
                if (flipped[i] != null) { flipped[i].localPosition = flipPos[i]; flipped[i].localRotation = flipRot[i]; }
            flipped.Clear(); flipPos.Clear(); flipRot.Clear();
        }

        static void RestoreFingers()
        {
            if (!fingersSaved) return;
            fingersSaved = false;
            foreach (FingerJoint[] side in new[] { rFingers, lFingers })
                for (int i = 0; i < side.Length; i++) if (side[i].t != null) { side[i].t.localRotation = side[i].saved; side[i].t.localScale = side[i].savedScale; }
        }
        // Idle fist pose relative to the view (right hand; left is mirrored in x).
        static readonly Vector3 IdleFwd = new Vector3(-0.42f, -0.02f, 0.91f), IdleUp = new Vector3(-0.31f, 0.94f, -0.13f);

        static Quaternion FistAim(Transform wrist, Vector3 fist, Vector3 end, Quaternion ctrl, bool mirror)
        {
            Vector3 f = end - fist, u = fist - wrist.position;
            if (f.sqrMagnitude < 1e-8f || u.sqrMagnitude < 1e-8f) return Quaternion.identity;
            Quaternion cur = Quaternion.LookRotation(f, u);
            Vector3 tf = mirror ? new Vector3(-IdleFwd.x, IdleFwd.y, IdleFwd.z) : IdleFwd;
            Vector3 tu = mirror ? new Vector3(-IdleUp.x, IdleUp.y, IdleUp.z) : IdleUp;
            Quaternion want = Quaternion.LookRotation(ctrl * tf, ctrl * tu);
            return want * Quaternion.Inverse(cur);
        }
        static Transform spine, rWrist, lWrist, rFist, lFist, rClav, lClav;

        // Rotate the arm by r about its fist and put the fist at target, by moving the arm's top bone.
        static void MoveArm(Transform top, Vector3 f, Vector3 target, Quaternion r)
        {
            top.SetPositionAndRotation(target + r * (top.position - f), r * top.rotation);
        }
        static SkinnedMeshRenderer fistSkin;

        static SkinnedMeshRenderer ActiveSkinned(Transform root)
        {
            foreach (SkinnedMeshRenderer sm in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (sm.enabled) return sm;
            return null;
        }
        static bool fistsModified;
        static int pinnedFrame = -1;
        static Vector3 spineScale, rWristLocalPos, lWristLocalPos, rWristScale, lWristScale;
        static Quaternion rWristLocalRot, lWristLocalRot;

        static Transform FindBone(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static void PinFists(Transform root, Vector3 rightTarget, Quaternion q, float s)
        {
            // There are two copies of the arm skeleton under the rig (guns: Standard/bHands, fists:
            // Standard/Weapon/Container/bHands). Use the bones of the skinned mesh that is actually drawn.
            SkinnedMeshRenderer smr = ActiveSkinned(root);
            if (smr == null) return;
            if (fistSkin != smr)
            {
                fistSkin = smr;
                Transform skel = smr.rootBone != null ? smr.rootBone : smr.transform.parent;
                while (skel.parent != null && skel.parent != root && skel.name != "bHands") skel = skel.parent;
                spine = FindBone(skel, "jnt_Spine_01");
                rWrist = FindBone(skel, "jnt_R_Arm_Wrist"); lWrist = FindBone(skel, "jnt_L_Arm_Wrist");
                rFist = FindBone(skel, "jnt_R_Pistol_Aux"); lFist = FindBone(skel, "jnt_L_Pistol_Aux");
                rClav = FindBone(skel, "jnt_R_Arm_Clavicle"); lClav = FindBone(skel, "jnt_L_Arm_Clavicle");
                rEnd = FindBone(skel, "jnt_R_PistolEnd_Aux"); lEnd = FindBone(skel, "jnt_L_PistolEnd_Aux");
                rFingers = Fingers(skel, "R", smr); lFingers = Fingers(skel, "L", smr);
                skelRoot = skel;
                SetupVirtual(smr);
                Plugin.Log.LogInfo("VRHands: fists skeleton " + skel.name + " under " + (skel.parent != null ? skel.parent.name : "-")
                    + " spine=" + (spine != null) + " wrists=" + (rWrist != null) + "/" + (lWrist != null) + " fists=" + (rFist != null) + "/" + (lFist != null));
            }
            if (spine == null || rWrist == null || lWrist == null || rFist == null || lFist == null || rClav == null || lClav == null || rEnd == null || lEnd == null) return;
            if (fistsModified && pinnedFrame == Time.frameCount) return;   // second eye: already placed this frame
            MeasureRestAnchors();
            pinnedFrame = Time.frameCount;

            // Where each fist should go: its controller plus a clamped share of the animation's offset.
            Vector3 rOff = Vector3.ClampMagnitude(RAux() - root.TransformPoint(FistRestLocal), MaxAnimOffset);
            Vector3 lRest = root.TransformPoint(new Vector3(-FistRestLocal.x, FistRestLocal.y, FistRestLocal.z));
            Vector3 lOff = Vector3.ClampMagnitude(LAux() - lRest, MaxAnimOffset);
            Vector3 pl; Quaternion ql;
            bool left = HandAim.TryGetLeftPose(out pl, out ql);
            Vector3 lTarget = left ? pl + ql * Plugin.HandModelOffset.Value : lRest;


            if (!fistsModified)
            {
                fistsModified = true;
                spineScale = spine.localScale;
                rWristLocalPos = rClav.localPosition; rWristLocalRot = rClav.localRotation; rWristScale = rClav.localScale;
                lWristLocalPos = lClav.localPosition; lWristLocalRot = lClav.localRotation; lWristScale = lClav.localScale;
            }
            // Move each whole arm (from its clavicle) so the fist lands on target; the forearm stub stays attached.
            // Rotation comes from the controller, not the animation (the throw/punch animations twist the fist up to
            // 90+ deg and slow motion freezes them mid-way). Fist frame: forward = Pistol_Aux -> PistolEnd_Aux (the line
            // through the fist), up = wrist -> fist, made perpendicular. Target = the game's idle fist pose relative to
            // the view (from the [hands] dump), applied relative to each controller.
            // Holding a melee weapon, the rig's hand comes out upside down relative to the bare fist: roll it back.
            Quaternion rCtrl = q;
            if (HandManager.THIS != null && HandManager.THIS.weapon is SimpleMeleeWeapon)
                rCtrl = q * Quaternion.Euler(0f, 0f, IsKnife(HandManager.THIS.weapon) || meleeListed ? Plugin.KnifeHandRoll.Value : Plugin.MeleeHandRoll.Value);
            MoveArm(rClav, RAux(), rightTarget + rOff, FistAim(rWrist, RAux(), REnd(), rCtrl, false));
            MoveArm(lClav, LAux(), lTarget + lOff, FistAim(lWrist, LAux(), LEnd(), left ? ql : q, true));
            float dt = Time.unscaledDeltaTime;
            rCurl = Mathf.MoveTowards(rCurl, VRControllers.Right.Grip ? 1f : 0f, dt * 8f);
            lCurl = Mathf.MoveTowards(lCurl, VRControllers.Left.Grip ? 1f : 0f, dt * 8f);
            if (Plugin.FistOnGrip.Value)
            {
                SaveFingers(rFingers); SaveFingers(lFingers); fingersSaved = true;
                Curl(rFingers, rCurl); Curl(lFingers, lCurl);
            }
            PlaceMeleeWeapon(skelRoot);
        }

        static void RestoreFists()
        {
            if (!fistsModified) return;
            fistsModified = false;
            if (spine != null) spine.localScale = spineScale;
            if (rClav != null) { rClav.localScale = rWristScale; rClav.localPosition = rWristLocalPos; rClav.localRotation = rWristLocalRot; }
            if (lClav != null) { lClav.localScale = lWristScale; lClav.localPosition = lWristLocalPos; lClav.localRotation = lWristLocalRot; }
        }

        // ---- Weapon "draw" tween (starts rotated -130 deg and swings in): show the final pose in VR ----
        internal static readonly Dictionary<Weapon, KeyValuePair<Vector3, Quaternion>> DrawFinal = new Dictionary<Weapon, KeyValuePair<Vector3, Quaternion>>();
        static Weapon frozen;
        static Vector3 frozenPos;
        static Quaternion frozenRot;

        static void FreezeDrawAnimation()
        {
            HandManager h = HandManager.THIS;
            Weapon w = h != null ? h.weapon : null;
            KeyValuePair<Vector3, Quaternion> fin;
            if (w == null || w.state != WeaponState.Drawing || !DrawFinal.TryGetValue(w, out fin)) return;
            if (frozen != w) { RestoreDraw(); frozen = w; frozenPos = w.transform.localPosition; frozenRot = w.transform.localRotation; }
            w.transform.localPosition = fin.Key;
            w.transform.localRotation = fin.Value;
        }

        static void RestoreDraw()
        {
            if (frozen == null) return;
            frozen.transform.localPosition = frozenPos; frozen.transform.localRotation = frozenRot;
            frozen = null;
        }

        // Grip point in the arms rig's own space (rest pose: rig origin ~ the gun, hand below and behind it).
        static readonly Vector3 GripLocal = new Vector3(0.05f, -0.06f, -0.08f);
        // Right fist (jnt_R_Pistol_Aux) in the bare-hands rig's space at rest (from the [hands] dump).
        static readonly Vector3 FistRestLocal = new Vector3(0.10f, 1.32f, 0.33f);
        static readonly Vector3 GunGripFromTip = new Vector3(0f, -0.03f, -0.08f);

        static Transform gripCacheRoot, gripCache;
        static int gripCacheFrame = -1;

        static Transform FindGripAnchor(Transform root)
        {
            if (gripCacheFrame == Time.frameCount && gripCacheRoot == root) return gripCache;
            gripCacheFrame = Time.frameCount; gripCacheRoot = root; gripCache = null;
            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!r.enabled || r.name.IndexOf("baked", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Transform g = r.transform.Find("glock");
                if (g != null) { gripCache = g; break; }
            }
            return gripCache;
        }

        static bool FistsActive(Transform root)
        {
            HandManager h = HandManager.THIS;
            // Bare hands AND melee weapons (pipe, bat...) are held by the animated skinned arm rig; guns use baked meshes.
            if (h == null || h.weapon == null) return false;
            foreach (SkinnedMeshRenderer sm in root.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (sm.enabled) return true;
            return false;
        }

        static void Restore()
        {
            RestoreFlip();
            RestoreFingers();
            RestoreFists();
            RestoreDraw();
            if (moved == null) return;
            moved.localPosition = savedLocalPos; moved.localRotation = savedLocalRot; moved.localScale = savedLocalScale;
            moved = null;
        }

        System.Collections.IEnumerator RestoreAtEndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true) { yield return wait; Restore(); }
        }

        // ---- Pistol: the baked hand+gun mesh has a 50 cm forearm; keep only the gun and the hand around the grip ----
        readonly Dictionary<Mesh, Mesh> clipped = new Dictionary<Mesh, Mesh>();
        readonly HashSet<Mesh> clippedSet = new HashSet<Mesh>();

        void ClipBakedForearms(Transform root)
        {
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh src = mf.sharedMesh;
                if (src == null || clippedSet.Contains(src) || mf.name.IndexOf("baked", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Transform grip = mf.transform.Find("glock");
                if (grip == null || !src.isReadable) continue;
                Mesh m;
                if (!clipped.TryGetValue(src, out m))
                {
                    Matrix4x4 toGrip = grip.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    Vector3[] v = src.vertices;
                    bool[] keep = new bool[v.Length];
                    float back = Plugin.ForearmCut.Value;
                    for (int i = 0; i < v.Length; i++)
                    {
                        Vector3 g = toGrip.MultiplyPoint3x4(v[i]);
                        keep[i] = g.z >= -back && g.magnitude <= back + 0.22f;   // gun: z -0.09..0.23, hand wraps the grip
                    }
                    m = Object.Instantiate(src);
                    m.name = src.name + " (SHMCDVR no forearm)";
                    int kept = 0, total = 0;
                    for (int sub = 0; sub < src.subMeshCount; sub++)
                    {
                        int[] t = src.GetTriangles(sub);
                        List<int> o = new List<int>(t.Length);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            total++;
                            if (keep[t[i]] && keep[t[i + 1]] && keep[t[i + 2]]) { o.Add(t[i]); o.Add(t[i + 1]); o.Add(t[i + 2]); kept++; }
                        }
                        m.SetTriangles(o, sub, false);
                    }
                    if (Plugin.CapWrists.Value) CapStatic(src, m, keep);
                    m.bounds = src.bounds;   // keep the game's huge bounds (never culled)
                    clipped[src] = m; clippedSet.Add(m);
                    Plugin.Log.LogInfo("VRHands: clipped forearm from " + src.name + ": kept " + kept + "/" + total + " triangles");
                }
                mf.sharedMesh = m;
            }
        }

        // ---- Bare hands: keep only triangles fully skinned to the hand bones (wrist and everything under it) ----
        void ClipSkinnedArms(Transform root)
        {
            foreach (SkinnedMeshRenderer sm in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh src = sm.sharedMesh;
                if (src == null || clippedSet.Contains(src) || !src.isReadable) continue;
                Mesh m;
                if (!clipped.TryGetValue(src, out m))
                {
                    Transform[] bones = sm.bones;
                    bool[] hand = new bool[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                        for (Transform t = bones[i]; t != null; t = t.parent)
                            if (t.name == "jnt_R_Arm_Wrist" || t.name == "jnt_L_Arm_Wrist") { hand[i] = true; break; }
                    BoneWeight[] w = src.boneWeights;
                    bool[] keep = new bool[w.Length];
                    // Wrist joints in mesh (bind) space, to keep a short stub of forearm behind each hand.
                    Matrix4x4[] bind = src.bindposes;
                    var joints = new List<Vector3>();
                    for (int i = 0; i < bones.Length && i < bind.Length; i++)
                        if (bones[i].name == "jnt_R_Arm_Wrist" || bones[i].name == "jnt_L_Arm_Wrist") joints.Add(bind[i].inverse.MultiplyPoint3x4(Vector3.zero));
                    Vector3[] mv = src.vertices;
                    float stub = Plugin.WristStub.Value;
                    for (int i = 0; i < w.Length; i++)
                    {
                        BoneWeight b = w[i];
                        float hw = (Hand(hand, b.boneIndex0) ? b.weight0 : 0f) + (Hand(hand, b.boneIndex1) ? b.weight1 : 0f)
                                 + (Hand(hand, b.boneIndex2) ? b.weight2 : 0f) + (Hand(hand, b.boneIndex3) ? b.weight3 : 0f);
                        bool near = false;
                        foreach (Vector3 j in joints) if ((mv[i] - j).sqrMagnitude <= stub * stub) { near = true; break; }
                        keep[i] = hw >= 0.99f || (hw > 0.01f && near) || near;
                    }
                    m = Object.Instantiate(src);
                    m.name = src.name + " (SHMCDVR hands only)";
                    int kept = 0, total = 0;
                    for (int sub = 0; sub < src.subMeshCount; sub++)
                    {
                        int[] t = src.GetTriangles(sub);
                        List<int> o = new List<int>(t.Length);
                        for (int i = 0; i + 2 < t.Length; i += 3)
                        {
                            total++;
                            if (keep[t[i]] && keep[t[i + 1]] && keep[t[i + 2]]) { o.Add(t[i]); o.Add(t[i + 1]); o.Add(t[i + 2]); kept++; }
                        }
                        m.SetTriangles(o, sub, false);
                    }
                    if (Plugin.CapWrists.Value) CapCutEdges(src, m, keep, hand, bones);
                    m.bounds = src.bounds;
                    clipped[src] = m; clippedSet.Add(m);
                    Plugin.Log.LogInfo("VRHands: hands-only " + src.name + ": kept " + kept + "/" + total + " triangles (" + bones.Length + " bones)");
                }
                sm.sharedMesh = m;
            }
        }

        static bool Hand(bool[] hand, int i) { return i >= 0 && i < hand.Length && hand[i]; }

        // Close the holes the cut leaves at each wrist: every edge that was shared by a kept and a removed triangle is
        // fanned to one new centre vertex per hand (rigidly skinned to that wrist), with the winding flipped.
        static void CapCutEdges(Mesh src, Mesh m, bool[] keep, bool[] hand, Transform[] bones)
        {
            int[] all = src.triangles;
            // Faceted mesh: neighbouring triangles have their own copies of shared corners -> match corners by position.
            Vector3[] pos = src.vertices;
            int[] weld = new int[pos.Length];
            var byPos = new Dictionary<Vector3, int>();
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 q = new Vector3(Mathf.Round(pos[i].x * 10000f), Mathf.Round(pos[i].y * 10000f), Mathf.Round(pos[i].z * 10000f));
                int id; if (!byPos.TryGetValue(q, out id)) { id = i; byPos[q] = i; }
                weld[i] = id;
            }
            var count = new Dictionary<long, int>();
            for (int i = 0; i + 2 < all.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    long k = EdgeKey(weld[all[i + e]], weld[all[i + (e + 1) % 3]]);
                    int n; count.TryGetValue(k, out n); count[k] = n + 1;
                }
            // Which arm each bone belongs to (0 right, 1 left, -1 neither), from its clavicle ancestor.
            int[] armOf = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                armOf[i] = -1;
                for (Transform t = bones[i]; t != null; t = t.parent)
                {
                    if (t.name == "jnt_R_Arm_Clavicle") { armOf[i] = 0; break; }
                    if (t.name == "jnt_L_Arm_Clavicle") { armOf[i] = 1; break; }
                }
            }
            BoneWeight[] bw = src.boneWeights;
            // Cut edges, directed as they appear in the kept triangles, grouped by hand.
            var edges = new List<int>[2] { new List<int>(), new List<int>() };
            var kept = new Dictionary<long, int>();
            for (int i = 0; i + 2 < all.Length; i += 3)
            {
                if (!(keep[all[i]] && keep[all[i + 1]] && keep[all[i + 2]])) continue;
                for (int e = 0; e < 3; e++) { long k = EdgeKey(weld[all[i + e]], weld[all[i + (e + 1) % 3]]); int n; kept.TryGetValue(k, out n); kept[k] = n + 1; }
            }
            for (int i = 0; i + 2 < all.Length; i += 3)
            {
                if (!(keep[all[i]] && keep[all[i + 1]] && keep[all[i + 2]])) continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = all[i + e], b = all[i + (e + 1) % 3];
                    long k = EdgeKey(weld[a], weld[b]);
                    if (kept[k] != 1 || count[k] < 2) continue;   // only edges opened by our cut
                    int bi = bw[a].boneIndex0;
                    int side = bi >= 0 && bi < armOf.Length ? armOf[bi] : -1;
                    if (side < 0) continue;
                    edges[side].Add(a); edges[side].Add(b);
                }
            }
            var v = new List<Vector3>(m.vertices); var nrm = new List<Vector3>(m.normals); var tan = new List<Vector4>(m.tangents);
            var uv = new List<Vector2>(m.uv); var col = new List<Color>(m.colors); var w = new List<BoneWeight>(bw);
            var tris = new List<int>(m.GetTriangles(0));
            int caps = 0;
            for (int side = 0; side < 2; side++)
            {
                List<int> es = edges[side];
                if (es.Count < 6) continue;
                Vector3 c = Vector3.zero, cn = Vector3.zero; Vector2 cu = Vector2.zero;
                for (int i = 0; i < es.Count; i++) { c += v[es[i]]; if (nrm.Count > 0) cn += nrm[es[i]]; if (uv.Count > 0) cu += uv[es[i]]; }
                c /= es.Count; cu /= es.Count;
                int ci = v.Count;
                v.Add(c);
                if (nrm.Count > 0) nrm.Add(cn.sqrMagnitude > 1e-8f ? cn.normalized : Vector3.up);
                if (tan.Count > 0) tan.Add(tan[es[0]]);
                if (uv.Count > 0) uv.Add(cu);
                if (col.Count > 0) col.Add(col[es[0]]);
                // Skin the centre to the bone most of this hole's edge is on (the forearm, now that a stub is kept).
                var votes = new Dictionary<int, int>();
                foreach (int vi in es) { int n; votes.TryGetValue(bw[vi].boneIndex0, out n); votes[bw[vi].boneIndex0] = n + 1; }
                int best = -1, bestN = -1;
                foreach (var kv in votes) if (kv.Value > bestN) { best = kv.Key; bestN = kv.Value; }
                w.Add(new BoneWeight { boneIndex0 = best, weight0 = 1f });
                for (int i = 0; i + 1 < es.Count; i += 2) { tris.Add(es[i + 1]); tris.Add(es[i]); tris.Add(ci); }
                caps += es.Count / 2;
            }
            if (caps == 0) { Plugin.Log.LogWarning("VRHands: no cut edges found for wrist caps (" + edges[0].Count / 2 + "/" + edges[1].Count / 2 + ")"); return; }
            m.SetVertices(v);
            if (nrm.Count > 0) m.SetNormals(nrm);
            if (tan.Count > 0) m.SetTangents(tan);
            if (uv.Count > 0) m.SetUVs(0, uv);
            if (col.Count > 0) m.SetColors(col);
            m.boneWeights = w.ToArray();
            m.SetTriangles(tris, 0, false);
            Plugin.Log.LogInfo("VRHands: capped wrists with " + caps + " triangles (" + edges[0].Count / 2 + " right / " + edges[1].Count / 2 + " left edges)");
        }

        // Static (baked) mesh version of the wrist cap: one fan over all edges opened by the cut.
        static void CapStatic(Mesh src, Mesh m, bool[] keep)
        {
            int[] all = src.triangles;
            Vector3[] pos = src.vertices;
            int[] weld = new int[pos.Length];
            var byPos = new Dictionary<Vector3, int>();
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 q = new Vector3(Mathf.Round(pos[i].x * 10000f), Mathf.Round(pos[i].y * 10000f), Mathf.Round(pos[i].z * 10000f));
                int id; if (!byPos.TryGetValue(q, out id)) { id = i; byPos[q] = i; }
                weld[i] = id;
            }
            var count = new Dictionary<long, int>(); var kept = new Dictionary<long, int>();
            for (int i = 0; i + 2 < all.Length; i += 3)
            {
                bool k3 = keep[all[i]] && keep[all[i + 1]] && keep[all[i + 2]];
                for (int e = 0; e < 3; e++)
                {
                    long k = EdgeKey(weld[all[i + e]], weld[all[i + (e + 1) % 3]]);
                    int n; count.TryGetValue(k, out n); count[k] = n + 1;
                    if (k3) { kept.TryGetValue(k, out n); kept[k] = n + 1; }
                }
            }
            var es = new List<int>();
            for (int i = 0; i + 2 < all.Length; i += 3)
            {
                if (!(keep[all[i]] && keep[all[i + 1]] && keep[all[i + 2]])) continue;
                for (int e = 0; e < 3; e++)
                {
                    int a = all[i + e], b = all[i + (e + 1) % 3];
                    long k = EdgeKey(weld[a], weld[b]);
                    if (kept[k] == 1 && count[k] >= 2) { es.Add(a); es.Add(b); }
                }
            }
            if (es.Count < 6) { Plugin.Log.LogWarning("VRHands: no cut edges for the pistol wrist cap"); return; }
            var v = new List<Vector3>(m.vertices); var nrm = new List<Vector3>(m.normals); var tan = new List<Vector4>(m.tangents);
            var uv = new List<Vector2>(m.uv); var col = new List<Color>(m.colors);
            Vector3 cpos = Vector3.zero, cn = Vector3.zero; Vector2 cu = Vector2.zero;
            foreach (int i in es) { cpos += v[i]; if (nrm.Count > 0) cn += nrm[i]; if (uv.Count > 0) cu += uv[i]; }
            cpos /= es.Count; cu /= es.Count;
            int ci = v.Count;
            v.Add(cpos);
            if (nrm.Count > 0) nrm.Add(cn.sqrMagnitude > 1e-8f ? cn.normalized : Vector3.up);
            if (tan.Count > 0) tan.Add(tan[es[0]]);
            if (uv.Count > 0) uv.Add(cu);
            if (col.Count > 0) col.Add(col[es[0]]);
            var tris = new List<int>(m.GetTriangles(0));
            for (int i = 0; i + 1 < es.Count; i += 2) { tris.Add(es[i + 1]); tris.Add(es[i]); tris.Add(ci); }
            m.SetVertices(v);
            if (nrm.Count > 0) m.SetNormals(nrm);
            if (tan.Count > 0) m.SetTangents(tan);
            if (uv.Count > 0) m.SetUVs(0, uv);
            if (col.Count > 0) m.SetColors(col);
            m.SetTriangles(tris, 0, false);
            Plugin.Log.LogInfo("VRHands: capped pistol wrist with " + es.Count / 2 + " triangles");
        }

        static long EdgeKey(int a, int b) { return a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; }

        readonly HashSet<string> dumpedWeapons = new HashSet<string>();

        void LateUpdate()
        {
            Place();
            // Debug: log the arms/weapon setup once per weapon type (pistol, fists, rifle...).
            HandManager hm = HandManager.THIS;
            if (Plugin.LogEffects.Value && hm != null && hm.weapon != null && dumpedWeapons.Add(hm.weapon.GetType().Name + "/" + hm.weapon.name))
            {
                Plugin.Log.LogInfo("[hands] ===== weapon " + hm.weapon.GetType().Name + " '" + hm.weapon.name + "' =====");
                try { VRDriver.DumpHands(); } catch (System.Exception e) { Plugin.Log.LogWarning("DumpHands failed: " + e.Message); }
            }
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 0.25f;   // weapons are spawned/swapped at runtime
            Transform root = ArmsRoot();
            if (root == null) return;
            if (Plugin.HideArms.Value) { ClipBakedForearms(root); ClipSkinnedArms(root); }
            int n = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || ours.Contains(m) || m.shader == null || m.shader.name != ViewmodelShader) continue;
                    Material rep;
                    if (!swapped.TryGetValue(m, out rep))
                    {
                        rep = new Material(m);
                        rep.name = m.name + " (SHMCDVR 3D)";
                        rep.shader = normalShader;
                        rep.renderQueue = -1;
                        swapped[m] = rep;
                        ours.Add(rep);
                    }
                    mats[i] = rep; changed = true; n++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (n > 0 && Time.unscaledTime > nextLog)
            {
                nextLog = Time.unscaledTime + 2f;
                Plugin.Log.LogInfo("VRHands: swapped " + n + " viewmodel material slot(s) to 3D (" + swapped.Count + " materials)");
            }
        }
    }

    // Remember where each weapon's draw tween ends, so VRHands can show it there instead of swinging in.
    [HarmonyLib.HarmonyPatch]
    static class RecordDrawPose
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (System.Type t in new[] { typeof(Weapon), typeof(CombatShotGun), typeof(MachineGun), typeof(RailSpikeGun), typeof(ShotGun), typeof(SniperRifle), typeof(ThrowableItemWeapon) })
            {
                var m = HarmonyLib.AccessTools.DeclaredMethod(t, "LaunchDrawingAnimation", new[] { typeof(Vector3), typeof(Quaternion) });
                if (m != null) yield return m;
            }
        }

        static void Prefix(Weapon __instance, Vector3 finalPosition, Quaternion finalRotation)
        {
            VRHands.DrawFinal[__instance] = new KeyValuePair<Vector3, Quaternion>(finalPosition, finalRotation);
        }
    }
}