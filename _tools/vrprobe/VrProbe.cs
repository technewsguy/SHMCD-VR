// Background OpenVR probe: reports which app the compositor shows, frame rate, and optionally takes a stereo screenshot.
// Usage: VrProbe.exe [screenshotBasePath]
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Valve.VR;

static class VrProbe
{
    static string Proc(uint pid)
    {
        if (pid == 0) return "0 (none)";
        try { return pid + " (" + Process.GetProcessById((int)pid).ProcessName + ")"; } catch { return pid + " (?)"; }
    }

    static int Main(string[] args)
    {
        var err = EVRInitError.None;
        OpenVR.Init(ref err, EVRApplicationType.VRApplication_Background);
        if (err != EVRInitError.None) { Console.WriteLine("Init failed: " + err); return 1; }
        try
        {
            var c = OpenVR.Compositor;
            Console.WriteLine("Scene focus process : " + Proc(c.GetCurrentSceneFocusProcess()));
            Console.WriteLine("Last frame renderer : " + Proc(c.GetLastFrameRenderer()));
            Console.WriteLine("Dashboard visible   : " + OpenVR.Overlay.IsDashboardVisible());
            var stats = new Compositor_CumulativeStats();
            uint sz = (uint)Marshal.SizeOf(typeof(Compositor_CumulativeStats));
            c.GetCumulativeStats(ref stats, sz);
            uint p1 = stats.m_nNumFramePresents, d1 = stats.m_nNumDroppedFrames;
            if (args.Length < 2) System.Threading.Thread.Sleep(2000);
            c.GetCumulativeStats(ref stats, sz);
            Console.WriteLine("Presents / dropped in last 2s : " + (stats.m_nNumFramePresents - p1) + " / " + (stats.m_nNumDroppedFrames - d1));
            if (args.Length > 0)
            {
                uint h = 0;
                var e = OpenVR.Screenshots.TakeStereoScreenshot(ref h, args[0] + "_preview", args[0] + "_vr");
                Console.WriteLine("Stereo screenshot   : " + e);
                System.Threading.Thread.Sleep(args.Length < 2 ? 3000 : 800);
            }
        }
        finally { OpenVR.Shutdown(); }
        return 0;
    }
}
