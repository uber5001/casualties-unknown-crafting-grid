[CmdletBinding()]
param([string]$Version = '0.4.0')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$pluginFile = Join-Path $repoRoot 'src\CraftingGrid\bin\Release\net472\CasualtiesUnknown.CraftingGrid.dll'
if (-not (Test-Path -LiteralPath $pluginFile)) { throw 'Build the Release plugin before packaging.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have the form major.minor.patch.' }
if ([Reflection.AssemblyName]::GetAssemblyName($pluginFile).Version -ne [version]($Version+'.0')) { throw 'Requested package version does not match the built DLL.' }
$artifactRoot = Join-Path $repoRoot 'artifacts'
$stageRoot = Join-Path $artifactRoot ('CraftingGrid-'+$Version+'-release')
if (Test-Path -LiteralPath $stageRoot) {
    $resolvedStage = (Resolve-Path -LiteralPath $stageRoot).Path
    if (-not $resolvedStage.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Staging folder escapes artifacts.' }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
$pluginDestination = Join-Path $stageRoot 'BepInEx\plugins\CraftingGrid'
New-Item -ItemType Directory -Path $pluginDestination -Force | Out-Null
Copy-Item -LiteralPath $pluginFile -Destination $pluginDestination
foreach ($name in @('LICENSE','NOTICE')) { Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $stageRoot }
Copy-Item -LiteralPath (Join-Path $repoRoot 'release\README.txt') -Destination $stageRoot
$zipFile = Join-Path $artifactRoot ('CraftingGrid-'+$Version+'.zip')
Compress-Archive -LiteralPath (Join-Path $stageRoot 'BepInEx'),(Join-Path $stageRoot 'README.txt'),(Join-Path $stageRoot 'LICENSE'),(Join-Path $stageRoot 'NOTICE') -DestinationPath $zipFile -Force
$zipHash = (Get-FileHash -LiteralPath $zipFile -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumFile = Join-Path $artifactRoot ('CraftingGrid-'+$Version+'.sha256')
[IO.File]::WriteAllText($checksumFile,$zipHash+'  '+[IO.Path]::GetFileName($zipFile)+"`n",[Text.UTF8Encoding]::new($false))
Write-Output $zipFile
Write-Output ($zipHash+'  '+[IO.Path]::GetFileName($zipFile))
