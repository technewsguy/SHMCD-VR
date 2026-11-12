# Builds the plugin and packages a release zip in dist\ (install.ps1 + files laid out like the game folder).
param([string]$GameDir = "T:\SHMCD-VR\game")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
& (Join-Path $root 'build.ps1') -GameDir $GameDir
$ver = ([regex]'BepInPlugin\("[^"]+",\s*"[^"]+",\s*"([^"]+)"\)').Match((Get-Content (Join-Path $root 'src\Plugin.cs') -Raw)).Groups[1].Value
$name = "SHMCD-VR-v$ver"
$stage = Join-Path $root "dist\$name"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$files = Join-Path $stage 'files'
$plug = Join-Path $files 'BepInEx\plugins'
New-Item -ItemType Directory -Force (Join-Path $plug 'shmcdvr_input'), (Join-Path $plug 'shmcdvr_tools'), (Join-Path $files 'SHMCD_Data\Plugins') | Out-Null
Copy-Item (Join-Path $root 'build\SHMCDVR.dll') $plug
Copy-Item (Join-Path $root 'input\*.json') (Join-Path $plug 'shmcdvr_input')
Copy-Item (Join-Path $root '_tools\vrprobe\VrProbe.exe'), (Join-Path $root '_tools\vrprobe\openvr_api.dll') (Join-Path $plug 'shmcdvr_tools')
Copy-Item (Join-Path $root 'deps\openvr-win32\openvr_api.dll') (Join-Path $files 'SHMCD_Data\Plugins')
Copy-Item (Join-Path $root 'install\*') $stage
# Optional: Steam / SteamVR library entry with artwork (add-steam-vr-shortcut.ps1 -GameDir ... -BuildDir .\steam-entry)
$se = Join-Path $stage 'steam-entry'; New-Item -ItemType Directory -Force $se | Out-Null
Copy-Item (Join-Path $root 'build\shmcdvr-launch.exe'), (Join-Path $root 'build\SteamShortcut.exe'), (Join-Path $root 'build\openvr_api.dll'), (Join-Path $root 'launcher\add-steam-vr-shortcut.ps1') $se
Copy-Item (Join-Path $root '_tools\patch_ggm.py') $stage
Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY.md') $stage
$zip = Join-Path $root "dist\$name.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Packaged $zip"
