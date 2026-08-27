# ShadowDungeonTrainer build & deploy for BepInEx-Manager isolated profile.
# Usage: powershell -File build.ps1 [-GameDir <game dir>]
# Resolves the profile BepInEx root from doorstop_config.ini (target_assembly),
# builds with -p:BepDir -p:GameManaged and deploys the dll into <profile>/BepInEx/plugins/.
param(
    [string]$GameDir = 'E:\steam\steamapps\common\Shadow Dungeon'
)
$ErrorActionPreference = 'Stop'
$Proj = $PSScriptRoot
$Runtime = 'net472'

# 1) Locate profile BepInEx root from doorstop target_assembly
$doorstop = Join-Path $GameDir 'doorstop_config.ini'
if (-not (Test-Path $doorstop)) { Write-Host "doorstop_config.ini not found: $doorstop"; exit 1 }
$m = Select-String -Path $doorstop -Pattern '^\s*target_assembly\s*=\s*(.+)$' | Select-Object -First 1
if (-not $m) { Write-Host 'target_assembly not found in doorstop_config.ini'; exit 1 }
$target = $m.Matches[0].Groups[1].Value.Trim()
# target = <profile>/BepInEx/core/BepInEx.Preloader.dll  ->  BepInEx root is two levels up
$bepDir = Split-Path (Split-Path $target -Parent) -Parent
if (-not (Test-Path (Join-Path $bepDir 'core'))) { Write-Host "Invalid profile BepInEx dir: $bepDir"; exit 1 }
Write-Host "Profile BepInEx: $bepDir"

# 2) Game Managed directory
$gameManaged = Join-Path $GameDir 'Shadow Dungeon_Data\Managed'
if (-not (Test-Path (Join-Path $gameManaged 'Assembly-CSharp.dll'))) { Write-Host "Assembly-CSharp.dll not found: $gameManaged"; exit 1 }
Write-Host "Game Managed: $gameManaged"

# 3) Build
dotnet build "$Proj\src\ShadowDungeonTrainer\ShadowDungeonTrainer.csproj" -c Release -p:BepDir="$bepDir" -p:GameManaged="$gameManaged"
if ($LASTEXITCODE -ne 0) { Write-Host 'BUILD FAILED'; exit 1 }

# 4) Deploy into profile plugins
$dst = Join-Path $bepDir 'plugins\ShadowDungeonTrainer.dll'
Copy-Item "$Proj\src\ShadowDungeonTrainer\bin\Release\$Runtime\ShadowDungeonTrainer.dll" $dst -Force
Write-Host "Deployed: $dst"
Write-Host "Done! Restart game to load the trainer."
