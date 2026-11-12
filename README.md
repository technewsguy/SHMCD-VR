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
- Python 3 (only used once by the installer, to switch VR on in the game data; https://www.python.org/downloads/)

## Install

1. Download the latest `SHMCD-VR-v*.zip` from [Releases](../../releases) and unzip it anywhere.
2. Close the game. Double-click `install.cmd` (or run `install.ps1` in PowerShell).
   - It finds the game through Steam; otherwise run `install.ps1 -GameDir "<folder with SHMCD.exe>"`.
   - It installs BepInEx 5.4.23.6 (x86) from its official GitHub release, copies the mod, sets
     `vr-enabled=1` in `boot.config`, and patches `globalgamemanagers` to enable OpenVR
     (it asks before installing the `UnityPy` Python package).
   - Originals are kept as `*.orig`.
3. Start SteamVR, then start the game.

**Uninstall:** run `uninstall.cmd`, or verify the game files in Steam. Back up your game folder first if you want
to be extra careful; the installer only changes the files listed above.

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
some weapons have been checked, menus are on a flat panel, and the Steam "resume" overlay can appear at start.

## Building from source

`build.ps1 -GameDir "<game folder with BepInEx installed>" [-Install]` compiles `src\*.cs` against the game's
assemblies (C# 5 / .NET 3.5, Windows `csc`). `package.ps1` builds the release zip. See `NOTES.md` for how things
work and the gotchas found along the way.

## Disclaimer

Fan-made and unofficial. Not affiliated with or endorsed by SUPERHOT Team. You need your own copy of the game;
this repository contains no game code or assets.

## License

MIT for this mod's code (see `LICENSE`). Third-party components: see `THIRD_PARTY.md`.
