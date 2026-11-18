// SHMCD-VR installer and uninstaller: one self-contained exe. Embedded resources: "bepinex.zip" (BepInEx 5 x86, laid
// out like the game folder) and "payload/<path inside the game folder>" (the plugin, controller bindings, the x86
// openvr_api.dll Unity's VR support loads). See build.ps1.
//
// Double-click: finds SUPERHOT: MIND CONTROL DELETE in every Steam library (Windows Steam, or Linux Steam when run
// through Proton/Wine, where the Linux file system is drive Z:), installs, and puts "SHMCD-VR Uninstall.exe" (a copy
// of this exe) in the game folder. Installing also switches on Unity's built-in VR (boot.config vr-enabled=1 and
// "OpenVR" in globalgamemanagers, see GgmPatch.cs), so Steam's normal entry for the game plays in VR.
//
// Command line (Scoop etc.): /S = silent (log in %TEMP%\shmcdvr-setup.log), /GAMEDIR=<folder>, /UNINSTALL.
// /VRTAG-WAIT = background helper (started by the mod): sets up Steam's entry for the game after Steam closes.
// Exit code 0 = success.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

static partial class ShmcdVrSetup
{
    const string GameName = "SUPERHOT: MIND CONTROL DELETE";
    const string SteamAppId = "690040";
    const string GameExe = "SHMCD.exe";
    const string DefaultInstallDir = "SUPERHOTMCD";
    const string UninstallerName = "SHMCD-VR Uninstall.exe";
    const string DataDir = "SHMCD_Data";
    // The mod's own files (also v0.1's), removed on uninstall.
    static readonly string[] ModFiles = { @"BepInEx\plugins\SHMCDVR.dll", @"SHMCD_Data\Plugins\openvr_api.dll",
        @"SHMCD_Data\globalgamemanagers.orig", @"SHMCD_Data\boot.config.orig" };
    static readonly string[] ModDirs = { @"BepInEx\plugins\shmcdvr_input", @"BepInEx\plugins\shmcdvr_tools", @"BepInEx\shmcdvr_dump" };
    // BepInEx 5 in the game folder (removed with the mod unless other BepInEx mods use it).
    static readonly string[] LoaderFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };
    static readonly string[] LoaderDirs = { "BepInEx" };

    internal static Action<string> Log = s => { };
    static bool Silent;

    // ---------------------------------------------------------------- entry point

    [STAThread]
    static int Main(string[] args)
    {
        string gameDir = null;
        bool uninstall = Path.GetFileName(SelfPath).IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0;
        foreach (var a in args)
        {
            if (a.Equals("/S", StringComparison.OrdinalIgnoreCase)) Silent = true;
            else if (a.Equals("/UNINSTALL", StringComparison.OrdinalIgnoreCase)) uninstall = true;
            else if (a.Equals("/VRTAG-WAIT", StringComparison.OrdinalIgnoreCase)) return SteamVRTag.WaitAndTag();
            else if (a.StartsWith("/GAMEDIR=", StringComparison.OrdinalIgnoreCase)) gameDir = a.Substring(9).Trim('"');
        }
        if (uninstall && gameDir == null && File.Exists(Path.Combine(Path.GetDirectoryName(SelfPath), GameExe)))
            gameDir = Path.GetDirectoryName(SelfPath);

        if (Silent) return RunSilent(gameDir, uninstall);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm(gameDir, uninstall));
        return 0;
    }

    static int RunSilent(string gameDir, bool uninstall)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "shmcdvr-setup.log");
        var sb = new StringBuilder();
        Log = s => sb.AppendLine(s);
        int code = 0;
        try
        {
            if (gameDir == null)
            {
                var found = FindGames();
                if (found.Count == 0) throw new Exception("Couldn't find " + GameName + ". Pass /GAMEDIR=<game folder>.");
                gameDir = found[0];
            }
            if (uninstall) Uninstall(gameDir); else Install(gameDir);
        }
        catch (Exception e) { Log("ERROR: " + e.Message); code = 1; }
        try { File.WriteAllText(logPath, sb.ToString()); } catch { }
        Console.Write(sb.ToString());
        return code;
    }

    static string SelfPath { get { return Assembly.GetExecutingAssembly().Location; } }

    // ---------------------------------------------------------------- Wine / Proton

    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

    static bool? _wine;
    internal static bool IsWine
    {
        get
        {
            if (_wine == null)
            {
                try { var h = GetModuleHandle("ntdll.dll"); _wine = h != IntPtr.Zero && GetProcAddress(h, "wine_get_version") != IntPtr.Zero; }
                catch { _wine = false; }
            }
            return _wine.Value;
        }
    }

    // Linux: Steam runs the game through Proton only when forced to, and Wine needs to be told to load BepInEx's
    // winhttp.dll. The bash part starts SHMCD.exe instead of the SUPERHOTMCD.exe stub (see SteamVRTag.cs).
    internal const string LinuxLaunchOption = "WINEDLLOVERRIDES=\"winhttp=n,b\" bash -c 'exec \"${@/SUPERHOTMCD.exe/SHMCD.exe}\"' -- %command%";

    // "/home/me/.steam" -> "Z:\home\me\.steam" (Wine maps the Linux root to Z:). Windows paths pass through.
    static string FromUnix(string p)
    {
        if (string.IsNullOrEmpty(p) || !p.StartsWith("/")) return p;
        return "Z:" + p.Replace('/', '\\');
    }

    // ---------------------------------------------------------------- finding the game

    static List<string> SteamRoots()
    {
        var roots = new List<string>();
        foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam" })
            foreach (var name in new[] { "SteamPath", "InstallPath" })
            {
                try { var v = Registry.GetValue(key, name, null) as string; if (!string.IsNullOrEmpty(v)) roots.Add(v.Replace('/', '\\')); } catch { }
            }
        if (IsWine)
        {
            var homes = new List<string>();
            string home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home) && home.StartsWith("/")) homes.Add(FromUnix(home));
            try { if (Directory.Exists(@"Z:\home")) homes.AddRange(Directory.GetDirectories(@"Z:\home")); } catch { }
            foreach (var h in homes)
                foreach (var rel in new[] { @".local\share\Steam", @".steam\steam", @".steam\root",
                                            @".var\app\com.valvesoftware.Steam\.local\share\Steam", @".var\app\com.valvesoftware.Steam\.steam\steam" })
                    roots.Add(Path.Combine(h, rel));
        }
        var result = new List<string>();
        foreach (var r in roots)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(r, "steamapps"))) continue;
                string full = Path.GetFullPath(r).TrimEnd('\\');
                if (!result.Exists(x => x.Equals(full, StringComparison.OrdinalIgnoreCase))) result.Add(full);
            }
            catch { }
        }
        return result;
    }

    internal static List<string> FindGames()
    {
        var libs = new List<string>();
        foreach (var root in SteamRoots())
        {
            libs.Add(root);
            try
            {
                string vdf = Path.Combine(root, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(FromUnix(m.Groups[1].Value.Replace("\\\\", "\\")));
            }
            catch { }
        }
        var games = new List<string>();
        foreach (var lib in libs)
        {
            try
            {
                string installDir = DefaultInstallDir;
                string acf = Path.Combine(lib, @"steamapps\appmanifest_" + SteamAppId + ".acf");
                if (File.Exists(acf))
                {
                    var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success) installDir = m.Groups[1].Value;
                }
                string dir = Path.GetFullPath(Path.Combine(lib, @"steamapps\common\" + installDir));
                if (File.Exists(Path.Combine(dir, GameExe)) && !games.Exists(g => g.Equals(dir, StringComparison.OrdinalIgnoreCase)))
                    games.Add(dir);
            }
            catch { }
        }
        return games;
    }

    internal static bool IsInstalled(string gameDir)
    {
        return !string.IsNullOrEmpty(gameDir) && File.Exists(Path.Combine(gameDir, @"BepInEx\plugins\SHMCDVR.dll"));
    }

    static void CheckGameDir(string gameDir)
    {
        if (string.IsNullOrEmpty(gameDir) || !File.Exists(Path.Combine(gameDir, GameExe)))
            throw new Exception("That folder doesn't contain " + GameExe + ". Pick the game folder (steamapps\\common\\" + DefaultInstallDir + ").");
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)))
        {
            string path = null;
            try { path = p.MainModule.FileName; } catch { }
            if (path == null || path.StartsWith(Path.GetFullPath(gameDir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                throw new Exception("The game is running. Close it first.");
        }
    }

    // ---------------------------------------------------------------- install

    internal static void Install(string gameDir)
    {
        CheckGameDir(gameDir);
        gameDir = Path.GetFullPath(gameDir).TrimEnd('\\');
        Log("Installing SHMCD-VR " + ModVersion + " into " + gameDir);
        RemoveOldVersionLeftovers(gameDir, false);

        var asm = Assembly.GetExecutingAssembly();
        int n = 0;
        using (var s = asm.GetManifestResourceStream("bepinex.zip"))
        using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
        {
            foreach (var e in zip.Entries)
            {
                string dest = Path.Combine(gameDir, e.FullName.Replace('/', '\\'));
                if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                e.ExtractToFile(dest, true);
                n++;
            }
        }
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("payload/")) continue;
            string dest = Path.Combine(gameDir, res.Substring(8).Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            using (var s = asm.GetManifestResourceStream(res))
            using (var o = File.Create(dest)) s.CopyTo(o);
            n++;
        }
        Log("Copied " + n + " files (BepInEx 5 mod loader, SHMCD-VR, openvr_api.dll).");

        SwitchVR(gameDir, true);

        string uninst = Path.Combine(gameDir, UninstallerName);
        if (!string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(SelfPath, uninst, true);
            Log("Uninstaller: " + uninst);
        }

        if (!IsWine) SetUpSteamEntry(uninst);

        Log("");
        Log("Done. Start the game from Steam as usual: it plays in VR (SteamVR starts by itself).");
        if (IsWine)
        {
            Log("");
            Log("Linux / Steam Deck: two more steps. In Steam, right-click " + GameName + " > Properties:");
            Log("  1. Compatibility: tick 'Force the use of a specific Steam Play compatibility tool' and pick Proton.");
            Log("  2. Launch Options: enter");
            Log("    " + LinuxLaunchOption);
        }
    }

    // Unity's built-in VR: boot.config "vr-enabled=1" and "OpenVR" in the game's list of VR devices.
    static void SwitchVR(string gameDir, bool on)
    {
        string data = Path.Combine(gameDir, DataDir);
        string boot = Path.Combine(data, "boot.config");
        if (File.Exists(boot))
        {
            // The line is changed where it is (Unity writes it as "vr-enabled=0"), so switching back gives the original file.
            string text = File.ReadAllText(boot), want = "vr-enabled=" + (on ? "1" : "0");
            var m = Regex.Match(text, "^vr-enabled=[^\r\n]*", RegexOptions.Multiline);
            string updated = m.Success ? text.Substring(0, m.Index) + want + text.Substring(m.Index + m.Length)
                                       : text + (text.Length == 0 || text.EndsWith("\n") ? "" : "\n") + want + "\n";
            if (updated != text) File.WriteAllText(boot, updated, Encoding.ASCII);
        }
        else if (on) throw new Exception("No " + boot + ". Is the game fully installed? (Steam: right-click the game > Properties > Installed Files > Verify.)");

        string ggm = Path.Combine(data, "globalgamemanagers");
        try
        {
            GgmPatch.SetVrDevices(ggm, on ? new[] { "OpenVR" } : new string[0]);
            Log(on ? "Switched on the game's VR support (SteamVR)." : "Switched the game's VR support off again.");
        }
        catch (Exception e)
        {
            if (on) throw new Exception("Couldn't switch on VR in " + ggm + ": " + e.Message +
                " (A game update may have changed the file format. Please report it, with the game version.)");
            Log("Couldn't switch VR off in " + ggm + ": " + e.Message + ". Steam can restore it: right-click the game > Properties > Installed Files > Verify.");
        }
    }

    // Steam's entry for the game (see SteamVRTag.cs): listed in SteamVR's VR tab, starts SHMCD.exe directly. Needs
    // Steam closed; if it's running, ask to restart it, or let the background helper (the uninstaller copy with
    // /VRTAG-WAIT) do it after Steam next closes.
    static void SetUpSteamEntry(string helperExe)
    {
        bool? tagged = SteamVRTag.IsTagged();
        if (tagged == null) { Log("Couldn't read Steam's info for the game, so it isn't listed in SteamVR's VR tab."); return; }
        if (tagged == true) { Log("Listed in SteamVR's VR tab."); return; }
        try
        {
            if (!SteamVRTag.SteamRunning())
            {
                SteamVRTag.Apply(true);
                Log("Listed in SteamVR's VR tab; Steam starts the game directly (without the SUPERHOT Launcher).");
                return;
            }
            if (!Silent && MessageBox.Show("To list the game in SteamVR's VR tab and have Steam start it directly (without the SUPERHOT Launcher window), Steam has to close and reopen once.\r\n\r\nRestart Steam now?\r\n" +
                    "(If you choose No, it's done the next time Steam restarts.)", "SHMCD-VR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                SteamVRTag.ApplyWithSteamRestart(true, Log, Application.DoEvents);
                Log("Listed in SteamVR's VR tab; Steam starts the game directly (without the SUPERHOT Launcher).");
                return;
            }
        }
        catch (Exception e) { Log("Couldn't update Steam's settings for the game: " + e.Message); return; }
        SteamVRTag.StartWaiter(helperExe);
        Log("After Steam next restarts, the game appears in SteamVR's VR tab and starts without the SUPERHOT Launcher.");
    }

    static void RestoreSteamEntry()
    {
        // Stop a waiting helper first: it would set the entry up again, and it keeps the uninstaller file in use.
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(UninstallerName)))
            try { if (p.Id != Process.GetCurrentProcess().Id) p.Kill(); } catch { }
        if (!SteamVRTag.IsModified()) return;
        try
        {
            if (!SteamVRTag.SteamRunning()) { if (SteamVRTag.Apply(false)) Log("Removed from SteamVR's VR tab."); return; }
            if (!Silent && MessageBox.Show("To remove the game from SteamVR's VR tab, Steam has to close and reopen once.\r\n\r\nRestart Steam now?",
                    "SHMCD-VR", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (SteamVRTag.ApplyWithSteamRestart(false, Log, Application.DoEvents)) Log("Removed from SteamVR's VR tab.");
                return;
            }
            Log("Note: the game stays in SteamVR's VR tab until Valve next updates its store info (or run the uninstaller with Steam closed).");
        }
        catch (Exception e) { Log("Couldn't restore Steam's entry for the game: " + e.Message); }
    }

    // ---------------------------------------------------------------- uninstall

    internal static void Uninstall(string gameDir)
    {
        CheckGameDir(gameDir);
        gameDir = Path.GetFullPath(gameDir).TrimEnd('\\');
        Log("Removing SHMCD-VR from " + gameDir);
        RemoveOldVersionLeftovers(gameDir, true);
        if (!IsWine) RestoreSteamEntry();
        SwitchVR(gameDir, false);

        foreach (var f in ModFiles) TryDeleteFile(Path.Combine(gameDir, f));
        foreach (var d in ModDirs) TryDeleteDir(Path.Combine(gameDir, d));
        string plugins = Path.Combine(gameDir, @"BepInEx\plugins");
        bool others = Directory.Exists(plugins) && Directory.GetFileSystemEntries(plugins).Length > 0;
        if (!others)
        {
            foreach (var d in LoaderDirs) TryDeleteDir(Path.Combine(gameDir, d));
            foreach (var f in LoaderFiles) TryDeleteFile(Path.Combine(gameDir, f));
            Log("Removed SHMCD-VR and the BepInEx mod loader.");
        }
        else Log("Removed SHMCD-VR. Kept the BepInEx mod loader: other mods use it.");

        string uninst = Path.Combine(gameDir, UninstallerName);
        if (string.Equals(Path.GetFullPath(SelfPath), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
        {
            // A running exe can't delete itself: a short-lived cmd does it a moment after we exit.
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del \"" + uninst + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch { }
        }
        else TryDeleteFile(uninst);

        Log("");
        Log("Done. The game is back to the normal flat game. Your saves are untouched.");
    }

    // ---------------------------------------------------------------- leftovers of v0.1 (separate "... VR" entry)

    // v0.1 could add a separate "SUPERHOT: MIND CONTROL DELETE VR" entry: a launcher folder in the game folder,
    // desktop/Start menu shortcuts, a SteamVR library entry and a non-Steam game in Steam with artwork. Now Steam's
    // normal entry plays in VR, so that goes. The non-Steam game entry itself stays in Steam (the user removes it there).
    const string OldEntryName = "SUPERHOT: MIND CONTROL DELETE VR";

    static void RemoveOldVersionLeftovers(string gameDir, bool uninstalling)
    {
        string ld = Path.Combine(gameDir, "SHMCDVR-launcher");
        if (!Directory.Exists(ld)) return;

        if (!IsWine)
            foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
                TryDeleteFile(Path.Combine(dir, OldEntryName.Replace(":", "") + ".lnk"));

        // The Steam entry's artwork (Steam\userdata\<user>\config\grid\<shortcut id>*)
        string gidFile = Path.Combine(ld, "steam_gameid.txt");
        bool hadSteamEntry = false;
        try
        {
            ulong gid;
            if (File.Exists(gidFile) && ulong.TryParse(File.ReadAllText(gidFile).Trim(), out gid))
            {
                hadSteamEntry = true;
                string steam = WindowsSteamPath();
                string id = (gid >> 32).ToString();
                if (steam != null && Directory.Exists(Path.Combine(steam, "userdata")))
                    foreach (var user in Directory.GetDirectories(Path.Combine(steam, "userdata")))
                    {
                        string grid = Path.Combine(user, @"config\grid");
                        if (Directory.Exists(grid)) foreach (var f in Directory.GetFiles(grid, id + "*")) TryDeleteFile(f);
                    }
            }
        }
        catch { }

        // SteamVR library entry. SteamVR keeps its list in Steam\config\appconfig.json and rewrites that file when it
        // exits, so it can only be edited while SteamVR is closed. Otherwise the mod removes the entry itself the next
        // time the game runs in VR (it needs the manifest file for that, so it stays when installing).
        string manifest = Path.Combine(ld, "shmcdvr.vrmanifest");
        if (File.Exists(manifest) && Process.GetProcessesByName("vrserver").Length > 0)
        {
            foreach (var f in Directory.GetFiles(ld)) if (!f.Equals(manifest, StringComparison.OrdinalIgnoreCase)) TryDeleteFile(f);
            if (uninstalling)
                Log("Note: SteamVR is running, so the old '" + OldEntryName + "' entry stays in SteamVR's library. Close SteamVR and run the uninstaller again to remove it.");
        }
        else
        {
            if (File.Exists(manifest) && RemoveFromSteamVRConfig(manifest)) Log("Removed the old '" + OldEntryName + "' entry from SteamVR's library.");
            TryDeleteDir(ld);
        }

        Log("Removed v0.1's '" + OldEntryName + "' launcher and shortcuts.");
        if (hadSteamEntry)
            Log("Your Steam library still has the old '" + OldEntryName + "' entry. Remove it in Steam: right-click it > Manage > Remove non-Steam game.");
    }

    static string WindowsSteamPath()
    {
        try { var v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; return v == null ? null : v.Replace('/', '\\'); }
        catch { return null; }
    }

    static bool RemoveFromSteamVRConfig(string manifest)
    {
        try
        {
            string steam = WindowsSteamPath();
            if (steam == null) return false;
            string cfg = Path.Combine(steam, @"config\appconfig.json");
            if (!File.Exists(cfg)) return false;
            string text = File.ReadAllText(cfg);
            var arr = Regex.Match(text, "(\"manifest_paths\"\\s*:\\s*\\[)(.*?)(\\])", RegexOptions.Singleline);
            if (!arr.Success) return false;
            var keep = new List<string>();
            bool removed = false;
            string want = Path.GetFullPath(manifest);
            foreach (Match e in Regex.Matches(arr.Groups[2].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string p = e.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                bool same;
                try { same = string.Equals(Path.GetFullPath(p), want, StringComparison.OrdinalIgnoreCase); } catch { same = false; }
                if (same) removed = true; else keep.Add(e.Value);
            }
            if (!removed) return false;
            if (!File.Exists(cfg + ".shmcdvr-backup")) File.Copy(cfg, cfg + ".shmcdvr-backup");
            string body = keep.Count == 0 ? "" : "\n      " + string.Join(",\n      ", keep.ToArray()) + "\n   ";
            File.WriteAllText(cfg, text.Substring(0, arr.Index) + arr.Groups[1].Value + body + arr.Groups[3].Value + text.Substring(arr.Index + arr.Length));
            return true;
        }
        catch (Exception e) { Log("Couldn't edit SteamVR's app list: " + e.Message); return false; }
    }

    static void TryDeleteFile(string p) { try { if (File.Exists(p)) File.Delete(p); } catch (Exception e) { Log("Couldn't delete " + p + ": " + e.Message); } }
    static void TryDeleteDir(string p) { try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch (Exception e) { Log("Couldn't delete " + p + ": " + e.Message); } }
}

// ---------------------------------------------------------------- window

class SetupForm : Form
{
    readonly ComboBox _dir = new ComboBox();
    readonly TextBox _log = new TextBox();
    readonly Button _install = new Button(), _uninstall = new Button(), _close = new Button(), _browse = new Button(), _copy = new Button();

    public SetupForm(string gameDir, bool uninstallMode)
    {
        Text = "SHMCD-VR " + ShmcdVrSetup.ModVersion + (uninstallMode ? " - Uninstall" : " - Setup");
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 400);
        MinimumSize = new Size(480, 320);
        StartPosition = FormStartPosition.CenterScreen;
        try { Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); } catch { }

        var title = new Label { Text = "VR mod for SUPERHOT: MIND CONTROL DELETE (Steam)", Font = new Font("Segoe UI", 12f, FontStyle.Bold), AutoSize = true, Location = new Point(12, 10) };
        var lbl = new Label { Text = "Game folder:", AutoSize = true, Location = new Point(12, 48) };
        _dir.Location = new Point(12, 68); _dir.Width = 500; _dir.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _browse.Text = "Browse..."; _browse.Location = new Point(520, 67); _browse.Width = 88; _browse.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical; _log.BackColor = SystemColors.Window;
        _log.Location = new Point(12, 104); _log.Size = new Size(596, 244); _log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _install.Text = "Install"; _uninstall.Text = "Uninstall"; _close.Text = "Close"; _copy.Text = "Copy launch option";
        foreach (var b in new[] { _install, _uninstall, _close }) { b.Width = 96; b.Height = 30; b.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; }
        _close.Location = new Point(512, 358); _uninstall.Location = new Point(408, 358); _install.Location = new Point(304, 358);
        _copy.Width = 150; _copy.Height = 30; _copy.Location = new Point(12, 358); _copy.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; _copy.Visible = false;
        Controls.AddRange(new Control[] { title, lbl, _dir, _browse, _log, _install, _uninstall, _close, _copy });

        ShmcdVrSetup.Log = s => { _log.AppendText(s + "\r\n"); _log.Update(); };
        _browse.Click += (s, e) =>
        {
            using (var d = new FolderBrowserDialog { Description = "Pick the SUPERHOT: MIND CONTROL DELETE folder (the one with SHMCD.exe)" })
                if (d.ShowDialog(this) == DialogResult.OK) { _dir.Text = d.SelectedPath; UpdateButtons(); }
        };
        _dir.TextChanged += (s, e) => UpdateButtons();
        _close.Click += (s, e) => Close();
        _copy.Click += (s, e) => { try { Clipboard.SetText(ShmcdVrSetup.LinuxLaunchOption); } catch { } };
        _install.Click += (s, e) => Run(() => ShmcdVrSetup.Install(_dir.Text.Trim()), true);
        _uninstall.Click += (s, e) => Run(() => ShmcdVrSetup.Uninstall(_dir.Text.Trim()), false);

        var found = ShmcdVrSetup.FindGames();
        foreach (var g in found) _dir.Items.Add(g);
        if (!string.IsNullOrEmpty(gameDir)) _dir.Text = gameDir;
        else if (found.Count > 0) _dir.SelectedIndex = 0;

        if (uninstallMode) ShmcdVrSetup.Log("Click Uninstall to remove SHMCD-VR from this game folder.");
        else if (found.Count == 0) ShmcdVrSetup.Log("Couldn't find SUPERHOT: MIND CONTROL DELETE in your Steam libraries. Click Browse and pick the game folder (the one with SHMCD.exe).");
        else
        {
            ShmcdVrSetup.Log("Found SUPERHOT: MIND CONTROL DELETE" + (found.Count > 1 ? " (" + found.Count + " copies; pick one above)" : "") + ".");
            ShmcdVrSetup.Log("Click Install. Afterwards, Steam's normal entry for the game plays in VR.");
        }
        if (ShmcdVrSetup.IsWine) ShmcdVrSetup.Log("(Running under Proton/Wine.)");
        UpdateButtons();
        AcceptButton = uninstallMode ? _uninstall : _install;
    }

    void UpdateButtons()
    {
        string d = _dir.Text.Trim();
        bool hasGame = d.Length > 0 && File.Exists(Path.Combine(d, "SHMCD.exe"));
        _install.Enabled = hasGame;
        _install.Text = ShmcdVrSetup.IsInstalled(d) ? "Reinstall" : "Install";
        _uninstall.Enabled = hasGame && ShmcdVrSetup.IsInstalled(d);
    }

    void Run(Action action, bool installing)
    {
        _install.Enabled = _uninstall.Enabled = _browse.Enabled = _dir.Enabled = false;
        _log.Clear();
        Cursor = Cursors.WaitCursor;
        try
        {
            action();
            if (installing && ShmcdVrSetup.IsWine) _copy.Visible = true;
        }
        catch (Exception e) { ShmcdVrSetup.Log(""); ShmcdVrSetup.Log("Failed: " + e.Message); }
        finally { Cursor = Cursors.Default; _browse.Enabled = _dir.Enabled = true; UpdateButtons(); }
    }
}
