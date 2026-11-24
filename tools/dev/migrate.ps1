<#
.SYNOPSIS
    Applies the pending database migrations of the EUDORIA core to the local database.
.DESCRIPTION
    Migrations are applied on purpose by this command, never automatically when the core starts.
    The connection string is built in this process's memory only (from .env): nothing is written
    to disk or passed on a command line. Start the database first with .\tools\dev\start-db.ps1.
    Modules with migrations today: Identity (PostgreSQL schema "identity").
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DotEnv.ps1')

Set-EudoriaConnectionString (Read-EudoriaDotEnv)

Push-Location $script:RepoRoot
try {
    # dotnet-ef is a local tool of the repository (.config/dotnet-tools.json), pinned to one version.
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    foreach ($module in @('core\src\Eudoria.Core.Identity')) {
        Write-Host "Applying migrations of $module..."
        & dotnet ef database update --project $module
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}
