<#
.SYNOPSIS
    Puts the render test content (sub-step 1.7) in place, or says exactly what is missing.
.DESCRIPTION
    This content is kept out of git (plan 1.7, decision D1). The list is tools/dev/render-test-content.json;
    the human-readable recipe is docs/render-test/assets.md.
    - Mannequin: copied from the engine install (no download), only the files listed in the manifest
      (no Animation Blueprint, no Control Rig: plan 1.7, decision D3).
    - Fab assets: added by hand in the editor; this script only checks that they are there.
    - The test map: built by unreal/Scripts/build_render_test_map.py (-BuildMap), always the same scene.
    Exit code 0 when everything is present (and, with -VerifyReferences, no hard reference is missing),
    1 otherwise.
.EXAMPLE
    .\tools\dev\setup-render-test.ps1                     # copy the mannequin, check the Fab assets and the map
    .\tools\dev\setup-render-test.ps1 -BuildMap           # (re)build the test map (editor closed, a few minutes)
    .\tools\dev\setup-render-test.ps1 -VerifyReferences   # also ask the editor for missing references (editor closed, ~1-3 min)
#>
[CmdletBinding()]
param(
    [switch]$BuildMap,
    [switch]$VerifyReferences,
    # Download cache of the Fab plugin. Its default is %LOCALAPPDATA%\Temp\FabLibrary (on C:): the
    # "everything on E:" rule moves it. The setting is per project and per user (Saved/, not versioned).
    [string]$FabCacheDirectory = 'E:\Dev\FabCache'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Unreal.ps1')

$manifestPath = Join-Path $PSScriptRoot 'render-test-content.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$problems = New-Object System.Collections.Generic.List[string]

function Format-Size([long]$bytes) { '{0:N1} MB' -f ($bytes / 1MB) }

# Runs one of our editor Python scripts headless and returns the JSON it writes in Saved/RenderTest
# (the commandlet's exit code says nothing about the script itself).
function Invoke-EudoriaEditorPython([string]$scriptName, [string]$resultName) {
    Assert-EudoriaEditorClosed
    $editorCmd = Get-EudoriaEnginePath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
    # Forward slashes: the editor's command line reads "\r", "\n"... in a path as escape sequences.
    $scriptPath = (Join-Path $script:RepoRoot "unreal\Scripts\$scriptName") -replace '\\', '/'
    $resultFile = Join-Path $script:RepoRoot "unreal\Saved\RenderTest\$resultName"
    $logFile = Join-Path $script:RepoRoot ("unreal\Saved\Logs\EudoriaRenderTest-{0}.log" -f [IO.Path]::GetFileNameWithoutExtension($scriptName))
    if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile -Force }
    & $editorCmd $script:UProject '-run=pythonscript' "-script=$scriptPath" '-unattended' '-nullrhi' '-nosplash' `
        '-nosound' '-nop4' "-abslog=$logFile" | Out-Null
    if (-not (Test-Path -LiteralPath $resultFile)) {
        $problems.Add("$scriptName produced no result ($resultFile). See the log: $logFile")
        return $null
    }
    return Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
}

# --- Mannequin: copy from the engine -------------------------------------------------------------
$sourceRoot = Get-EudoriaEnginePath $manifest.mannequin.source
$destinationRoot = Join-Path $script:RepoRoot $manifest.mannequin.destination
$copied = 0
$total = 0L
foreach ($relative in $manifest.mannequin.files) {
    $source = Join-Path $sourceRoot $relative
    $destination = Join-Path $destinationRoot $relative
    if (-not (Test-Path -LiteralPath $source)) {
        $problems.Add("Mannequin file missing from the engine: $source")
        continue
    }
    $sourceItem = Get-Item -LiteralPath $source
    $total += $sourceItem.Length
    $upToDate = (Test-Path -LiteralPath $destination) -and ((Get-Item -LiteralPath $destination).Length -eq $sourceItem.Length)
    if (-not $upToDate) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
        $copied++
    }
}
Write-Host ("Mannequin: {0} files ({1}), {2} copied, {3} already in place -> {4}" -f `
    @($manifest.mannequin.files).Count, (Format-Size $total), $copied, (@($manifest.mannequin.files).Count - $copied), $destinationRoot)

# --- Fab plugin: download cache on E: --------------------------------------------------------------
# UFabSettings (config = EditorPerProjectUserSettings), property CacheDirectoryPath (an FDirectoryPath),
# read from the Fab 0.0.23 plugin. The editor rewrites this file when it closes: it must be closed now.
function Set-EudoriaFabCacheDirectory([string]$directory) {
    Assert-EudoriaEditorClosed
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $iniPath = Join-Path $script:RepoRoot 'unreal\Saved\Config\WindowsEditor\EditorPerProjectUserSettings.ini'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $iniPath) | Out-Null
    $section = '[/Script/Fab.FabSettings]'
    $line = 'CacheDirectoryPath=(Path="{0}")' -f ($directory -replace '\\', '/')
    $lines = if (Test-Path -LiteralPath $iniPath) { @(Get-Content -LiteralPath $iniPath -Encoding UTF8) } else { @() }

    $start = [array]::IndexOf($lines, $section)
    if ($start -lt 0) {
        $lines += @('', $section, $line)
    } else {
        $end = $start + 1
        while ($end -lt $lines.Count -and -not $lines[$end].StartsWith('[')) { $end++ }
        $kept = @($lines[($start + 1)..($end - 1)] | Where-Object { $_ -notmatch '^CacheDirectoryPath=' -and $_ -ne '' })
        $before = if ($start -gt 0) { $lines[0..($start - 1)] } else { @() }
        $after = if ($end -lt $lines.Count) { $lines[$end..($lines.Count - 1)] } else { @() }
        $lines = @($before) + @($section, $line) + $kept + @('') + @($after)
    }
    # UTF-8 without BOM, like the files the editor writes.
    [IO.File]::WriteAllLines($iniPath, [string[]]$lines, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Fab cache: $directory (written to $iniPath)"
}
Set-EudoriaFabCacheDirectory $FabCacheDirectory

