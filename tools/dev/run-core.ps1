<#
.SYNOPSIS
    Runs the EUDORIA core locally, configured from the repository's .env file.
.DESCRIPTION
    The connection string and the token signing key are set in this process's memory only:
    nothing is written to disk. Start the database first with .\tools\dev\start-db.ps1, and
    generate the signing key once with .\tools\dev\new-signing-key.ps1.
    The core listens over HTTPS only (https://localhost:<CORE_HTTPS_PORT>), with the .NET development
    certificate, which must be trusted once: dotnet dev-certs https --trust.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DotEnv.ps1')

$envValues = Read-EudoriaDotEnv

Set-EudoriaConnectionString $envValues
Set-EudoriaTokenSigningKey $envValues

# HTTPS only. In Development, Kestrel uses the .NET development certificate on its own; it must be
# trusted by Windows, otherwise clients (Unreal, curl) rightly refuse the connection.
& dotnet dev-certs https --check --trust *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Error ("The .NET HTTPS development certificate is missing or not trusted. " +
        "Run once, in an interactive terminal: dotnet dev-certs https --trust (then answer Yes). See docs/SETUP.md.")
    exit 1
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "https://localhost:$($envValues['CORE_HTTPS_PORT'])"

$project = Join-Path $script:RepoRoot 'core\src\Eudoria.Core.Api'
& dotnet run --project $project --no-launch-profile
exit $LASTEXITCODE
