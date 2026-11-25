<#
.SYNOPSIS
    Runs the EUDORIA Unreal automation tests headless (no window, no rendering) and checks the report.
.DESCRIPTION
    Build first with .\tools\dev\build-unreal.ps1. The Eudoria.Integration tests need the core
    (.\tools\dev\start-db.ps1 then .\tools\dev\run-core.ps1); without it they are skipped with a warning.
    Fails (exit code 1) when a test fails or when no test ran.
.EXAMPLE
    .\tools\dev\test-unreal.ps1                       # every Eudoria.* test
    .\tools\dev\test-unreal.ps1 -Filter Eudoria.Core  # one category
#>
[CmdletBinding()]
param([string]$Filter = 'Eudoria')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Unreal.ps1')

Assert-EudoriaEditorClosed
$editorCmd = Get-EudoriaEnginePath 'Engine\Binaries\Win64\UnrealEditor-Cmd.exe'
$reportDir = Join-Path $script:RepoRoot 'unreal\Saved\Automation\Reports\Latest'
$logFile = Join-Path $script:RepoRoot 'unreal\Saved\Logs\EudoriaTests.log'
if (Test-Path -LiteralPath $reportDir) { Remove-Item -LiteralPath $reportDir -Recurse -Force }

& $editorCmd $script:UProject "-ExecCmds=Automation RunTests $Filter" '-TestExit=Automation Test Queue Empty' `
    "-ReportExportPath=$reportDir" '-unattended' '-nullrhi' '-nosplash' '-nosound' '-nop4' `
    "-abslog=$logFile" '-stdout' '-FullStdOutLogOutput' | Out-Null

$index = Join-Path $reportDir 'index.json'
if (-not (Test-Path -LiteralPath $index)) {
    throw "No test report ($index). See the log: $logFile"
}
# The report is UTF-8 with a BOM on some versions: read it as text first.
$report = Get-Content -LiteralPath $index -Raw -Encoding UTF8 | ConvertFrom-Json

foreach ($test in $report.tests) {
    $warnings = @($test.entries | Where-Object { $_.event.type -eq 'Warning' })
    Write-Host ('{0,-8} {1}' -f $test.state, $test.fullTestPath)
    foreach ($entry in @($test.entries | Where-Object { $_.event.type -in 'Error', 'Warning' })) {
        Write-Host ('           {0}: {1}' -f $entry.event.type, $entry.event.message)
    }
}
Write-Host ''
Write-Host ("Passed: {0}   Failed: {1}   With warnings: {2}   Not run: {3}" -f `
    $report.succeeded, $report.failed, $report.succeededWithWarnings, $report.notRun)

$ran = [int]$report.succeeded + [int]$report.succeededWithWarnings + [int]$report.failed
if ($ran -eq 0) { Write-Error "No test matched '$Filter'."; exit 1 }
if ([int]$report.failed -gt 0) { exit 1 }
exit 0
