<#
.SYNOPSIS
    Runs the EUDORIA core locally, configured from the repository's .env file.
.DESCRIPTION
    The connection string and the token signing key are set in this process's memory only:
    nothing is written to disk. Start the database first with .\tools\dev\start-db.ps1, and
    generate the signing key once with .\tools\dev\new-signing-key.ps1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DotEnv.ps1')

$envValues = Read-EudoriaDotEnv

Set-EudoriaConnectionString $envValues
Set-EudoriaTokenSigningKey $envValues
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$($envValues['CORE_HTTP_PORT'])"

$project = Join-Path $script:RepoRoot 'core\src\Eudoria.Core.Api'
& dotnet run --project $project --no-launch-profile
exit $LASTEXITCODE