# --- Fab assets: check only ----------------------------------------------------------------------
$fab = @($manifest.fab)
if ($fab.Count -eq 0) {
    $problems.Add('No Fab asset is listed yet in render-test-content.json (filled at checkpoint A of plan 1.7).')
}
foreach ($asset in $fab) {
    $folder = Join-Path $script:RepoRoot (Join-Path 'unreal\Content' $asset.folder)
    if (-not (Test-Path -LiteralPath $folder)) {
        $problems.Add("Fab asset missing: '$($asset.name)' (expected in $folder). Add it from the Fab window of the editor: $($asset.url)")
        continue
    }
    $size = (Get-ChildItem -LiteralPath $folder -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ("Fab: {0} present ({1})" -f $asset.name, (Format-Size $size))

    # Megascans assets: exactly one quality tier, the one of the manifest (the Fab plugin keeps the
    # previous tier beside a new import: plan 1.7, Raw tiers removed on 2026-11-27).
    if ($asset.folder -like 'Fab/Megascans/*') {
        $tiers = @(Get-ChildItem -LiteralPath $folder -Directory | ForEach-Object Name)
        if ($tiers.Count -ne 1 -or $tiers[0] -ne $manifest.fabQualityTier) {
            $problems.Add("Fab asset '$($asset.name)': quality tiers [$($tiers -join ', ')], expected only '$($manifest.fabQualityTier)'. Remove the extra tier with unreal/Scripts/remove_fab_quality_tier.py.")
        }
    }
}

# No 8K texture: they are the "Raw" tier (about 4 times the video memory of a 4K one).
$oversized = @(Get-ChildItem -LiteralPath (Join-Path $script:RepoRoot 'unreal\Content') -Recurse -File -Filter '*_8K_*')
foreach ($file in $oversized) { $problems.Add("8K texture found: $($file.FullName)") }

# --- The test map ----------------------------------------------------------------------------------
$renderTestFolder = Join-Path $script:RepoRoot 'unreal\Content\RenderTest'
if ($BuildMap) {
    if ($problems.Count -gt 0) {
        $problems.Add('Test map not built: the content above is incomplete.')
    } else {
        # The whole folder is generated (map + floor material) and out of git: start from nothing. The
        # editor cannot replace a map that is already on disk (UE 5.8: "an asset already exists").
        Assert-EudoriaEditorClosed
        if (Test-Path -LiteralPath $renderTestFolder) { Remove-Item -LiteralPath $renderTestFolder -Recurse -Force }
        Write-Host 'Building the test map (headless, a few minutes)...'
        $build = Invoke-EudoriaEditorPython 'build_render_test_map.py' 'build-map.json'
        $failed = (-not $build) -or @($build.errors).Count -gt 0
        if ($build) {
            $counts = ($build.counts.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value)" }) -join ', '
            Write-Host "Test map: $($build.map) (seed $($build.seed)): $counts"
            foreach ($buildError in @($build.errors)) { $problems.Add("Test map: $buildError") }
        }
        # A half-built scene must never pass for the test scene.
        if ($failed -and (Test-Path -LiteralPath $renderTestFolder)) { Remove-Item -LiteralPath $renderTestFolder -Recurse -Force }
    }
}
$mapFile = Join-Path $renderTestFolder 'L_RenderTest.umap'
if (Test-Path -LiteralPath $mapFile) {
    Write-Host ("Test map present ({0})" -f (Format-Size (Get-Item -LiteralPath $mapFile).Length))
} else {
    $problems.Add('Test map missing: run .\tools\dev\setup-render-test.ps1 -BuildMap (editor closed).')
}

# --- Optional: missing references, asked to the editor -------------------------------------------
if ($VerifyReferences) {
    Write-Host 'Asking the editor for missing references (headless)...'
    $check = Invoke-EudoriaEditorPython 'check_render_test_content.py' 'content-check.json'
    if ($check) {
        Write-Host ("References: {0} packages checked, {1} hard and {2} soft references missing" -f `
            $check.checkedPackages, @($check.missingHard).Count, @($check.missingSoft).Count)
        foreach ($entry in @($check.missingHard)) { $problems.Add("Missing hard reference: $($entry.asset) -> $($entry.missing)") }
        foreach ($entry in @($check.missingSoft)) { Write-Host "  (soft, not blocking) $($entry.asset) -> $($entry.missing)" }
        # Plan 1.7, § 6.2: every Fab mesh must be Nanite (the point of the render test).
        foreach ($mesh in @($check.staticMeshes)) {
            Write-Host ("  mesh {0}: Nanite={1}, triangles={2}" -f $mesh.mesh, $mesh.naniteEnabled, $mesh.triangles)
            if ($mesh.mesh.StartsWith('/Game/Fab/') -and $mesh.naniteEnabled -ne 'True') {
                $problems.Add("Fab mesh without Nanite: $($mesh.mesh) (NaniteEnabled=$($mesh.naniteEnabled))")
            }
        }
    }
}

Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host 'Render test content is INCOMPLETE:'
    foreach ($problem in $problems) { Write-Host "  - $problem" }
    exit 1
}
Write-Host 'Render test content is complete.'
exit 0
