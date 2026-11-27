<#
.SYNOPSIS
    Starts (or stops) the local PostgreSQL database of EUDORIA in Docker.
.EXAMPLE
    .\tools\dev\start-db.ps1          # start and wait until the database is healthy
    .\tools\dev\start-db.ps1 -Stop    # stop (data is kept in the Docker volume)
#>
[CmdletBinding()]
param([switch]$Stop)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DotEnv.ps1')

$null = Read-EudoriaDotEnv   # fails early with a clear message if .env is missing or incomplete

$composeArgs = @('compose', '--env-file', $script:EnvFile, '-f', $script:ComposeFile)
$composeArgs += if ($Stop) { @('stop') } else { @('up', '--detach', '--wait') }

# Docker Compose writes its normal progress ("Container ... Running") to stderr. When the caller
# redirects this script's output (e.g. `*>`), Windows PowerShell 5.1 turns each stderr line into an
# error record, which 'Stop' would treat as fatal. So stderr is merged into plain text here, with
# 'Continue' scoped to the docker call only; failure is decided by the exit code alone.
$ErrorActionPreference = 'Continue'
try {
    & docker @composeArgs 2>&1 | ForEach-Object { Write-Host "$_" }
} finally {
    $ErrorActionPreference = 'Stop'
}
if ($LASTEXITCODE -ne 0) { throw "docker compose failed (exit code $LASTEXITCODE). Is Docker Desktop running?" }
