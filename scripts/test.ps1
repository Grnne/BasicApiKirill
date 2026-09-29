<#
.SYNOPSIS
    Полный прогон проверок перед мёржем (замена CI до последнего этапа плана 2).

.DESCRIPTION
    1. Сборка решения (Release).
    2. Юнит-тесты (BasicApi.Tests).
    3. Интеграционные тесты (BasicApi.IntegrationTests) — нужен запущенный Docker.
    4. Аудит NuGet-пакетов на известные уязвимости, включая транзитивные.
    5. По флагу -Image — сборка docker-образа, как в проде.

    В конце печатает строку-итог: её нужно вставить в описание PR или коммита.
    Код возврата 0 — всё прошло; иначе — первая упавшая стадия.

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 -SkipIntegration     # быстрый прогон без Docker
    ./scripts/test.ps1 -Image               # плюс сборка docker-образа
#>
[CmdletBinding()]
param(
    [switch]$SkipIntegration,
    [switch]$Image
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'TestResults'
$summary = [ordered]@{}

function Step([string]$name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "FAILED: $name (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

# Итог по тестам берём из trx: вывод dotnet test локализован и неудобен для разбора.
function TestCounts([string]$dir) {
    $trx = Get-ChildItem $dir -Filter *.trx | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $trx) { return 'no results' }
    $c = ([xml](Get-Content $trx.FullName -Raw)).TestRun.ResultSummary.Counters
    return "$($c.passed)/$($c.total)"
}

function RunTests([string]$project, [string]$key) {
    $dir = Join-Path $results $key
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    # E2E идут отдельно (scripts/e2e.ps1) — им нужен развёрнутый стек.
    dotnet test (Join-Path $root "$project/$project.csproj") -c Release --no-build `
        --filter "Category!=E2E" `
        --logger "trx" --logger "console;verbosity=minimal" --results-directory $dir
    $code = $LASTEXITCODE
    $summary[$key] = TestCounts $dir
    $global:LASTEXITCODE = $code
}

Push-Location $root
try {
    Step 'Build' {
        dotnet build BasicApi.sln -c Release -nologo -v q
    }

    Step 'Unit tests' { RunTests 'BasicApi.Tests' 'unit' }

    if ($SkipIntegration) {
        $summary['integration'] = 'SKIPPED'
    } else {
        Step 'Docker check' {
            docker version --format '{{.Server.Version}}' | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host 'Docker не запущен. Запустите Docker Desktop или используйте -SkipIntegration.' -ForegroundColor Yellow
            }
        }
        Step 'Integration tests' { RunTests 'BasicApi.IntegrationTests' 'integration' }
    }

    Step 'Vulnerable packages' {
        # По проектам, а не по решению: docker-compose.dcproj в решении роняет команду.
        $found = @()
        $projects = Get-ChildItem $root -Filter *.csproj -Recurse -Depth 1
        foreach ($csproj in $projects) {
            $json = dotnet list $csproj.FullName package --vulnerable --include-transitive --format json | Out-String
            if ($LASTEXITCODE -ne 0) { Write-Host $json; return }
            $p = ($json | ConvertFrom-Json).projects[0]
            foreach ($f in @($p.frameworks)) {
                foreach ($pkg in @($f.topLevelPackages) + @($f.transitivePackages)) {
                    if ($pkg -and $pkg.vulnerabilities) {
                        $found += "$($pkg.id) $($pkg.resolvedVersion) ($(Split-Path -Leaf $p.path))"
                    }
                }
            }
        }
        $found = $found | Sort-Object -Unique
        if ($found) {
            $found | ForEach-Object { Write-Host "  vulnerable: $_" -ForegroundColor Red }
            $summary['vulnerable'] = "$($found.Count) found"
            $global:LASTEXITCODE = 1
        } else {
            Write-Host '  none'
            $summary['vulnerable'] = 'none'
        }
    }

    if ($Image) {
        Step 'Docker image' {
            docker build -f BasicApi/Dockerfile -t basicapi:test .
        }
        $summary['image'] = 'ok'
    }
}
finally {
    Pop-Location
}

$commit = (git -C $root rev-parse --short HEAD).Trim()
$dirty = if (git -C $root status --porcelain) { '+dirty' } else { '' }
$parts = $summary.GetEnumerator() | ForEach-Object { "$($_.Key) $($_.Value)" }
$line = "Tests: $($parts -join ', ') — scripts/test.ps1 @ $commit$dirty, $(Get-Date -Format 'yyyy-MM-dd')"

Write-Host ""
Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green
Write-Host $line
