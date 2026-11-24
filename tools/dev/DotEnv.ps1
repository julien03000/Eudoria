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
    foreach ($required in 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_DB', 'POSTGRES_PORT', 'CORE_HTTP_PORT') {
        if (-not $values.ContainsKey($required) -or $values[$required] -eq '') {
            throw "Missing value for $required in .env (see .env.example)."
        }
    }
    return $values
}
