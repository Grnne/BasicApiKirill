<#
.SYNOPSIS
    UI end-to-end tests: user scenarios in a real browser against a deployed stack.

.DESCRIPTION
    Playwright drives an installed browser (Edge by default, or Chrome; nothing is downloaded)
    through the web client: registration and sign-in, chats and messages, files, groups and
    rights, settings and devices, privacy and blocking, a phone-sized screen, security checks.

    Every test registers its own users with unique names, dozens per run: the stack must allow
    that many sign-ins from one IP (RateLimiting__AuthPerMinute, docs/deploy.md). Meant for the
    local prod stack; on production the users would stay.

    -Restart adds the resilience tests: they restart the API and Postgres containers of the
    stack (basicchat_api, basicchat_postgres). Local stack only.

    Report: BasicWebClient/e2e-report/index.html; traces of failures in BasicWebClient/e2e-results.

.EXAMPLE
    ./scripts/e2e-ui.ps1                          # local prod stack, https://localhost:8443
    ./scripts/e2e-ui.ps1 -Restart                 # plus restarts of the API and the database
    ./scripts/e2e-ui.ps1 -Grep 'files|groups'     # some of the scenarios
    ./scripts/e2e-ui.ps1 -Headed -Workers 1       # watch it
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:8443',
    [ValidateSet('msedge', 'chrome')]
    [string]$Browser = 'msedge',
    [switch]$Restart,
    [switch]$Headed,
    [int]$Workers = 4,
    [string]$Grep
)

$ErrorActionPreference = 'Stop'
$client = Join-Path (Split-Path -Parent $PSScriptRoot) 'BasicWebClient'

Push-Location $client
try {
    if (-not (Test-Path 'node_modules/@playwright/test')) {
        # The installed browser is used: no browser download.
        $env:PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD = '1'
        npm ci
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }

    $env:E2E_BASE_URL = $BaseUrl
    $env:E2E_BROWSER = $Browser
    $env:E2E_WORKERS = "$Workers"
    $env:E2E_RESTART = if ($Restart) { '1' } else { '' }

    Write-Host "UI e2e against $BaseUrl in $Browser$(if ($Restart) { ', with restarts' })" -ForegroundColor Cyan

    # Node writes warnings to stderr; with Stop, Windows PowerShell would take them for failures
    # when the output is redirected. The exit code tells the result.
    $ErrorActionPreference = 'Continue'
    $arguments = @('playwright', 'test')
    if ($Headed) { $arguments += '--headed' }
    if ($Grep) { $arguments += @('--grep', $Grep) }
    npx @arguments
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:E2E_BASE_URL, Env:E2E_BROWSER, Env:E2E_WORKERS, Env:E2E_RESTART -ErrorAction SilentlyContinue
    Pop-Location
}
