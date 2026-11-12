param([string]$GameDir = "T:\SHMCD-VR\game", [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $GameDir "SHMCD_Data\Managed"
$core = Join-Path $GameDir "BepInEx\core"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root "build"
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @(
    "mscorlib", "System", "System.Core",
    "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.VRModule", "UnityEngine.XRModule", "UnityEngine.InputModule",
    "UnityEngine.InputLegacyModule", "UnityEngine.AnimationModule", "UnityEngine.PhysicsModule", "UnityEngine.ImageConversionModule", "UnityEngine.IMGUIModule", "UnityEngine.UIModule", "UnityEngine.UI",
    "Assembly-CSharp", "Assembly-CSharp-firstpass"
) | ForEach-Object { Join-Path $managed "$_.dll" } | Where-Object { Test-Path $_ }
$refs += "$core\BepInEx.dll", "$core\0Harmony.dll"
$src = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
$src += Join-Path $root "deps\openvr\openvr_api.cs"   # Valve's C# bindings (v1.14.15); native x86 openvr_api.dll is in SHMCD_Data\Plugins

& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug- /warn:4 "/out:$out\SHMCDVR.dll" ($refs | ForEach-Object { "/r:$_" }) $src
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host "Built $out\SHMCDVR.dll"
# Steam / SteamVR VR-entry launcher (x64, AnyCPU) and the shortcuts.vdf tool
& $csc /nologo /target:winexe /optimize+ "/out:$out\shmcdvr-launch.exe" (Join-Path $root "launcher\ShmcdVrLaunch.cs") (Join-Path $root "deps\openvr\openvr_api.cs")
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed" }
& $csc /nologo /optimize+ "/out:$out\SteamShortcut.exe" (Join-Path $root "launcher\SteamShortcut.cs")
if ($LASTEXITCODE -ne 0) { throw "SteamShortcut build failed" }
Copy-Item (Join-Path $root "_tools\vrprobe\openvr_api.dll") (Join-Path $out "openvr_api.dll") -Force   # x64, for the launcher
if ($Install) {
    $p = Join-Path $GameDir "BepInEx\plugins"; New-Item -ItemType Directory -Force $p | Out-Null
    Copy-Item "$out\SHMCDVR.dll" $p -Force
    $in = Join-Path $p "shmcdvr_input"; New-Item -ItemType Directory -Force $in | Out-Null
    Copy-Item (Join-Path $root "input\*.json") $in -Force
    Write-Host "Installed to $p"
}
