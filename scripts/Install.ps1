[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [string]$LoaderSource,
    [string]$PluginPath = (Join-Path $PSScriptRoot '..\src\CraftingGrid\bin\Release\net472\CasualtiesUnknown.CraftingGrid.dll')
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GamePath).Path
$pluginFile = (Resolve-Path -LiteralPath $PluginPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'CasualtiesUnknown.exe'))) { throw 'GamePath must contain the retail CasualtiesUnknown.exe.' }
if (Get-Process -Name CasualtiesUnknown -ErrorAction SilentlyContinue) { throw 'Close Casualties: Unknown before installing.' }
$loaderRoot = Join-Path $gameRoot 'BepInEx'
$loaderAssembly = Join-Path $loaderRoot 'core\BepInEx.dll'
if (-not (Test-Path -LiteralPath $loaderAssembly)) {
    if (-not $LoaderSource) { throw 'BepInEx is absent. Supply -LoaderSource pointing to an existing local BepInEx 5 x64 Mono game installation. This script never downloads software.' }
    $sourceRoot = (Resolve-Path -LiteralPath $LoaderSource).Path
    $sourceCore = Join-Path $sourceRoot 'BepInEx\core'
    $sourceAssembly = Join-Path $sourceCore 'BepInEx.dll'
    if (([Reflection.AssemblyName]::GetAssemblyName($sourceAssembly)).Version.Major -ne 5) { throw 'Only a local BepInEx 5 Mono loader is supported by this prototype.' }
    if (Test-Path -LiteralPath (Join-Path $gameRoot 'winhttp.dll')) { throw 'An existing winhttp.dll requires manual inspection before adding a loader. Nothing was overwritten.' }
    foreach ($name in @('winhttp.dll','doorstop_config.ini')) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot $name))) { throw "LoaderSource is missing $name." }
    }
    New-Item -ItemType Directory -Path (Join-Path $loaderRoot 'core') -Force | Out-Null
    # Only the loader core and bootstrap are copied; plugin/patcher/updater folders are not imported.
    foreach ($file in Get-ChildItem -LiteralPath $sourceCore -File -Filter '*.dll') {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $loaderRoot ('core\'+$file.Name))
    }
    foreach ($name in @('winhttp.dll','doorstop_config.ini','.doorstop_version')) {
        $sourceFile = Join-Path $sourceRoot $name
        if (Test-Path -LiteralPath $sourceFile) { Copy-Item -LiteralPath $sourceFile -Destination (Join-Path $gameRoot $name) }
    }
}
if (([Reflection.AssemblyName]::GetAssemblyName($loaderAssembly)).Version.Major -ne 5) { throw 'The installed loader is not BepInEx 5.' }
$configRoot = Join-Path $loaderRoot 'config'
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
$coreConfig = Join-Path $configRoot 'BepInEx.cfg'
$configText = if (Test-Path -LiteralPath $coreConfig) { [IO.File]::ReadAllText($coreConfig) } else { '' }
# The retail startup destroys the default manager before the crafting hook can run.
# Use BepInEx's documented persistence setting; preserve other sections and settings.
$sectionRegex = [regex]::new('(?ms)^\[Chainloader\][^\r\n]*\r?\n(?<body>.*?)(?=^\[|\z)')
$section = $sectionRegex.Match($configText)
if ($section.Success) {
    $updatedSection = $section.Value
    if ($updatedSection -match '(?m)^\s*HideManagerGameObject\s*=') {
        $updatedSection = [regex]::Replace($updatedSection, '(?m)^\s*HideManagerGameObject\s*=[^\r\n]*', 'HideManagerGameObject = true')
    } else {
        $updatedSection = $updatedSection.TrimEnd()+"`r`nHideManagerGameObject = true`r`n`r`n"
    }
    $configText = $configText.Substring(0,$section.Index)+$updatedSection+$configText.Substring($section.Index+$section.Length)
} else {
    $configText = $configText.TrimEnd()+"`r`n`r`n[Chainloader]`r`nHideManagerGameObject = true`r`n"
}
if (Test-Path -LiteralPath $coreConfig) { Copy-Item -LiteralPath $coreConfig -Destination ($coreConfig+'.previous') -Force }
[IO.File]::WriteAllText($coreConfig,$configText,[Text.UTF8Encoding]::new($false))
Write-Output 'Configured BepInEx manager persistence (HideManagerGameObject = true).'
$destinationRoot = Join-Path $loaderRoot 'plugins\CraftingGrid'
New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
$destinationFile = Join-Path $destinationRoot 'CasualtiesUnknown.CraftingGrid.dll'
if (Test-Path -LiteralPath $destinationFile) { Copy-Item -LiteralPath $destinationFile -Destination ($destinationFile+'.previous') -Force }
Copy-Item -LiteralPath $pluginFile -Destination $destinationFile -Force
Write-Output "Installed $destinationFile"
Write-Output 'Start the retail game through Steam, load a run, and press the normal crafting key (default C).'
