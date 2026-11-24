<#
.SYNOPSIS
    Runs the EUDORIA core locally, configured from the repository's .env file.
.DESCRIPTION
    The connection string is built in this process's memory only: nothing is written to disk.
    Start the database first with .\tools\dev\start-db.ps1.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'DotEnv.ps1')

$envValues = Read-EudoriaDotEnv

# Quotes a connection-string value so that ';', '=' or quotes in it are safe.
function ConvertTo-ConnectionStringValue([string]$value) {
    return '"' + $value.Replace('"', '""') + '"'
}

# 127.0.0.1, not "localhost": the database is published on IPv4 loopback only, and "localhost"
# resolves to ::1 first, where a refused connection takes ~2 s on Windows (= the health-check budget).
$env:ConnectionStrings__Core = 'Host=127.0.0.1;Port={0};Database={1};Username={2};Password={3}' -f `
    $envValues['POSTGRES_PORT'],
    (ConvertTo-ConnectionStringValue $envValues['POSTGRES_DB']),
    (ConvertTo-ConnectionStringValue $envValues['POSTGRES_USER']),
    (ConvertTo-ConnectionStringValue $envValues['POSTGRES_PASSWORD'])
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$($envValues['CORE_HTTP_PORT'])"

$project = Join-Path $script:RepoRoot 'core\src\Eudoria.Core.Api'
& dotnet run --project $project --no-launch-profile
exit $LASTEXITCODE
