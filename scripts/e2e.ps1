<#
.SYNOPSIS
    Сквозные тесты против развёрнутого стека (Caddy → API → Postgres).

.DESCRIPTION
    Ходит в стек по настоящему HTTPS и WebSocket: здоровье и заголовки,
    регистрация, личный чат, сообщения в реальном времени, непрочитанные,
    история, поиск, ошибки хаба, выход с обрывом соединения.

    Создаёт двух пользователей с уникальными именами (E2E_Alice_xxxx, e2e_bob_xxxx)
    — на проде они останутся; удалять их не обязательно.

    Делает 3 регистрации/входа: лимит auth-политики — 5 в минуту с IP, поэтому
    между повторными прогонами нужна минута.

.EXAMPLE
    ./scripts/e2e.ps1                                        # локальный прод-стек (см. docs/deploy.md)
    ./scripts/e2e.ps1 -BaseUrl https://chat.example.com      # после деплоя
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'https://localhost:8443',
    # Доверять самоподписанному сертификату (локальный Caddy для localhost).
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
