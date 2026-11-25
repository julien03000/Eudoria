<#
.SYNOPSIS
    Launches the EUDORIA PC client (development build, in a window) on the login screen.
.DESCRIPTION
    Build first with .\tools\dev\build-unreal.ps1, and start the core (.\tools\dev\start-db.ps1, then
    .\tools\dev\run-core.ps1). The client runs from the editor binaries in "game" mode: no editor window.
    Log: unreal\Saved\Logs\EudoriaClient.log
.EXAMPLE
    .\tools\dev\run-client.ps1
    .\tools\dev\run-client.ps1 -Width 1920 -Height 1080
#>
[CmdletBinding()]
param(
    [int]$Width = 1280,
    [int]$Height = 720
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Unreal.ps1')

$editor = Get-EudoriaEnginePath 'Engine\Binaries\Win64\UnrealEditor.exe'
$logFile = Join-Path $script:RepoRoot 'unreal\Saved\Logs\EudoriaClient.log'

$arguments = @("`"$script:UProject`"", '-game', '-windowed', "-ResX=$Width", "-ResY=$Height", "`"-abslog=$logFile`"")
$process = Start-Process -FilePath $editor -ArgumentList $arguments -PassThru
Write-Host "Client started (PID $($process.Id)). Log: $logFile"
