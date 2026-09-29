<#
.SYNOPSIS
    End-to-end tests against a deployed stack (Caddy → API → Postgres).

.DESCRIPTION
    Talks to the stack over real HTTPS and WebSocket: health and headers,
    registration, a private chat, real-time messages, unread counts,
    history, search, hub errors, logout closing the connection.

    Creates two users with unique names (E2E_Alice_xxxx, e2e_bob_xxxx);
    in production they stay, there is no need to delete them.

    Makes 3 registrations/logins: the auth policy allows 5 per minute per IP,
    so wait a minute between runs.

.EXAMPLE
    ./scripts/e2e.ps1                                        # local prod stack (see docs/deploy.md)
    ./scripts/e2e.ps1 -BaseUrl https://chat.example.com      # after a deploy
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:8443',
    # Trust a self-signed certificate (local Caddy for localhost).
    [switch]$Insecure
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if (-not $PSBoundParameters.ContainsKey('Insecure') -and $BaseUrl -match '^https://(localhost|127\.0\.0\.1)') {
    $Insecure = $true
}

$env:E2E_BASE_URL = $BaseUrl
$env:E2E_INSECURE = if ($Insecure) { '1' } else { '' }

Write-Host "E2E against $BaseUrl$(if ($Insecure) { ' (certificate not verified)' })" -ForegroundColor Cyan

try {
    dotnet test (Join-Path $root 'BasicApi.IntegrationTests/BasicApi.IntegrationTests.csproj') `
        --filter 'Category=E2E' --logger 'console;verbosity=normal'
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:E2E_BASE_URL, Env:E2E_INSECURE -ErrorAction SilentlyContinue
}
