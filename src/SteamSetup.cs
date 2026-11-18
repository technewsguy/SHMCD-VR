// Keeps Steam's entry for the game set up (see installer\SteamVRTag.cs) and removes v0.1's SteamVR library entry.
using System;
using System.IO;
using BepInEx;
using Valve.VR;

namespace SHMCDVR
{
    static class SteamSetup
    {
        // At every start: Valve updates to the game's store info reset Steam's entry (VR tab, start SHMCD.exe directly).
        // If that happened, the installer copy in the game folder waits in the background and sets it up again once
        // Steam has closed (Steam only reads the file at startup).
        public static void CheckSteamEntry()
        {
            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    if (Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Wine") != null) return;   // Proton: not used
                    if (SteamVRTag.IsTagged() != false) return;
                    string helper = Path.Combine(Paths.GameRootPath, "SHMCD-VR Uninstall.exe");
                    if (!File.Exists(helper)) return;
                    SteamVRTag.StartWaiter(helper);
                    Plugin.Log.LogInfo("Steam reset its entry for the game (VR tab, direct start); it's set up again after Steam closes.");
                }
                catch (Exception e) { Plugin.Log.LogInfo("Steam entry check failed: " + e.Message); }
            });
            t.IsBackground = true;
            t.Start();
        }

        // v0.1 could add a separate SteamVR library entry (SHMCDVR-launcher\shmcdvr.vrmanifest). SteamVR rewrites its
        // list when it exits, so the installer can't remove it while SteamVR runs: the mod does it through OpenVR.
        public static void RemoveOldLibraryEntry()
        {
            try
            {
                string ld = Path.Combine(Paths.GameRootPath, "SHMCDVR-launcher");
                string manifest = Path.Combine(ld, "shmcdvr.vrmanifest");
                if (!File.Exists(manifest)) return;
                var r = OpenVR.Applications != null ? OpenVR.Applications.RemoveApplicationManifest(manifest) : EVRApplicationError.InvalidParameter;
                Plugin.Log.LogInfo("Old SteamVR library entry: " + r);
                Directory.Delete(ld, true);
            }
            catch (Exception e) { Plugin.Log.LogInfo("Old SteamVR entry cleanup failed: " + e.Message); }
        }
    }
}
