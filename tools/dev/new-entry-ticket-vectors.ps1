<#
.SYNOPSIS
    Regenerates the shared entry ticket test vectors (shared/test-vectors/entry-tickets/).
.DESCRIPTION
    Runs tools/dev/NewEntryTicketVectors.cs, which signs every vector with throwaway keys created in
    memory and writes keys.json (public keys only) and vectors.json. No private key is written anywhere.
    Every run makes new keys, hence new files: regenerate only when the vectors change, then run the
    tests of both verifiers (.NET: dotnet test; C++: tools/dev/test-unreal.ps1).
.EXAMPLE
    .\tools\dev\new-entry-ticket-vectors.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$output = Join-Path $repoRoot 'shared\test-vectors\entry-tickets'

& dotnet run --file (Join-Path $PSScriptRoot 'NewEntryTicketVectors.cs') -- $output
if ($LASTEXITCODE -ne 0) {
    throw "Vector generation failed (exit code $LASTEXITCODE)."
}
