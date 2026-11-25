<#
.SYNOPSIS
    Builds the EUDORIA Unreal editor target (our C++ modules only), with the editor closed.
.DESCRIPTION
    Uses the engine's Build.bat (Unreal Build Tool). The parallelism limits of
    %APPDATA%\Unreal Engine\UnrealBuildTool\BuildConfiguration.xml apply (16 GB of RAM).
.EXAMPLE
    .\tools\dev\build-unreal.ps1                # build EudoriaEditor Win64 Development
    .\tools\dev\build-unreal.ps1 -ProjectFiles  # also (re)generate the Visual Studio solution unreal\Eudoria.sln
#>
[CmdletBinding()]
param([switch]$ProjectFiles)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Unreal.ps1')

Assert-EudoriaEditorClosed
$buildBat = Get-EudoriaEnginePath 'Engine\Build\BatchFiles\Build.bat'

if ($ProjectFiles) {
    & $buildBat -ProjectFiles "-Project=$script:UProject" -Game -Progress
    if ($LASTEXITCODE -ne 0) { throw "Project file generation failed (exit code $LASTEXITCODE)." }
}

& $buildBat EudoriaEditor Win64 Development "-Project=$script:UProject" -WaitMutex -NoHotReloadFromIDE
if ($LASTEXITCODE -ne 0) { throw "Unreal build failed (exit code $LASTEXITCODE)." }
