<#
.SYNOPSIS
    Puts the render test content (sub-step 1.7) in place, or says exactly what is missing.
.DESCRIPTION
    This content is kept out of git (plan 1.7, decision D1). The list is tools/dev/render-test-content.json;
    the human-readable recipe is docs/render-test/assets.md.
    - Mannequin: copied from the engine install (no download), only the files listed in the manifest
      (no Animation Blueprint, no Control Rig: plan 1.7, decision D3).
    - Fab assets: added by hand in the editor; this script only checks that they are there.
    Exit code 0 when everything is present (and, with -VerifyReferences, no hard reference is missing),
    1 otherwise.
.EXAMPLE
    .\tools\dev\setup-render-test.ps1                     # copy the mannequin, check the Fab assets
    .\tools\dev\setup-render-test.ps1 -VerifyReferences   # also ask the editor for missing references (editor closed, ~1-3 min)
#>
[CmdletBinding()]
param([switch]$VerifyReferences)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Unreal.ps1')

$manifestPath = Join-Path $PSScriptRoot 'render-test-content.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$problems = New-Object System.Collections.Generic.List[string]

function Format-Size([long]$bytes) { '{0:N1} MB' -f ($bytes / 1MB) }

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
}

# --- Optional: missing references, asked to the editor -------------------------------------------
if ($VerifyReferences) {
    Assert-EudoriaEditorClosed
    $editorCmd = Get-EudoriaEnginePath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
    $script = Join-Path $script:RepoRoot 'unreal\Scripts\check_render_test_content.py'
    $resultFile = Join-Path $script:RepoRoot 'unreal\Saved\RenderTest\content-check.json'
    $logFile = Join-Path $script:RepoRoot 'unreal\Saved\Logs\EudoriaRenderTestContent.log'
    if (Test-Path -LiteralPath $resultFile) { Remove-Item -LiteralPath $resultFile -Force }

    Write-Host 'Asking the editor for missing references (headless)...'
    & $editorCmd $script:UProject '-run=pythonscript' "-script=$script" '-unattended' '-nullrhi' '-nosplash' `
        '-nosound' '-nop4' "-abslog=$logFile" | Out-Null

    if (-not (Test-Path -LiteralPath $resultFile)) {
        $problems.Add("The reference check produced no result ($resultFile). See the log: $logFile")
    } else {
        $check = Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
        Write-Host ("References: {0} packages checked, {1} hard and {2} soft references missing" -f `
            $check.checkedPackages, @($check.missingHard).Count, @($check.missingSoft).Count)
        foreach ($entry in @($check.missingHard)) { $problems.Add("Missing hard reference: $($entry.asset) -> $($entry.missing)") }
        foreach ($entry in @($check.missingSoft)) { Write-Host "  (soft, not blocking) $($entry.asset) -> $($entry.missing)" }
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
