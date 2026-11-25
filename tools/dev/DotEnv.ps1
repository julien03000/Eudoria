# EUDORIA — shared helpers for the dev scripts (dot-sourced, not run directly).

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script:EnvFile = Join-Path $script:RepoRoot '.env'
$script:ComposeFile = Join-Path $PSScriptRoot 'docker-compose.yml'

# Reads KEY=VALUE lines from the repository's .env file into a hashtable.
# Blank lines and lines starting with '#' are ignored. Nothing is printed (secrets).
function Read-EudoriaDotEnv {
    if (-not (Test-Path -LiteralPath $script:EnvFile)) {
        throw "Missing $script:EnvFile. Create it once with: Copy-Item .env.example .env (then change the password)."
    }
    $values = @{}
    foreach ($line in Get-Content -LiteralPath $script:EnvFile -Encoding UTF8) {
        $trimmed = $line.Trim()
        if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { continue }
        $separator = $trimmed.IndexOf('=')
        if ($separator -lt 1) { throw "Invalid line in .env (expected KEY=VALUE): '$trimmed'" }
        $values[$trimmed.Substring(0, $separator).Trim()] = $trimmed.Substring($separator + 1).Trim()
    }
    foreach ($required in 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_DB', 'POSTGRES_PORT', 'CORE_HTTPS_PORT') {
        if (-not $values.ContainsKey($required) -or $values[$required] -eq '') {
            throw "Missing value for $required in .env (see .env.example)."
        }
    }
    return $values
}

# Quotes a connection-string value so that ';', '=' or quotes in it are safe.
function ConvertTo-ConnectionStringValue([string]$value) {
    return '"' + $value.Replace('"', '""') + '"'
}

# Sets ConnectionStrings__Core for this process only (in memory: never written to disk, never on a command line).
# 127.0.0.1, not "localhost": the database is published on IPv4 loopback only, and "localhost"
# resolves to ::1 first, where a refused connection takes ~2 s on Windows (= the health-check budget).
function Set-EudoriaConnectionString([hashtable]$envValues) {
    $env:ConnectionStrings__Core = 'Host=127.0.0.1;Port={0};Database={1};Username={2};Password={3}' -f `
        $envValues['POSTGRES_PORT'],
        (ConvertTo-ConnectionStringValue $envValues['POSTGRES_DB']),
        (ConvertTo-ConnectionStringValue $envValues['POSTGRES_USER']),
        (ConvertTo-ConnectionStringValue $envValues['POSTGRES_PASSWORD'])
}

# Sets the core's access-token signing key for this process only (in memory, never displayed).
function Set-EudoriaTokenSigningKey([hashtable]$envValues) {
    $key = $envValues['IDENTITY_TOKEN_SIGNING_KEY']
    if (-not $key) {
        throw "Missing IDENTITY_TOKEN_SIGNING_KEY in .env. Generate it once with: .\tools\dev\new-signing-key.ps1"
    }
    $env:Identity__Tokens__SigningKey = $key
}
