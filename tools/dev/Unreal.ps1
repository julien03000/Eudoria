# EUDORIA — shared helpers for the Unreal dev scripts (dot-sourced, not run directly).

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:UProject = Join-Path $script:RepoRoot 'unreal\Eudoria.uproject'

# Engine root: the Epic launcher install (UE 5.8, ADR-0003), unless EUDORIA_UE_ROOT overrides it.
$script:EngineRoot = if ($env:EUDORIA_UE_ROOT) { $env:EUDORIA_UE_ROOT } else { 'E:\Epic Games\UE_5.8' }

function Get-EudoriaEnginePath([string]$relativePath) {
    $path = Join-Path $script:EngineRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Unreal Engine file not found: $path. Install UE 5.8 there, or set EUDORIA_UE_ROOT."
    }
    return $path
}

# Building while the editor runs makes Unreal hot-reload (or Live Coding) the modules: forbidden here,
# and two heavy processes at once risk running out of memory on a 16 GB PC.
function Assert-EudoriaEditorClosed {
    $running = Get-Process -Name 'UnrealEditor', 'UnrealEditor-Cmd' -ErrorAction SilentlyContinue
    if ($running) {
        throw "The Unreal editor is running (PID $($running.Id -join ', ')). Close it, then build again."
    }
}
