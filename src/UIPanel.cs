// The game's 2D UI lives in global textures that full-screen effects (BlitSHGUI, terminal bloom) composite in
// screen space. Per eye that lands at diverging positions -> double vision. In VR we blank those globals while the
// game camera renders and show the real textures on floating panels drawn by our own overlay camera.
//   _GlobalMiniMenuTexture  'SHGUI MENU TEXTURE' (title/menus, 4:3, has alpha)
//   _GlobalUITexture        'ingame UI texture'  (HUD, hearts, 16:9; cleared to transparent in VR, see ClearUITransparent)
//   _GlobalPrecomposedMenuTexture  screen-sized composite of the above; hidden only.
using System.Collections;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SHMCDVR
{
    public class UIPanel : MonoBehaviour
    {
        const int Layer = 3; // not rendered by the game's StandardCamera (mask 0xF9EFFF11)
        static readonly int UIId = Shader.PropertyToID("_GlobalUITexture"), MenuId = Shader.PropertyToID("_GlobalMiniMenuTexture"),
            PrecomposedId = Shader.PropertyToID("_GlobalPrecomposedMenuTexture");
        static int[] HiddenIds;
        static Texture[] blanks;   // what each texture looks like when empty (BlitSHGUI treats alpha as a mask)
        static Texture[] saved;
        static bool blanked;

        static Texture2D Solid(Color32 c)
        {
            var t = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        Camera overlay;
        Panel menuPanel, hudPanel;
        Vector3 smoothedForward;
        bool snapped;

        class Panel
        {
            public Transform t;
            public Material mat;
            public Texture tex;
        }

        Panel MakePanel(string name, int queue)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.layer = Layer;
            DontDestroyOnLoad(q);
            var p = new Panel { t = q.transform, mat = new Material(Shader.Find("Sprites/Default")) };
            p.mat.renderQueue = queue;
            q.GetComponent<Renderer>().sharedMaterial = p.mat;
            return p;
        }

        void Start()
        {
            if (!Plugin.VR || !Plugin.UIPanelEnabled.Value) { enabled = false; return; }

            var ids = new List<int> { UIId, MenuId };
            var bl = new List<Texture> { Solid(new Color32(0, 0, 0, 255)), Solid(new Color32(0, 0, 0, 0)) };
            if (Plugin.UIHidePrecomposed.Value) { ids.Add(PrecomposedId); bl.Add(Solid(new Color32(128, 128, 128, 128))); }
            HiddenIds = ids.ToArray(); blanks = bl.ToArray(); saved = new Texture[HiddenIds.Length];

            Camera.onPreRender += OnAnyPreRender;
            StartCoroutine(RestoreAtEndOfFrame());
        }

        // Blank the UI globals only while the game's own camera (and its image effects) render.
        static void OnAnyPreRender(Camera c)
        {
            if (blanked || CameraManager.Instance == null || c != CameraManager.Instance.CurrentCamera) return;
            for (int i = 0; i < HiddenIds.Length; i++)
            {
                saved[i] = Shader.GetGlobalTexture(HiddenIds[i]);
                Shader.SetGlobalTexture(HiddenIds[i], blanks[i]);
            }
            if (Plugin.WorldCrosshair.Value)
            {
                // BlitSHGUI paints the crosshair at each eye image's centre, which is not the gaze direction on
                // headsets with asymmetric/canted eye frustums (two crosshairs, neither on target). Power (w) = 0 hides it.
                savedCrosshair = Shader.GetGlobalVector(CrosshairVecId);
                Shader.SetGlobalVector(CrosshairVecId, new Vector4(savedCrosshair.x, savedCrosshair.y, savedCrosshair.z, 0f));
                crosshairSaved = true;
            }
            blanked = true;
        }

        static void Restore()
        {
            if (!blanked) return;
            for (int i = 0; i < HiddenIds.Length; i++)
                if (saved[i] != null) Shader.SetGlobalTexture(HiddenIds[i], saved[i]);
            if (crosshairSaved) { Shader.SetGlobalVector(CrosshairVecId, savedCrosshair); crosshairSaved = false; }
            blanked = false;
        }

        static readonly int CrosshairVecId = Shader.PropertyToID("_GlobalCrosshairVector"),
            CrosshairTexId = Shader.PropertyToID("_GlobalCrosshairTexture"), CrosshairRotId = Shader.PropertyToID("_GlobalCrosshairRotationVector");
        static Vector4 savedCrosshair;
        static bool crosshairSaved;
        Panel crosshair;
        float crosshairDist = 3f, nextCrossLog;

        // One crosshair in the world along the head's forward ray (the ray the game aims and throws along),
        // at the distance of whatever you look at so both eyes fuse it.
        void UpdateCrosshair(Camera main)
        {
            if (crosshair == null || crosshair.t == null) crosshair = MakePanel("SHMCDVR Crosshair", 4003);
            Vector4 v = Shader.GetGlobalVector(CrosshairVecId);   // real value (restored at end of last frame)
            Texture tex = Shader.GetGlobalTexture(CrosshairTexId);
            bool on = Plugin.WorldCrosshair.Value && v.w > 0f && v.x > 0f && tex != null;
            if (Plugin.LogEffects.Value && Time.unscaledTime > nextCrossLog)
            {
                nextCrossLog = Time.unscaledTime + 3f;
                Plugin.Log.LogInfo(string.Format("[crosshair] on={0} vec={1} tex={2} dist={3:0.00} pos={4}", on, v, tex != null ? tex.name + " " + tex.width : "null", crosshairDist, crosshair.t.position));
            }
            crosshair.t.gameObject.SetActive(on);
            if (crosshairBack != null && crosshairBack.t != null) crosshairBack.t.gameObject.SetActive(on);
            if (!on) return;
            if (tex != crosshair.tex) { crosshair.mat.mainTexture = tex; crosshair.tex = tex; }

            Transform cam = main.transform;
            // Aim ray = what the game aims along (head, or the right hand with HandAim).
            Ray aim;
            if (!HandAim.TryGetRay(out aim)) aim = new Ray(cam.position, cam.forward);
            Ray ray = new Ray(aim.origin + aim.direction * 0.25f, aim.direction);
            RaycastHit hit;
            float target = Physics.Raycast(ray, out hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? Mathf.Clamp(hit.distance + 0.25f - 0.02f, 0.5f, 15f) : 15f;
            crosshairDist = Mathf.Lerp(crosshairDist, target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));

            // Game: sizeX = 24.2 / internalScale; keep its punch animation as a relative scale.
            float rel = 1f;
            Crosshair2D c2 = Crosshair2D.Instance;
            Crosshair cur = c2 != null ? c2.GetCurrentCrosshair() : null;
            if (cur != null && cur.originalScaleFloat > 0f) rel = Mathf.Clamp(24.2f / v.x / cur.originalScaleFloat, 0.3f, 3f);
            Vector3 point = aim.origin + aim.direction * crosshairDist;
            Vector3 toPoint = point - cam.position;
            float viewDist = Mathf.Max(0.3f, toPoint.magnitude);   // keep a constant size as seen from the eyes
            float size = 2f * viewDist * Mathf.Tan(Plugin.CrosshairSize.Value * rel * 0.5f * Mathf.Deg2Rad);

            Vector4 rot = Shader.GetGlobalVector(CrosshairRotId);
            float roll = Mathf.Atan2(rot.x, rot.y) * Mathf.Rad2Deg;
            crosshair.t.SetParent(null, false);
            crosshair.t.position = point;
            crosshair.t.rotation = Quaternion.LookRotation(toPoint, cam.up) * Quaternion.Euler(0f, 0f, -roll);
            crosshair.t.localScale = new Vector3(size, size, 1f);

            // The dot is white and the world mostly white: a black copy drawn first, larger, gives it an outline.
            if (crosshairBack == null || crosshairBack.t == null)
            {
                crosshairBack = MakePanel("SHMCDVR Crosshair Outline", 4002);
                crosshairBack.mat.color = new Color(0f, 0f, 0f, 0.85f);
            }
            crosshairBack.t.gameObject.SetActive(true);
            if (tex != crosshairBack.tex) { crosshairBack.mat.mainTexture = tex; crosshairBack.tex = tex; }
            crosshairBack.t.SetParent(null, false);
            crosshairBack.t.position = crosshair.t.position + toPoint.normalized * 0.002f;
            crosshairBack.t.rotation = crosshair.t.rotation;
            crosshairBack.t.localScale = new Vector3(size * 1.6f, size * 1.6f, 1f);
        }
        Panel crosshairBack;

        IEnumerator RestoreAtEndOfFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true) { yield return wait; Restore(); }
        }

        void OnDestroy() { Camera.onPreRender -= OnAnyPreRender; Restore(); }

        // The game destroys our objects on some level loads (even DontDestroyOnLoad ones), so (re)create on demand.
        void EnsureObjects()
        {
            if (overlay == null)
            {
                var camGo = new GameObject("SHMCDVR UI Camera");
                DontDestroyOnLoad(camGo);
                overlay = camGo.AddComponent<Camera>();
                overlay.clearFlags = CameraClearFlags.Depth;
                overlay.cullingMask = 1 << Layer;
                overlay.nearClipPlane = 0.05f;
                overlay.farClipPlane = 20f;
                overlay.stereoTargetEye = StereoTargetEyeMask.Both;
                overlay.enabled = false;
                snapped = false;
                Plugin.Log.LogInfo("UI overlay camera created");
            }
            if (menuPanel == null || menuPanel.t == null) menuPanel = MakePanel("SHMCDVR Menu Panel", 4000);
            if (hudPanel == null || hudPanel.t == null) hudPanel = MakePanel("SHMCDVR HUD Panel", 4001);
        }

        void LateUpdate()
        {
            EnsureObjects();
            Camera main = CameraManager.Instance != null ? CameraManager.Instance.CurrentCamera : null;
            bool show = main != null && main.isActiveAndEnabled;
            overlay.enabled = show;
            if (!show)
            {
                menuPanel.t.gameObject.SetActive(false); hudPanel.t.gameObject.SetActive(false);
                if (crosshair != null && crosshair.t != null) crosshair.t.gameObject.SetActive(false);
                if (crosshairBack != null && crosshairBack.t != null) crosshairBack.t.gameObject.SetActive(false);
                return;
            }
            UpdateCrosshair(main);

            // Same tracked rig as the game camera: Unity applies the HMD pose to every stereo camera's local transform.
            Transform rig = main.transform.parent;
            if (overlay.transform.parent != rig) { overlay.transform.SetParent(rig, false); snapped = false; }
            overlay.depth = main.depth + 1f;

            // Globals are real here (restored at end of last frame).
            Bind(menuPanel, Shader.GetGlobalTexture(MenuId));
            Bind(hudPanel, Shader.GetGlobalTexture(UIId));
            // Sprites/Default without a texture renders solid white: keep a panel hidden until it has one.
            menuPanel.t.gameObject.SetActive(menuPanel.tex != null);
            hudPanel.t.gameObject.SetActive(hudPanel.tex != null);

            // Lazy head-follow in rig space: only re-centre when the head turns well away from the panel.
            Vector3 headPos = UnityEngine.XR.InputTracking.GetLocalPosition(UnityEngine.XR.XRNode.Head);
            Vector3 fwd = Vector3.ProjectOnPlane(UnityEngine.XR.InputTracking.GetLocalRotation(UnityEngine.XR.XRNode.Head) * Vector3.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            if (!snapped) { smoothedForward = fwd; snapped = true; }
            if (Vector3.Angle(smoothedForward, fwd) > Plugin.UIFollowAngle.Value)
                smoothedForward = Vector3.Slerp(smoothedForward, fwd, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime)).normalized;

            Place(menuPanel, rig, headPos, Plugin.UIDistance.Value);
            Place(hudPanel, rig, headPos, Plugin.UIDistance.Value - 0.01f);
        }

        static void Bind(Panel p, Texture tex)
        {
            if (tex == null || System.Array.IndexOf(blanks, tex) >= 0) return; // never bind our own blanks
            if (tex != p.tex) { p.mat.mainTexture = tex; p.tex = tex; }
        }

        void Place(Panel p, Transform rig, Vector3 headPos, float dist)
        {
            // Both textures span the same screen: fit to panel height so 4:3 menus and 16:9 HUD line up.
            float height = Plugin.UIWidth.Value * 9f / 16f;
            float aspect = p.tex != null && p.tex.height > 0 ? (float)p.tex.width / p.tex.height : 16f / 9f;
            p.t.SetParent(rig, false);
            p.t.localPosition = headPos + smoothedForward * dist;
            p.t.localRotation = Quaternion.LookRotation(smoothedForward, Vector3.up);
            // The HUD texture is drawn upside down (CameralessRenderer: GPU projection into a render texture) and the
            // game's composite flips it back; Sprites/Default ignores texture tiling, so flip the quad (Cull Off).
            bool flip = p == hudPanel && Plugin.FlipHUD.Value;
            p.t.localScale = new Vector3(height * aspect, flip ? -height : height, 1f);
        }
    }

    // The HUD texture is cleared to opaque black every frame; in VR clear it to transparent so a plain
    // alpha-blended panel shows only the UI (no additive shader ships with the game).
    [HarmonyPatch(typeof(CameralessRenderer), "renderAllRegisteredRenderers")]
    static class ClearUITransparent
    {
        static Color ClearColor() { return Plugin.VR && Plugin.UIPanelEnabled.Value ? Color.clear : Color.black; }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var black = AccessTools.PropertyGetter(typeof(Color), "black");
            var repl = AccessTools.Method(typeof(ClearUITransparent), "ClearColor");
            int n = 0;
            foreach (var ins in instructions)
            {
                if (ins.Calls(black)) { n++; yield return new CodeInstruction(OpCodes.Call, repl); }
                else yield return ins;
            }
            if (n != 1) Plugin.Log.LogWarning("ClearUITransparent: expected 1 Color.black, found " + n);
        }
    }
}
