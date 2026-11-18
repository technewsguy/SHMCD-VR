# Builds SHMCD-VR into .\build:
#   SHMCDVR.dll (the BepInEx 5 plugin) and SHMCD-VR-Setup.exe (the installer, with BepInEx and the mod embedded).
# -GameDir: a copy of the game with BepInEx 5 (x86) installed, for the plugin's references.
# -Install: also copies the plugin and controller bindings into -GameDir (for testing).
param([string]$GameDir = "T:\SHMCD-VR\game", [switch]$Install)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $GameDir "SHMCD_Data\Managed"
$core = Join-Path $GameDir "BepInEx\core"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out = Join-Path $root "build"
New-Item -ItemType Directory -Force $out | Out-Null

# BepInEx 5 x86 (the game is 32-bit), embedded in the installer.
$bepZip = Join-Path $root "deps\bepinex\BepInEx_win_x86_5.4.23.6.zip"
if (-not (Test-Path $bepZip)) {
    New-Item -ItemType Directory -Force (Split-Path $bepZip) | Out-Null
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x86_5.4.23.6.zip' -OutFile $bepZip -UseBasicParsing
}

# Plugin (net35 / Mono 2.0, against the game's own assemblies)
$refs = @(
    "mscorlib", "System", "System.Core",
    "UnityEngine", "UnityEngine.CoreModule", "UnityEngine.VRModule", "UnityEngine.XRModule", "UnityEngine.InputModule",
    "UnityEngine.InputLegacyModule", "UnityEngine.AnimationModule", "UnityEngine.PhysicsModule", "UnityEngine.ImageConversionModule", "UnityEngine.IMGUIModule", "UnityEngine.UIModule", "UnityEngine.UI",
    "Assembly-CSharp", "Assembly-CSharp-firstpass"
) | ForEach-Object { Join-Path $managed "$_.dll" } | Where-Object { Test-Path $_ }
$refs += "$core\BepInEx.dll", "$core\0Harmony.dll"
$src = @(Get-ChildItem (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
$src += Join-Path $root "deps\openvr\openvr_api.cs"        # Valve's C# bindings (v1.14.15); native x86 openvr_api.dll goes in SHMCD_Data\Plugins
$src += Join-Path $root "installer\SteamVRTag.cs"         # shared with the installer
& $csc /nologo /noconfig /nostdlib+ /target:library /optimize+ /debug- /warn:4 "/out:$out\SHMCDVR.dll" ($refs | ForEach-Object { "/r:$_" }) $src
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }
Write-Host "Built $out\SHMCDVR.dll"

$version = [regex]::Match((Get-Content (Join-Path $root "src\Plugin.cs") -Raw), 'BepInPlugin\("[^"]+",\s*"[^"]+",\s*"([^"]+)"\)').Groups[1].Value

# Installer payload: files laid out like the game folder, embedded as "payload/<path>".
$payload = Join-Path $out "payload"
Remove-Item $payload -Recurse -Force -ErrorAction SilentlyContinue
$plug = Join-Path $payload "BepInEx\plugins"
New-Item -ItemType Directory -Force (Join-Path $plug "shmcdvr_input"), (Join-Path $plug "shmcdvr_tools"), (Join-Path $payload "SHMCD_Data\Plugins") | Out-Null
Copy-Item "$out\SHMCDVR.dll" $plug
Copy-Item (Join-Path $root "input\*.json") (Join-Path $plug "shmcdvr_input")
Copy-Item (Join-Path $root "_tools\vrprobe\VrProbe.exe"), (Join-Path $root "_tools\vrprobe\openvr_api.dll") (Join-Path $plug "shmcdvr_tools")   # F12 stereo screenshots
Copy-Item (Join-Path $root "deps\openvr-win32\openvr_api.dll") (Join-Path $payload "SHMCD_Data\Plugins")
$resArgs = @("/resource:$bepZip,bepinex.zip")
$resArgs += Get-ChildItem $payload -Recurse -File | ForEach-Object { "/resource:$($_.FullName),payload/" + $_.FullName.Substring($payload.Length + 1).Replace('\', '/') }
"static partial class ShmcdVrSetup { internal const string ModVersion = `"$version`"; }" | Set-Content "$out\SetupVersion.cs" -Encoding ASCII
@"
using System.Reflection;
[assembly: AssemblyTitle("SHMCD-VR $version Setup")]
[assembly: AssemblyProduct("SHMCD-VR")]
[assembly: AssemblyDescription("Installs the SHMCD-VR mod for SUPERHOT: MIND CONTROL DELETE")]
[assembly: AssemblyFileVersion("$version.0")]
"@ | Set-Content "$out\SetupAssemblyInfo.cs" -Encoding ASCII
& $csc /nologo /target:winexe /optimize+ /main:ShmcdVrSetup /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    "/out:$out\SHMCD-VR-Setup.exe" `
    (Join-Path $root "installer\Setup.cs") (Join-Path $root "installer\SteamVRTag.cs") (Join-Path $root "installer\GgmPatch.cs") "$out\SetupVersion.cs" "$out\SetupAssemblyInfo.cs" $resArgs
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Write-Host "Built $out\SHMCD-VR-Setup.exe ($([math]::Round((Get-Item "$out\SHMCD-VR-Setup.exe").Length / 1MB, 1)) MB)"

if ($Install) {
    $p = Join-Path $GameDir "BepInEx\plugins"; New-Item -ItemType Directory -Force $p | Out-Null
    Copy-Item "$out\SHMCDVR.dll" $p -Force
    $in = Join-Path $p "shmcdvr_input"; New-Item -ItemType Directory -Force $in | Out-Null
    Copy-Item (Join-Path $root "input\*.json") $in -Force
    Write-Host "Installed to $p"
}
