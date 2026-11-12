// Debug helpers for camera-effect interpolators (what does "PlayerHit" etc. actually drive?).
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Utilities.CameraEffects;

namespace SHMCDVR
{
    static class EffectsDebug
    {
        static readonly FieldInfo MaterialField = HarmonyLib.AccessTools.Field(typeof(CustomMaterialOnScreen), "material");

        internal static void DumpInterpolators()
        {
            var all = Resources.FindObjectsOfTypeAll<CameraEffectsInterpolator>();
            Plugin.Log.LogInfo("=== " + all.Length + " camera effect interpolators");
            foreach (var ip in all)
            {
                var sb = new StringBuilder();
                sb.Append("[").Append(ip.Id).Append("] dur=").Append(ip.Duration).Append(" on=").Append(ip.gameObject.name);
                foreach (PostprocessData e in ip.Elements)
                {
                    sb.Append("\n    ").Append(Describe(e.Postprocess)).Append(" :");
                    foreach (InterpolatorDataWrapper w in e.Values) sb.Append(" ").Append(ValueName(w));
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
        }

        internal static string Describe(Component c)
        {
            if (c == null) return "<null>";
            string s = c.GetType().Name + "@" + c.gameObject.name;
            var cm = c as CustomMaterialOnScreen;
            if (cm != null) s += " '" + cm.Name + "' shader=" + (cm.Shader != null ? cm.Shader.name : "?");
            var b = c as Behaviour;
            if (b != null) s += b.enabled ? " (on)" : " (off)";
            return s;
        }

        static string ValueName(InterpolatorDataWrapper w)
        {
            foreach (FieldInfo f in typeof(InterpolatorDataWrapper).GetFields())
            {
                var d = f.GetValue(w) as InterpolatorData;
                if (d != null && w.typeIndex >= 0 && !string.IsNullOrEmpty(d.ValueName)) return d.ValueName;
            }
            return "?";
        }

        static readonly string[] GlobalTextures = { "_GlobalUITexture", "_GlobalPrecomposedMenuTexture", "_GlobalTextTexture",
            "_GlobalMiniMenuTexture", "_GlobalMonitorTexture", "_GlobalCrosshairTexture", "_GlobalTextpunchTexture" };

        internal static void SaveGlobalTextures(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (string n in GlobalTextures)
            {
                Texture t = Shader.GetGlobalTexture(n);
                if (t == null) { Plugin.Log.LogInfo("global " + n + " = null"); continue; }
                Plugin.Log.LogInfo("global " + n + " = '" + t.name + "' " + t.width + "x" + t.height + " " + t.GetType().Name);
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(t.width, t.height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                // Write colour and alpha separately so either kind of mask is visible.
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), ImageConversion.EncodeToPNG(Opaque(tex, false)));
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + "_alpha.png"), ImageConversion.EncodeToPNG(Opaque(tex, true)));
                Object.Destroy(tex);
            }
        }

        static Texture2D Opaque(Texture2D src, bool alpha)
        {
            Color32[] px = src.GetPixels32();
            for (int i = 0; i < px.Length; i++)
                px[i] = alpha ? new Color32(px[i].a, px[i].a, px[i].a, 255) : new Color32(px[i].r, px[i].g, px[i].b, 255);
            var o = new Texture2D(src.width, src.height, TextureFormat.ARGB32, false);
            o.SetPixels32(px);
            o.Apply();
            return o;
        }

        internal static void Play(string id)
        {
            foreach (var ip in Object.FindObjectsOfType<CameraEffectsInterpolator>())
                if (ip.Id == id) { Plugin.Log.LogInfo("Playing effect '" + id + "'"); ip.Play(); return; }
            Plugin.Log.LogWarning("No interpolator '" + id + "'");
        }

        // Image effects on the VR camera, in render order.
        internal static void DumpImageEffects()
        {
            foreach (Camera c in Camera.allCameras)
            {
                var sb = new StringBuilder("Effects on " + c.name + ":");
                foreach (MonoBehaviour mb in c.GetComponents<MonoBehaviour>())
                {
                    sb.Append("\n    ").Append(Describe(mb));
                    var cm = mb as CustomMaterialOnScreen;
                    if (cm == null) continue;
                    if (cm.MaterialProperties != null)
                        foreach (var w in cm.MaterialProperties)
                        {
                            var p = w.Get();
                            object v = p.GetValue();
                            var tex = v as Texture;
                            sb.Append("\n        ").Append(p.Name).Append(" = ").Append(tex != null ? "tex '" + tex.name + "' " + tex.width + "x" + tex.height : (v == null ? "null" : v.ToString()));
                        }
                    var m = MaterialField.GetValue(cm) as Material;
                    if (m != null)
                        foreach (string tn in m.GetTexturePropertyNames())
                        {
                            Texture t = m.GetTexture(tn);
                            sb.Append("\n        [mat] ").Append(tn).Append(" = ").Append(t != null ? "'" + t.name + "' " + t.width + "x" + t.height : "none");
                        }
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
        }
    }
}
