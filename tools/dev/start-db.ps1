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
if ($Stop) {
    & docker @composeArgs stop
} else {
    & docker @composeArgs up --detach --wait
}
if ($LASTEXITCODE -ne 0) { throw "docker compose failed (exit code $LASTEXITCODE). Is Docker Desktop running?" }
