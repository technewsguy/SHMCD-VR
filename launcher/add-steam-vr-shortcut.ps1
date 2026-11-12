# Adds "SUPERHOT: MIND CONTROL DELETE VR" to the Steam library as a non-Steam game with "Include in VR Library" on,
# plus the game's Steam artwork (cover, header, background, logo), a SteamVR library entry, and desktop / Start menu
# shortcuts. It starts through the launcher (SteamVR closes its dashboard, no "Resume Game" menu).
# Steam must be closed while its shortcut list is edited: this script closes and restarts it.
# Usage: .\add-steam-vr-shortcut.ps1 -GameDir "<folder with SHMCD.exe and the mod installed>"
param([Parameter(Mandatory = $true)][string]$GameDir, [string]$BuildDir = "")
$ErrorActionPreference = 'Stop'
$name = "SUPERHOT: MIND CONTROL DELETE VR"
if (-not $BuildDir) { $BuildDir = Join-Path $PSScriptRoot '..\build' }
$BuildDir = (Resolve-Path $BuildDir).Path
if (-not (Test-Path (Join-Path $GameDir 'SHMCD.exe'))) { throw "SHMCD.exe not found in $GameDir" }
if (-not (Test-Path (Join-Path $GameDir 'BepInEx\plugins\SHMCDVR.dll'))) { throw "Install the mod into $GameDir first." }
foreach ($f in 'shmcdvr-launch.exe', 'SteamShortcut.exe', 'openvr_api.dll') { if (-not (Test-Path (Join-Path $BuildDir $f))) { throw "$f missing in $BuildDir (run build.ps1)" } }

$steamKey = Get-ItemProperty HKCU:\Software\Valve\Steam
$steam = $steamKey.SteamPath; $steamExe = $steamKey.SteamExe
$user = (Get-ItemProperty HKCU:\Software\Valve\Steam\ActiveProcess -ErrorAction SilentlyContinue).ActiveUser
if (-not $user) { $user = (Get-ChildItem "$steam\userdata" -Directory | Select-Object -First 1).Name }
$cfg = "$steam\userdata\$user\config"
$art = "$steam\appcache\librarycache\690040"

# 1. Launcher next to the game (outside BepInEx\plugins so BepInEx never tries to load its x64 openvr_api.dll)
$ld = Join-Path $GameDir 'SHMCDVR-launcher'
New-Item -ItemType Directory -Force $ld | Out-Null
Copy-Item (Join-Path $BuildDir 'shmcdvr-launch.exe'), (Join-Path $BuildDir 'openvr_api.dll') $ld -Force
$launcher = Join-Path $ld 'shmcdvr-launch.exe'

# 2. SteamVR library entry (registered by the launcher on its first run): runs the launcher with --noop
foreach ($p in @(@('header.jpg', 'shmcdvr_header.jpg'), @('library_600x900.jpg', 'shmcdvr_capsule.jpg'))) {
    if (Test-Path "$art\$($p[0])") { Copy-Item "$art\$($p[0])" (Join-Path $ld $p[1]) -Force }
}
$esc = { param($s) $s.Replace('\', '\\') }
$manifest = @"
{
  "source": "builtin",
  "applications": [{
    "app_key": "shmcdvr.mod.game",
    "launch_type": "binary",
    "binary_path_windows": "$(& $esc $launcher)",
    "working_directory": "$(& $esc $ld)",
    "arguments": "--noop",
    "is_dashboard_overlay": false,
    "image_path": "$(& $esc (Join-Path $ld 'shmcdvr_header.jpg'))",
    "image_path_capsule": "$(& $esc (Join-Path $ld 'shmcdvr_capsule.jpg'))",
    "strings": { "en_us": { "name": "$name", "description": "SUPERHOT: MIND CONTROL DELETE with the SHMCD-VR mod" } }
  }]
}
"@
Set-Content (Join-Path $ld 'shmcdvr.vrmanifest') $manifest -Encoding UTF8

# 3. Desktop + Start menu shortcuts
$ws = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) {
    $lnk = $ws.CreateShortcut((Join-Path $dir "$($name.Replace(':', '')).lnk"))
    $lnk.TargetPath = $launcher; $lnk.WorkingDirectory = $ld
    $lnk.IconLocation = "$(Join-Path $GameDir 'SHMCD.exe'),0"
    $lnk.Description = "SUPERHOT: MIND CONTROL DELETE in VR (SHMCD-VR mod)"
    $lnk.Save()
}

# 4. Steam library entry + artwork (Steam closed while shortcuts.vdf is edited)
if (Get-Process steam -ErrorAction SilentlyContinue) {
    Write-Host "Closing Steam..."
    Start-Process $steamExe -ArgumentList '-shutdown'
    $t = 0; while ((Get-Process steam -ErrorAction SilentlyContinue) -and $t -lt 60) { Start-Sleep 1; $t++ }
    if (Get-Process steam -ErrorAction SilentlyContinue) { throw "Steam did not close; close it and run this again." }
}
if ((Test-Path "$cfg\shortcuts.vdf") -and -not (Test-Path "$cfg\shortcuts.vdf.shmcdvr-backup")) { Copy-Item "$cfg\shortcuts.vdf" "$cfg\shortcuts.vdf.shmcdvr-backup" }
$id = & (Join-Path $BuildDir 'SteamShortcut.exe') "$cfg\shortcuts.vdf" $name $launcher $ld (Join-Path $GameDir 'SHMCD.exe') '-' 1
if ($LASTEXITCODE -ne 0 -or -not $id) { throw "Adding the Steam shortcut failed." }
$id = "$id".Trim()
# The desktop shortcut hands over to this Steam entry (so Steam Input and the overlay work).
$gameId = ([uint64]$id -shl 32) -bor 0x02000000
Set-Content (Join-Path $ld 'steam_gameid.txt') $gameId
New-Item -ItemType Directory -Force "$cfg\grid" | Out-Null
$pairs = [ordered]@{ 'header.jpg' = "$id.jpg"; 'library_600x900.jpg' = "${id}p.jpg"; 'library_hero.jpg' = "${id}_hero.jpg"; 'logo.png' = "${id}_logo.png" }
foreach ($k in $pairs.Keys) { if (Test-Path "$art\$k") { Copy-Item "$art\$k" "$cfg\grid\$($pairs[$k])" -Force; Write-Host "artwork: $k -> grid\$($pairs[$k])" } }
Write-Host "Added '$name' (shortcut id $id). Starting Steam..."
Start-Process $steamExe
