# SHMCD-VR installer: SUPERHOT: MIND CONTROL DELETE in VR (SteamVR).
# Usage (PowerShell):  .\install.ps1                 auto-detects the Steam install
#                      .\install.ps1 -GameDir "D:\Games\SUPERHOTMCD"
#                      .\install.ps1 -Uninstall      restores the original game files
param([string]$GameDir, [switch]$Uninstall, [switch]$Yes)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$BepInExUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x86_5.4.23.6.zip'
$UnityPyVersion = '1.25.4'

function Find-Game {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return $null }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) { $libs += (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"').Matches | ForEach-Object { $_.Groups[1].Value -replace '\\\\', '\' } }
    foreach ($l in $libs) {
        $d = Join-Path $l 'steamapps\common\SUPERHOTMCD'
        if (Test-Path (Join-Path $d 'SHMCD.exe')) { return $d }
    }
    return $null
}

function Find-Python {
    foreach ($c in @('py', 'python', 'python3')) {
        $cmd = Get-Command $c -ErrorAction SilentlyContinue
        if ($cmd) {
            $args0 = if ($c -eq 'py') { @('-3') } else { @() }
            try { & $cmd.Source @args0 -c "import sys; print(sys.version_info[0])" 2>$null | Out-Null; if ($LASTEXITCODE -eq 0) { return ,(@($cmd.Source) + $args0) } } catch {}
        }
    }
    return $null
}

function Confirm-Step([string]$q) {
    if ($Yes) { return $true }
    $a = Read-Host "$q [Y/n]"
    return ($a -eq '' -or $a -match '^[Yy]')
}

if (-not $GameDir) { $GameDir = Find-Game }
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'SHMCD.exe'))) {
    throw "Could not find SUPERHOT: MIND CONTROL DELETE. Run again with -GameDir `"<folder that contains SHMCD.exe>`"."
}
$data = Join-Path $GameDir 'SHMCD_Data'
$ggm = Join-Path $data 'globalgamemanagers'
$boot = Join-Path $data 'boot.config'
Write-Host "Game folder: $GameDir"
$running = Get-Process SHMCD -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -like (Join-Path $GameDir '*')) }
if ($running) { throw "The game is running. Close it first." }

if ($Uninstall) {
    if (Test-Path "$ggm.orig") { Copy-Item "$ggm.orig" $ggm -Force; Write-Host "Restored globalgamemanagers" }
    if (Test-Path "$boot.orig") { Copy-Item "$boot.orig" $boot -Force; Write-Host "Restored boot.config" }
    foreach ($p in @('BepInEx\plugins\SHMCDVR.dll', 'BepInEx\plugins\shmcdvr_input', 'BepInEx\plugins\shmcdvr_tools', 'SHMCD_Data\Plugins\openvr_api.dll')) {
        $t = Join-Path $GameDir $p
        if (Test-Path $t) { Remove-Item $t -Recurse -Force; Write-Host "Removed $p" }
    }
    Write-Host "SHMCD-VR removed. BepInEx was left in place (delete the BepInEx folder, winhttp.dll and doorstop_config.ini to remove it too)."
    return
}

# 1. BepInEx 5 (x86: the game is 32-bit)
if (-not (Test-Path (Join-Path $GameDir 'BepInEx\core\BepInEx.dll'))) {
    Write-Host "Installing BepInEx 5.4.23.6 (x86) from GitHub..."
    $zip = Join-Path $env:TEMP 'BepInEx_win_x86_5.4.23.6.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest $BepInExUrl -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $GameDir -Force
} else { Write-Host "BepInEx already installed." }

# 2. Mod files (plugin, controller bindings, screenshot tool, x86 OpenVR runtime for Unity)
Copy-Item (Join-Path $here 'files\*') $GameDir -Recurse -Force
Write-Host "Copied mod files."

# 3. boot.config: enable VR
if (-not (Test-Path "$boot.orig")) { Copy-Item $boot "$boot.orig" }
$lines = @(Get-Content $boot | Where-Object { $_ -notmatch '^vr-enabled=' }) + 'vr-enabled=1'
Set-Content $boot $lines -Encoding ASCII
Write-Host "boot.config: vr-enabled=1"

# 4. globalgamemanagers: BuildSettings.enabledVRDevices = [OpenVR] (needs Python + UnityPy)
$py = Find-Python
if (-not $py) { throw "Python 3 is required for the last step (https://www.python.org/downloads/). Install it and run this again." }
$exe = $py[0]; $pyArgs = @($py | Select-Object -Skip 1)
& $exe @pyArgs -c "import UnityPy" 2>$null
if ($LASTEXITCODE -ne 0) {
    if (-not (Confirm-Step "Install the UnityPy Python package ($UnityPyVersion) with pip?")) { throw "UnityPy is needed to enable VR in the game data." }
    & $exe @pyArgs -m pip install --user "UnityPy==$UnityPyVersion"
    if ($LASTEXITCODE -ne 0) { throw "pip install UnityPy failed." }
}
& $exe @pyArgs (Join-Path $here 'patch_ggm.py') $ggm OpenVR
if ($LASTEXITCODE -ne 0) { throw "Patching globalgamemanagers failed (original kept as globalgamemanagers.orig)." }

Write-Host ""
Write-Host "Done. Start SteamVR, then the game (from Steam or SHMCD.exe)."
Write-Host "Settings: $GameDir\BepInEx\config\shmcdvr.mod.cfg (created on first start)."
Write-Host "To undo: .\install.ps1 -Uninstall   (or verify the game files in Steam)."
