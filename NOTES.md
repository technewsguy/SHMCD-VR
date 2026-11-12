# SHMCD-VR â€” SUPERHOT: MIND CONTROL DELETE VR mod

## Target
- Game: `T:\SteamLibrary\steamapps\common\SUPERHOTMCD` (appid 690040). Unity 2018.4.14f1, Mono, **32-bit** player `SHMCD.exe`
  (`SUPERHOTMCD.exe` is a 64-bit launcher stub; `SUPERHOT-Launcher.exe` is the Electron launcher).
- Test copy: `T:\SHMCD-VR\game` (launch `SHMCD.exe` directly; `steam_appid.txt` is present).
- Log: `%USERPROFILE%\AppData\LocalLow\SUPERHOT_Team\SHMCD\output_log.txt` (`-logFile` is ignored).
- `boot.config` has `single-instance=`: a second launch exits at once (exit 1/0) while one is running.

## Spike result (2026-11-09): Unity built-in VR WORKS
Steps applied to the test copy:
1. `python -I _tools\patch_ggm.py <SHMCD_Data>\globalgamemanagers OpenVR` (BuildSettings.enabledVRDevices; keeps `.orig`).
2. `boot.config`: `vr-enabled=1`.
3. `deps\openvr-win32\openvr_api.dll` (x86) -> `SHMCD_Data\Plugins\`.

Observed: "OpenVR initialized!", multi-pass stereo, 91 fps / 0 dropped on Beyond (90 Hz), stereo screenshot shows
correct per-eye parallax + head tracking (`T:\SHMCD-VR\shot1_vr.png`). InControl auto-detects Knuckles as joystick.
Each change alone is harmless; both are needed.

Known issues:
- Log spam every frame: "Cannot set field of view on camera 'StandardCamera' while VR is enabled" (game sets FOV; patch out).
- Steam "Resume game" dashboard opens over it (same as NSLT; launcher approach solves it).

## Tools
- `_tools\vrprobe\VrProbe.exe [shotBase]` â€” scene app, fps, and stereo screenshot (`<base>_vr.png` = submitted frames, trustworthy).
- `_tools\inspect_ggm.py`, `_tools\patch_ggm.py` â€” UnityPy (pip) based.

## Modding setup (2026-11-09)
- BepInEx 5.4.23.6 **x86** installed in test copy (zip in `deps\bepinex`); runs on Mono CLR 2.0 â†’ plugins target **net35**.
- Decompiled source: `_decomp\Assembly-CSharp` (2,860 files, ilspycmd 8.2 from NSLT-VR\_tools\ilspy).
- Reference: SUPERHOT VR (`S:\SteamLibrary\steamapps\common\SUPERHOT VR`) is IL2CPP â†’ names/signatures only (Il2CppDumper/Cpp2IL). Study only; don't copy code/assets.

## Code map
- Camera chain: Player â†’ `Camera` container (`CameraManager.Container`, pitch via `CameraLook.pivotY`, yaw on body)
  â†’ `_Camera` (`StandardCameraPrefab`) â†’ `StandardCamera` (Unity VR drives this one's local pose).
- `CameraManager.GetCrosshairRay()` = StandardCamera viewport center â†’ aim already follows the head in VR.
  `ReplaceStandardCamera` reparents the camera for cutscenes/photo mode (comfort concern).
- FOV log spam: `CameraProxy.LateUpdate` sets `Camera.fieldOfView = fov` every frame â†’ patch out.
- Time: `TimeControl.LateUpdate` sets `Time.timeScale` = max of all `ITimeShifter.CurrentTimeModifier` (clamped 0.005..1).
  Register our own `TimeControl.GetNewInstantTimeShifter()` and feed it head/hand velocity.
  `CameraLookTimeControl` (look shifter, 0.03/deg) measures `_Camera` rotation = mouse look only, not the HMD.
  `PlayerController` line ~392: `_movementTimeShifter = magnitude * num`.
- Camera effects: `CameraEffectsManager` named interpolators ("Jumping", "LandingPunch", "ErrorPunch", ... `ListEffects()`),
  `CameraShaker`, `CameraEffectRoll` â†’ disable the head-moving ones.
- Input: `SHInput` / `SHInputCamera` (namespace `InputSystem`) on top of InControl.

## Plugin v0.1.0 (src\Plugin.cs, `build.ps1 [-Install]`, game must be closed to install)
- No FOV setter in VR (warnings 0), pitch lock, CameraEffectRoll/Rotation/PositionOffset + CameraShaker disabled,
  motion time shifter (head lin/ang + hand speed, re-registers if TimeControl registry resets), Stationary tracking,
  recenter at startup + F8, F9 camera dump. Config: BepInEx\config\shmcdvr.mod.cfg.
- Verified 2026-11-09: loads, no exceptions, 94 fps, shifter registers in level, clean stereo in-level (shot4_vr.png).
  Level start shows a glitch "melt" transition (game effect, not a bug).
- Camera in VR: `_Camera/StandardCamera`, near 0.03.
- User test 2026-11-09: motion time works, sensitivity good; turns physically (game has no stick turn bound for
  Index; sticks do nothing); height right. Double vision after hits â†’ fixed (below).
- Full-screen effects: everything goes through `CustomMaterialOnScreen` 'BlitSHGUI' (shader XULM POSTPROCESS/BlitSHGUI)
  on StandardCamera, animated by 53 named interpolators (F9 dumps them). Hit = "PlayerHit": _SciemPunch, _Mosaic*,
  _FishEyeIntensityX, GlobalVertexScale. Fisheye/scale are screen-space per eye â†’ double vision.
  Fix: `NoScreenWarps` holds `ScreenWarpProperties` at the shader's defaults (all 0) before the blit. Verified hit2_vr.png.
- Debug keys: F9 cameras + image effects + interpolators, F10 play "PlayerHit".
- Still to check: GlobalKaszetaX/Y (Hotswitch), HDR bump, menu effects.

## 2D UI â†’ floating panel (src\UIPanel.cs)
- All 2D UI is in global textures composited per eye by BlitSHGUI / terminal bloom at diverging positions
  (title "MORE" at 35% L / 62% R of each eye â†’ unfusable). Not caused by our patches (verified with fix off).
  - `_GlobalMiniMenuTexture` 'SHGUI MENU TEXTURE' 1216x912: title/menus, real alpha.
  - `_GlobalUITexture` 'ingame UI texture' 1920x1080 (CameralessRenderer): HUD/hearts/terminal text, cleared opaque black.
  - `_GlobalPrecomposedMenuTexture` Screen-sized (3200x1080 here): composite of the above; empty = grey/alpha 0.5.
- Fix: on the game camera's onPreRender swap those globals for "empty" stand-ins (UI = opaque black, menu = clear,
  precomposed = 50% grey) and restore at WaitForEndOfFrame. Real textures drawn on two Sprites/Default quads
  (layer 3) by our own stereo overlay camera (depth main+1, clear depth) parented to the game camera's rig, lazy follow.
- No additive shader exists (shader list: BepInEx\shmcdvr_dump\shaders.txt), no Unity editor here â†’
  transpiler swaps `Color.black` â†’ `Color.clear` in `CameralessRenderer.renderAllRegisteredRenderers` so the HUD has alpha.
- GOTCHA: an alpha-0 stand-in for `_GlobalUITexture` makes white world surfaces render solid blue (BlitSHGUI mask).
- GOTCHA: the game destroys our DontDestroyOnLoad objects on level loads â†’ `EnsureObjects()` recreates every frame.
- Verified by stereo screenshots (T:\SHMCD-VR\runs\blanksB, runs\title): title + HUD fuse, level + hit clean.
- White block before title text = Sprites/Default quad with no texture yet â†’ panels hidden until bound.

## Crosshair (src\UIPanel.cs UpdateCrosshair)
- Game crosshair = BlitSHGUI paints _GlobalCrosshairTexture at each eye image's centre (_GlobalCrosshairVector:
  x=sizeX=24.2/internalScale, y, w=power). Beyond eye frustums are asymmetric, so image centre != gaze: two crosshairs,
  neither on target. Aim/throw use StandardCamera transform forward (Weapon.GetTargetPoint, GetCrosshairRay) = correct.
- Fix: w=0 while the game camera renders (same swap/restore as UI globals); our own quad on the overlay camera along
  camera forward at the raycast hit distance, plus a 1.6x black copy behind as outline (dot is white, world is white).
  Texture 'crosshair-texture-basic-04' 512px, dot ~10% of it. Config [UI] WorldCrosshair, CrosshairSize (deg, 4).
  Verified 2026-11-10 runs\restart\6_vr.png: one dot per eye on the same surface point.

## Hand aim (src\HandAim.cs) â€” tier 3 step 1, 2026-11-10
- Pose actions pose_l/pose_r bound to /user/hand/*/pose/tip, read with GetPoseActionDataForNextFrame(Seated),
  converted RH->LH like SteamVR_Utils; world = StandardCamera.parent.TransformPoint (same space Unity uses for the
  camera's local pose; [space] log compares OpenVR seated head vs InputTracking head, both 0/0 with headset idle).
- Game aim sources: CameraManager.GetCrosshairRay (guns PlayerActions:530, melee, katana, pickup hover),
  Gun.GetVirtualMuzzlePosition (bullet spawn next to head), Weapon/Katana.SpawnThrowAway (StandardCameraTransform
  pos/forward; velocity = forward*28 + impulse toward GetTargetPoint(10)+0.05 up -> the "not precise" arc).
  Patched: ray postfix, muzzle postfix, throws move the camera transform onto the hand for the call (Finalizer restores).
- Not yet: weapon/arm model still drawn at the head; Katana.cs:155 / PlayerActions:164,596 still head-forward.
- Config [Controllers] HandAim (true), AimPitchOffset (deg).

## Upside-down big text (2026-11-10)
- "THROW THE GUN" etc. = TextMeshPro drawn by CameralessRenderer into _GlobalUITexture (our HUD panel), with
  GL.GetGPUProjectionMatrix(renderIntoTexture) -> stored upside down; BlitSHGUI flips it back, our panel didn't.
  Sprites/Default ignores mainTextureScale -> flip the HUD quad (negative Y scale, Cull Off). Config [UI] FlipHUD.
  (_GlobalTextpunchTexture is NOT sampled by BlitSHGUI â€” dead end.)

## Arms / weapon in 3D at the controller (src\VRHands.cs, 2026-11-10)
- Viewmodel = Core Mechanics/_Player/Hands/Standard (siblings: Hands/Oculus, empty). Pistol visible mesh =
  'hand_glock_baked_NEW' MeshRenderer, shader "XULM FULLFORWARD/SELF-CONTAINED FOV-INDEPENDENT ALWAYOS-ON-TOP
  BLACK-CRYSTAL" = own projection, ignores stereo -> the user's double vision on the gun arm.
- Fix: materials under the arms root swapped to "XULM FULLFORWARD/Black Crystal" (real 3D, depth-tested).
- Rest pose: Muzzle forward == Standard forward, Standard ~1.5 m ahead of the camera. onPreCull (game camera) sets
  Standard world pose = right-hand aim pose (+GripLocal, HandModelOffset, HandModelScale), restored at end of frame.
- User 2026-11-10: walking fixed. Pistol arm "comes out of the centre of my body" (flat viewmodel arm reaches
  from the camera point) - should look like a right hand; two-handed rifle from the centre looks right; punches not
  visible. Next: per-weapon data from the automatic [hands] ===== weapon ... dump (VRHands, once per weapon type).
- Build: needs UnityEngine.AnimationModule ref; DebugHooks uses Utilities.CameraEffects.PlayMode (ambiguous).
  Log [scene] loaded '<name>' added. After the tutorials the game boots to 'SHMenu' (Return no longer starts a level).
- Per-weapon models (dump 2026-11-10, Standard space):
  - Fists (TakedownWeapon): skinned 'male' (Weapon/Container/bHands, Animator AnimCtrl_PlayerMain), rig origin at the
    hips ~1.3 m below the hands; right fist jnt_R_Pistol_Aux rest ~(0.10, 1.32, 0.33). Old placement put the fists
    over the head -> "no punches visible". Now FistRestLocal goes to the controller.
  - Pistol: MeshRenderer 'hand_glock_baked_NEW' (glock 1801 + hand/forearm 571 verts, forearm 0.5 m along +X),
    child transform 'glock' = grip pivot, +Z barrel. Now the rig is solved so 'glock' sits at tip + (0,-0.03,-0.08).
  - Rifle (MachineGun): 'hand_karabin_baked NEW' (two-handed). User says it looks right -> unchanged (GripLocal).
- User: arms not attached at the shoulder / too far forward; throwing = "helicopter of arms". Direction chosen:
  SUPERHOT VR style hands only ([Controllers] HideArms):
  - Fists: PinFists() - each fist (jnt_*_Pistol_Aux) on its own controller + animation offset clamped to 0.25 m;
    jnt_Spine_01 scaled 0.01 and both wrists x100 so only hands keep size. Once per frame (2 eyes!).
  - Pistol: baked mesh copy with triangles removed behind the grip (glock-space z < -ForearmCut or > 0.30 m away).
  - Weapon draw tween (starts -130 deg) -> RecordDrawPose (prefix on all LaunchDrawingAnimation overrides) and
    FreezeDrawAnimation() shows the final pose while state == Drawing (rifle path; pistol path is grip-solved anyway).
  - BUG: first PinFists used FindBone(root) = the GUN skeleton (Standard/bHands, animator off); the fists skeleton is
    Standard/Weapon/Container/bHands. Now bones come from the enabled SkinnedMeshRenderer's skeleton (log
    "VRHands: fists skeleton ..."). User test before this fix: fists at sternum height, helicopter still there.
  - BUG 2: bone edits in Camera.onPreCull have no effect on SkinnedMeshRenderers (skinned in
    PostLateUpdate.UpdateAllSkinnedMeshes, before cameras). Rigid root moves did work there. Now VRHands.Place() runs
    at the end of LateUpdate ([DefaultExecutionOrder(32000)]) and again in onPreCull (PinFists once per frame).
  - Spine-scale trick (0.01 + wrists x100) left "arm bits stretching" and hands joined: blended vertices interpolate
    toward the collapse point. Replaced by ClipSkinnedArms: copy of the skinned 'male' mesh keeping only triangles
    whose vertices have >= 0.99 weight on the wrist bones and their children. Bones are no longer scaled.
  - User: hands OK and separate, but open-ended wrists bother them -> CapCutEdges: edges shared by a kept and a
    removed triangle are fanned to one centre vertex per hand (weight 1 on that wrist), winding flipped.
    Config [Controllers] CapWrists. Log "VRHands: capped wrists with N triangles (r / l edges)".
    First try found no edges: 'male' is faceted (unshared vertices per triangle) -> edges now matched on welded
    positions (rounded to 0.1 mm). Punch jab confirmed visible by the user.
  - Caps worked but "no wrist" (hand ends behind the knuckles). Now keeps vertices within WristStub (0.07 m, bind
    pose, around jnt_*_Arm_Wrist) and moves each arm rigidly from its clavicle (MoveArm) so the stub stays attached.
  - Stub version had "bits trailing behind, connecting the hands": cap sides were decided by "is a wrist descendant",
    so right-forearm stub edges were fanned into the LEFT cap centre. Now side = clavicle ancestor, cap centre skinned
    to the most common bone of its edge (the forearm).
  - Pistol wrist: ForearmCut 0.12 -> 0.18 (radius limit = cut + 0.22) and CapStatic() closes the baked mesh's cut
    (welded-position edge detection, one fan). User confirmed the bare hands' wrists look good.
  - User: both wrists look great. Throw twisted the hand inward too far -> fist rotation relative to the controller
    is clamped to MaxAnimAngle (20 deg) around a learned resting angle (learned/updated only while the animated
    offset < 3 cm, i.e. idle).
  - Clamp-around-learned-rest failed (throw still ~90 deg too far, 2nd punch fist pointing at the player): slow
    motion holds animations mid-way, so "idle" was learned wrong. Now FistAim(): fist rotation fully from the
    controller. Fist frame = LookRotation(Pistol_Aux->PistolEnd_Aux, wrist->Pistol_Aux); target = idle pose from the
    dump relative to the view: fwd (-0.42,-0.02,0.91), up (-0.31,0.94,-0.13), mirrored in x for the left hand.
    Animation still contributes position (punch jab, clamped 0.25 m), not rotation.
    User confirmed 2026-11-10: throws natural, punches knuckles-forward, idle fist angle natural.
  - User asked about using SUPERHOT VR's hand models: declined (other game's copyrighted assets; study-only rule).
  - TakedownWeapon has no draw tween; the helicopter was the fists rig's throw/grab animation around the hips.
- Verified runs\hands2: gun + forearm from below pointing at the target, fused. Config [Controllers] Hands3D,
  HandModelOnController, HandModelScale, HandModelOffset. F9 also logs [hands] hierarchy/bones/renderers.

## Controllers (src\VRInput.cs, input\*.json â†’ BepInEx\plugins\shmcdvr_input)
- SteamVR Input action manifest set from inside Unity's built-in OpenVR session works (SetActionManifestPath after
  XR init; "SteamVR Input active", both hands active). Actions â†’ virtual Xbox-style InControl device (NSLT-VR design).
- Game gamepad bindings (InputSystem\SHInput*.cs): move LS; attack RT/A; jump LT (or Y!); throw B/RB/R3; hotswitch X/LB;
  restart Y/Select; pause Start/Menu; menus confirm A/RT/RB, back B, nav LS/dpad. Y = jump AND restart â†’ unused.
- Mapping: LS move (head-relative in gameplay only), RS snap turn (rotates CameraLook.pivotX; game gets no RS),
  RT attack/confirm, R-A jump(LT), R-B throw/back, grips throw(R3), LT hotswitch(X), L-A restart(Select), L-B pause(Start),
  both stick clicks recenter. Aim is still the head (crosshair ray).
- User test 2026-11-10: move + snap turn good. Trigger pickup threw instantly: Knuckles grip ("grab" mode) fires while
  squeezing the trigger, and grip was mapped to throw. Now right grip -> Action1 (attack = pick up), grip no longer throws;
  throw = right B only. Game picks up with its attack input (PlayerActions: attack justPressed -> Attack()).
- Then SUPERHOT VR style (user request): right grip squeeze = attack only if empty-handed (HandManager.THIS.weapon null
  or TakedownWeapon = fists), let go = 3-frame R3 throw pulse, only if grip held >= 0.25 s on an item (guards against
  the grip that fires during trigger pulls). B still throws.
- BUG fixed 2026-11-10: grip squeeze must be a 3-frame attack TAP, not held. Users keep the Index grip squeezed
  (log grip=True throughout); a held Action1 keeps attack "pressed" so RT never produces justPressed -> no punches
  (tutorial "press RT to punch" only clears on a landed hit, PejAiBodyPart "takedown").
- User decision 2026-11-10: follow the GAME's control scheme first (tutorials prompt RT to throw an empty gun etc.);
  SUPERHOT VR style later. Grip grab/release now behind [Controllers] GripGrabRelease (default false = grip does nothing).
- BUG fixed 2026-11-10 "lose walking after a few rounds": InControl ActiveDevice = device that changed last; Unity
  also exposes both Knuckles as raw joysticks ("OpenVR Controller(Knuckles Right)") which steal it. PinActiveDevice
  (postfix InputManager.UpdateActiveDevice, sets private static activeDevice) keeps ours while controllers are live.
  Log: [input] active device. Likely also why RT sometimes did nothing.
- Still lost walking after replays: SHInputMovement SUMS LeftStick over ALL InputManager.Devices, and the raw Knuckles
  joysticks ('Unknown Device' meta "OpenVR Controller(Knuckles Left) - Left") have unrelated axes. VRInputDriver now
  DetachDevice()s any device whose name/meta contains "OpenVR" (every frame; logs [input] devices / detached).
  [move] log every 2 s while the stick is pushed.
- REAL cause of "lost movement" (2026-11-10): InControl InputManager.UpdateInternal only runs when
  `!SuspendInBackground || applicationIsFocused`; the desktop window loses focus (SteamVR dashboard, other apps,
  our sendkey tool) -> no device updates at all ([move] device=(0,0) active=None). VRInputDriver forces the private
  static `applicationIsFocused` back to true (logs "[input] game window lost focus; kept controller input running").
- Arms floating at level start: placement was gated on InGameplay/weapon; now HandAim.TryGetPoseAny + ArmsRoot finds
  Hands/Standard by name; hidden (scale 0) if the controller isn't tracked.
- Empty gun: Gun.Attack ammo 0 -> FireNoAmmo + ammo=-1; next attack -> ThrowNoAmmo. So RT throws on the 2nd pull.
  Debug logs [attack] (Gun.Attack ammo/ready/state) and [throw] (HandManager.Throw).
- Tutorial 2 (SHRL_R2Tutorial02FlowControl) gives a pistol, sets ammo=1, attackDelay=1.5 and ForceAttack()s = the
  "automatic" shot. isWeaponReady uses Time.time (scaled) -> standing still in VR the cooldown takes ~minutes, so RT
  did nothing. EmptyGunThrow (prefix Gun.Attack): ammo<=0 -> HandManager.Throw() at once. Config
  [Controllers] EmptyGunTriggerThrow. Verified in user's play 2026-11-10 ("[throw] empty gun -> throw").
- 2026-11-10 user chose SUPERHOT VR style: GripGrabRelease default true again. Added motion throws: right-hand pose
  velocity (pose.vVelocity, z flipped) peak over the last 0.15 s is noted at grip release (HandAim.NoteRelease);
  ThrowFromHand.Begin points the throw along it if >= ThrowMinSpeed (1 m/s), else along the hand's aim.
  Config [Controllers] ThrowByHandMotion, ThrowMinSpeed. Log "[throw] by hand motion, dir=...".
- User: grab/throw "worked pretty good"; wants a fist when gripping to punch. MotionPunch: grip squeeze only picks up
  when Crosshair2D type == Hand (pickup targeted); empty hand + grip + forward speed (velocity . pose forward) >=
  PunchSpeed 1.8 m/s = one attack tap (re-arm < 0.5 m/s). Left grip jab punches too: HandAim.UseLeftAimForFrames
  makes GetCrosshairRay use the left hand for the attack. Log "[punch] right/left jab".
- Motion punches verified in log: jabs -> TakedownWeapon.Attack ready=True, inFront=Enemy at 0.1-0.3 m (range 1.5).
  User: "no fist-making animation" when squeezing. Next: curl fingers on grip; [hands] finger(...) dump lists the
  wrist subtree (fists skeleton + gun skeleton, whose pistol grip pose may serve as the fist pose).
- Finger dump: jnt_{R,L}_Finger_0{1..5}_0{1..4} (01 = thumb), bone axis +X, bend into the palm = +local Z; the
  neutral pose is the same in every weapon (6-21 deg). FistOnGrip: per-hand curl 0..1 follows that hand's grip
  (8/s), adds Z: fingers 70/80/60 deg (joints 01/02/03), thumb 0/25/35; saved and restored each frame.
- User: fist looks good; punches harder to connect than SUPERHOT VR. Punch() now: (a) contact - fist speed >=
  ContactPunchSpeed (1.0) and OverlapSphere(ContactRadius 0.18) finds a collider under a PejAiBodyPart -> attack ray
  from 0.25 m behind the fist to its closest point; (b) jab - speed >= PunchSpeed (1.8) in ANY direction -> ray
  along the motion. HandAim.OverrideAim(ray, frames) replaces UseLeftAimForFrames. Log "[punch] right contact ...".
- Punch log 2026-11-11: contact punches mostly inFront=Enemy, but several rays first hit the PLAYER's own colliders
  (MeleeHitbox, CustomCollider, CustomColliderHead) -> IsInteractiveObjectVisible fails -> None. LogPunchAttack now
  disables all enabled colliders under PlayerController.Instance during TakedownWeapon.Attack (Finalizer restores).
- Game rule: a punch only damages a STUNNED enemy (PejAiBodyPart.TakeDownAttackReaction: first punch stuns, then
  health -= 2 / PunchesMod.GetPunches() per punch; strongStun = instant). Not one-punch like SUPERHOT VR.
- Speed-up at level start (2026-11-11): PlayerController.UpdateMovement sets _jumpTimeShifter = 0.75 while not
  grounded; spawn drop ('LandingPunch') counts. TimeStartLog (TimeFixes.cs, [DefaultExecutionOrder(-32000)]) zeroes
  it in LateUpdate unless lastJumpTime is within 3 s. Config [Time] NoFallTime. Logs "[time] start ..." (shifters >
  0.05) for 5 s after each scene load.
  Remaining start speed-up in SHRL_R2Tutorial02 = the level's own script: Timeshift(2f, 1f) ->
  SimplifiedTimeShifter=1.00 for ~2 s ([time] log). LevelTimeline waits are in game time, so suppressing it would
  stall the intro. User chose "slow it down": SimplifiedTimeShifter (amount constant, duration in Time.time) with
  amount >= 0.9 in the first 10 s after a scene load is capped to [Time] IntroTimeScale (0.5) -> intro at half speed,
  script timing intact.
- Thumb: user saw no change from 12/15 deg + 0.88 -> now no extra thumb bend, Finger_01_02 scale = [Controllers]
  FistThumbLength (0.8 -> 0.7 at user request 2026-11-11). User: intro slow-down "works great".
- Thumb in a fist wrapped around the fingertips: thumb bend 12/15 deg (was 25/35) and Finger_01_02 scaled to 0.88
  (tip follows) while curled; scale saved/restored with the rotation.
- GOTCHA: Harmony-patching PlayerController.UpdateMovement ALSO hangs Mono at startup (trace stops at
  "patching NoSpawnDropTime", ~0% CPU). Treat PlayerController/PlayerStats as unpatchable; use LateUpdate fixes.
- Pipe (SimpleMeleeWeapon, 'gazrurka' chunks) floated far ahead: melee weapons are held by the skinned fists rig
  (pipe mesh parented under the hand bones), but FistsActive required TakedownWeapon -> fell to GripLocal placement
  (hips at the hand). FistsActive now = any weapon + enabled skinned renderer. Melee swing: holding SimpleMeleeWeapon
  + grip + hand speed >= MeleeSwingSpeed (1.5) + enemy body part within OverlapSphere(hand + fwd*reach/2,
  reach/2 + 0.15), MeleeReach 0.9 -> attack ray at it. SimpleMeleeWeapon.Attack also ignores player colliders.
  Melee attack itself: AttackWithRayCast(ray, AttackRange) on Enemies/Glass/Bullets/Pickups + isWeaponReady.
- Pipe pointed down out of the fist: FlipMeleeWeapon() rotates the top-most enabled MeshRenderers under the right
  wrist 180 deg about the hand's side axis through jnt_R_Pistol_Aux (restored each frame). [Controllers] MeleeFlip.
- Melee swing log: many ready=False (game-time cooldown) and targets 'MeleeWeaponPejAiWeapon' (enemy weapon, which
  SimpleMeleeWeapon.AttackWithRayCast skips). Now HandAim.IsEnemyBody excludes enemy weapons, and attacks triggered
  by a physical punch/swing (HandAim.MotionAttackActive) reset weapon.lastAttackTime so they always connect.
- User: pipe up + swings better, but hand separate from the pipe; left hand should be open (no grip) or fist (grip).
  - After the flip the pipe is shifted so its mesh long axis (largest bounds dimension) passes through
    jnt_R_Pistol_Aux (fist centre).
  - Fingers no longer follow the animation: all finger joints start from their BIND pose (local rotation from the
    skin's bindposes: inverse(BindRot(parent)) * BindRot(bone)) = open hand, plus the grip curl = fist. Both hands.
- OneHitKills (src\OneHitKills.cs, [Gameplay] OneHitKills, default on per user 2026-11-11): prefix on
  PejAiBodyPart.TakeDownAttackReaction sets controller StunTimer > 0 and strongStun = true (non-boss) so the punch
  path kills; prefix on PejAiBodyPart.MeleeeAttackReaction forces DamageMode.Kill. Bosses/armour/bullet-proof
  unchanged. Patching PejAiBodyPart is safe (no startup hang).
- Pipe still not in the fist after centring: user's F12 shot (shots\20261111_122903) showed the pipe passing beside
  the fist (jnt_R_Pistol_Aux is not the fist hole). PlaceMeleeWeapon() (after the finger curl) now aligns the
  weapon's long axis with knuckle 05_01 -> 02_01 (out of the thumb side) and puts the point MeleeGripAt (0.2 from
  the bottom) at the midpoint of Finger_03_01 and Finger_03_03 (the fist hole). Replaces the 180-degree flip.
  Next shot (shots\20261111_130611) inconclusive (a pipe ~1.5 m out near an enemy; fist empty-looking). Added
  "[meleepos]" log (camera-local hole/top/bottom/fist) every 2 s while a melee weapon is placed.
  [meleepos] 2026-11-11: pipe top 0.55 m BELOW the fist -> knuckle 05->02 line points down in this model. Direction
  now from the controller: pose forward pitched up by [Controllers] MeleeAngle (50 deg); fist-hole centring kept.
  Then pipe direction OK (log top up-forward) but user: "fist is 180 degrees upside down" while holding it ->
  FistAim gets the controller rotation rolled by [Controllers] MeleeHandRoll (180) for SimpleMeleeWeapon only.
  User 2026-11-11: pipe good. Knife floated ~0.3 m above the real fist ([meleepos] hole vs rFist): the finger bones
  used for the fist hole were cached from the PIPE's skeleton (each melee weapon has its own Weapon/Container/bHands).
  Cache now keyed on the skeleton transform.
  Still off after that: [meleepos] hole ~ wrist but rFist (jnt_R_Pistol_Aux) 0.33 m away - the Aux/PistolEnd
  helpers are ANIMATED per weapon. PinFists/FistAim/MoveArm now use virtual anchors fixed on each wrist: from the
  skin bind pose if the helpers are skinned, else the rest offsets (0.096, 0.023, -0.064) / (0.234, 0.023, -0.065).
  Log "VRHands: fist anchors from ...". Re-check fists and pipe after this.
  User: knife now in the fist but wants hand and knife both flipped. Knife = SimpleMeleeWeapon whose private
  weaponPickup (SimpleMeleeWeaponPickup) has isKnife. Knives use [Controllers] KnifeHandRoll (0, vs MeleeHandRoll
  180 for the pipe) and KnifeFlip (swap which end points up). Fists confirmed fine with the wrist anchors.
  Knife confirmed good. New level: LEFT hand turned to face the player - the fallback used the right hand's offsets
  for both wrists (left skeleton is mirrored). Now MeasureRestAnchors() records both hands' Aux/End offsets on the
  wrist the first time bare fists are seen (helpers at rest) and reuses them; before that the left uses the negated
  right values. Log "VRHands: measured fist anchors ...".
  Left hand confirmed right. Baseball bat (Chunk3, len 1.21) upside down: which end is "top" is picked from the
  animated pose. Added [Controllers] MeleeFlipNames (default "bat,baseball,kij", matched against the mesh name);
  [meleepos] now logs mesh='...' to confirm the bat's real mesh name. Confirmed: 'baseball-bat-halfempty2'; bat
  orientation good. Hand was upside down -> flip-listed weapons also use KnifeHandRoll (0) instead of
  MeleeHandRoll (180). Then the bat was held by the wrong end: the "top" end is picked relative to the (now rolled)
  hand, so the extra flip double-flipped. Listed weapons now = knife hand roll only, no flip. Rule: pipe = roll
  180; bat = roll 0; knife = roll 0 + KnifeFlip. (End choice is still pose-dependent; a deterministic thick-end
  rule would be more robust if more weapons misbehave.)
- One-hit melee still needed several hits: hits on arms (jnt_R_Arm_Elbow/Wrist) only cut the limb. OneHitMelee
  postfix now calls body.Kill(MeleePunch) if the enemy survived (non-boss, not bullet-proof).
- F12 (Steam screenshot) does nothing: game is launched outside Steam, no overlay. [Keys] StereoScreenshot (F12) now
  starts VrProbe -> T:\SHMCD-VR\shots\<yyyyMMdd_HHmmss>_vr.png (log "[shot] ..."). Verified 2026-11-11.
  GOTCHA: Unity Input only sees keys while the desktop window is focused (rare in VR) -> the key is also polled with
  user32 GetAsyncKeyState. Verified with an unfocused simulated F12.
- Save reset 2026-11-10 at user request; backup in T:\SHMCD-VR\save_backup_20261110_*. Save shared with the real install.

## Death / restart (2026-11-09)
- Real hit in level 1 = 'PlayerHit' + 'PlayerDeath' (2 s: _MosaicFactorX, _FishEyeIntensityX, GlobalVertexScale/Loading),
  then 'LandingPunch' on respawn. F10 alone (PlayerHit) did NOT reproduce the user's double vision.
- Real cause: `ImageRevealEffectController.makeScreenshot` renders ONE flat Screen-sized (3200x1080) screenshot and
  `XulmReduxImageRevealEffect` blends it into the restarted view per eye â†’ unfusable. `NoImageReveal` passes it through in VR.
  Verified: T:\SHMCD-VR\runs\realhit (before, s031/s041 31% vs 59%) vs runs\realhit2 (after, consistent).
- Debug: F11 toggles TimeControl.forcedTimeScale=1 (enemies act without you moving); [Debug] LogEffects logs [fx]/[cam].
- GOTCHA: Harmony-patching `PlayerStats.ReceiveDamage` deadlocks Mono at startup (0% CPU). Patches are now applied
  per class with BepInEx\shmcdvr_patch_trace.txt so a hang shows which one.
- GOTCHA: VrProbe screenshot base names must not contain '.' (SteamVR then writes only the preview).
- `_tools\testrun.ps1 -Name x` = launch, title/level/hit shots, contact sheet. Config [UI]: Distance 2, Width 2.4, FollowAngle 25.

## Next
- Headset test + tune Time settings; world-space UI/HUD; launcher (dashboard); hands (tier 3).
