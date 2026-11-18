# SHMCD-VR

An unofficial VR mod for **SUPERHOT: MIND CONTROL DELETE** (Steam), played with SteamVR motion controllers.

> Early test version. It is playable, but expect rough edges. Please report what you find (see Feedback below).

The game is a flat-screen Unity 2018 game. This mod turns on Unity's built-in OpenVR support and then fixes what
breaks in a headset, aiming for a SUPERHOT VR-like feel:

- **Stereo 3D** at full frame rate. Screen-space effects that caused double vision (fisheye punches, death
  screenshot reveal, crosshair, HUD and menus composited per eye) are fixed or moved onto a floating panel.
- **Time moves when you move** - head and hand motion drive time, like SUPERHOT VR.
- **Motion controllers** through SteamVR Input (default bindings for Valve Index, Oculus Touch and Vive wands).
- **Hand aiming**: guns, punches, throws and the crosshair follow your right hand.
- **3D hands**: weapons sit in your hand; bare hands make a fist when you squeeze the grip.
- **Grab and throw**: squeeze the grip to pick up, let go to throw (the item flies the way your hand moves).
- **Physical punches and melee swings**, and an optional **one-hit knockout** setting.
- Comfort: no camera roll/shake, pitch lock, snap turn, a slower level-start intro.

Tested on a Bigscreen Beyond with Valve Index controllers. Other headsets that work with SteamVR should work, but
are untested.

## Requirements

- SUPERHOT: MIND CONTROL DELETE (Steam, Windows)
- SteamVR and a VR headset with motion controllers

## Install

1. Download **`SHMCD-VR-Setup-v0.2.0.exe`** from [Releases](../../releases) and double-click it.
2. It finds the game in your Steam libraries (or click Browse). Click **Install**.
   - If Steam is open, it asks to restart Steam once. If you say no, the last step happens the next time Steam
     restarts.
3. Start the game from Steam as usual, on the desktop or from the SteamVR library's **VR** tab. It goes straight
   into the game in VR.

What the installer does:

- Installs BepInEx 5.4.23.6 (x86, the mod loader) and the mod into the game folder.
- Switches on the game's own (Unity) VR support: `vr-enabled=1` in `SHMCD_Data\boot.config` and "OpenVR" in the
  game's list of VR devices in `SHMCD_Data\globalgamemanagers`. Only that one setting in the file is changed.
- Changes Steam's local copy of the game's launch settings, so that the game is listed in SteamVR's VR tab and
  Steam starts `SHMCD.exe` directly instead of the SUPERHOT Launcher window. If a Steam update resets this, the
  mod sets it up again after Steam next closes.
- Leaves `SHMCD-VR Uninstall.exe` in the game folder.

**Upgrading from 0.1:** just run the new installer. It removes the old launcher, desktop / Start menu shortcuts and
SteamVR entry. If you added the separate "SUPERHOT: MIND CONTROL DELETE VR" entry to Steam, remove it there
(right-click it > Manage > Remove non-Steam game); the normal entry does the job now.

**Linux (Steam + Proton):** run the installer through Proton (add it to Steam as a non-Steam game, force Proton,
start it). It installs the mod and then lists two Steam settings for the game to make by hand: force Proton, and a
launch option.

**Uninstall:** double-click `SHMCD-VR Uninstall.exe` in the game folder. It puts every changed file back exactly as
it was, removes BepInEx (unless other mods use it) and restores Steam's launch settings. Your saves are untouched.

### With UniGetUI or Scoop

SHMCD-VR is also in the [technewsguy/scoop-vr-mods](https://github.com/technewsguy/scoop-vr-mods) bucket, so
[UniGetUI](https://github.com/marticliment/UniGetUI) or [Scoop](https://scoop.sh) can install it and keep it up to
date:

```powershell
scoop bucket add vrmods https://github.com/technewsguy/scoop-vr-mods
scoop install vrmods/shmcd-vr
```

It runs the same installer silently. Game in an unusual place? Set the `SHMCDVR_GAMEDIR` environment variable to
its folder first.

## Controls (Valve Index)

| Control | Action |
|---|---|
| Left stick | Move (toward where you look) / menu navigation |
| Right stick | Snap turn |
| Right trigger | Attack / shoot / confirm (also throws an empty gun) |
| Right grip | Squeeze: pick up what you point at, or make a fist. Release: throw |
| Grip + jab or swing | Punch / melee hit (either hand) |
| Right A | Jump |
| Right B | Throw / back |
| Left trigger | Hotswitch |
| Left A | Restart |
| Left B | Pause |
| Click both sticks | Recenter view |
| F12 (keyboard) | Save a stereo headset screenshot to `BepInEx\shmcdvr_shots` |

Bindings can be changed in SteamVR's controller binding settings for the game.

## Settings

`BepInEx\config\shmcdvr.mod.cfg` (created on first start). Highlights:

- `[Gameplay] OneHitKills` - one punch / melee hit takes an enemy down (default on).
- `[Controllers] GripGrabRelease`, `MotionPunch`, `PunchSpeed`, `HandAim`, `HandModelScale`, `HideArms`, ...
- `[Time] MotionTime`, `HandSpeedFull`, `HeadSpeedFull`, `IntroTimeScale`, ...
- `[UI] Distance`, `Width`, `CrosshairSize`, ...
- `[Debug] LogControllerButtons`, `LogEffects` - verbose logs in `BepInEx\LogOutput.log`.

## Feedback

Please open an [issue](../../issues) with:

- what happened and what you expected,
- your headset and controllers,
- `BepInEx\LogOutput.log` from the session, and an F12 screenshot if it is something you can see.

Known rough spots: some melee weapons can sit oddly in the hand (`MeleeFlipNames`, `MeleeHandRoll` help), only
some weapons have been checked, and menus are on a flat panel.

## Building from source

`build.ps1 -GameDir "<game folder with BepInEx installed>" [-Install]` compiles `src\*.cs` against the game's
assemblies (C# 5 / .NET 3.5, Windows `csc`) and builds the installer `SHMCD-VR-Setup.exe`. `package.ps1` builds the
release files (the installer and a zip for Scoop). See `NOTES.md` for how things
work and the gotchas found along the way.

## Disclaimer

Fan-made and unofficial. Not affiliated with or endorsed by SUPERHOT Team. You need your own copy of the game;
this repository contains no game code or assets.

## License

MIT for this mod's code (see `LICENSE`). Third-party components: see `THIRD_PARTY.md`.
