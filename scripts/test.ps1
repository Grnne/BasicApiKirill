<#
.SYNOPSIS
    Full pre-merge check run; CI (.github/workflows/ci.yml) runs the same on every push.

.DESCRIPTION
    1. Solution build (Release).
    2. Web client (BasicWebClient): type check and Vitest tests — Node.js must be installed.
    3. Unit tests (BasicApi.Tests).
    4. Integration tests (BasicApi.IntegrationTests) — Docker must be running.
    5. Audit of NuGet packages and the client's runtime npm packages for known vulnerabilities.
    6. With -Image — the docker image build, as in production.

    Prints a summary line at the end: paste it into the PR or commit description.
    Exit code 0 — everything passed; otherwise — the first failed stage.

.EXAMPLE
    ./scripts/test.ps1
    ./scripts/test.ps1 -SkipIntegration     # quick run without Docker
    ./scripts/test.ps1 -SkipClient          # backend only, without Node.js
    ./scripts/test.ps1 -Image               # plus the docker image build
#>
[CmdletBinding()]
param(
    [switch]$SkipIntegration,
    [switch]$SkipClient,
    [switch]$Image
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$results = Join-Path $root 'TestResults'
$client = Join-Path $root 'BasicWebClient'
$summary = [ordered]@{}

# In GitHub Actions the result also goes to the summary page of the run.
function Report([string]$text) {
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text -Encoding utf8 }
}

function Step([string]$name, [scriptblock]$action) {
    Write-Host ""
    Write-Host "=== $name ===" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "FAILED: $name (exit $LASTEXITCODE)" -ForegroundColor Red
        Report "**FAILED: $name** (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

# Test totals come from trx: dotnet test output is localized and awkward to parse.
function TestCounts([string]$dir) {
    $trx = Get-ChildItem $dir -Filter *.trx | Sort-Object LastWriteTime | Select-Object -Last 1
    if (-not $trx) { return 'no results' }
    $c = ([xml](Get-Content $trx.FullName -Raw)).TestRun.ResultSummary.Counters
    return "$($c.passed)/$($c.total)"
}

function RunTests([string]$project, [string]$key) {
    $dir = Join-Path $results $key
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    # E2E runs separately (scripts/e2e.ps1): it needs a deployed stack.
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

    if ($SkipClient) {
        $summary['client'] = 'SKIPPED'
    } else {
        Push-Location $client
        try {
            Step 'Client dependencies' {
                # npm ci wipes node_modules: locally only when the lock file changed since the last install.
                $installed = Join-Path $client 'node_modules/.package-lock.json'
                $lock = Join-Path $client 'package-lock.json'
                if ($env:CI -or -not (Test-Path $installed) -or
                    (Get-Item $lock).LastWriteTime -gt (Get-Item $installed).LastWriteTime) {
                    npm ci --no-audit --no-fund
                } else {
                    Write-Host '  up to date'
                    $global:LASTEXITCODE = 0
                }
            }
            Step 'Client type check' { npm run check }
            Step 'Client tests' {
                $out = Join-Path $results 'client'
                if (Test-Path $out) { Remove-Item $out -Recurse -Force }
                $json = Join-Path $out 'vitest.json'
                npx vitest run --reporter=default --reporter=json "--outputFile.json=$json"
                $code = $LASTEXITCODE
                $summary['client'] = if (Test-Path $json) {
                    $r = Get-Content $json -Raw | ConvertFrom-Json
                    "$($r.numPassedTests)/$($r.numTotalTests)"
                } else { 'no results' }
                $global:LASTEXITCODE = $code
            }
        }
        finally {
            Pop-Location
        }
    }

    Step 'Unit tests' { RunTests 'BasicApi.Tests' 'unit' }

    if ($SkipIntegration) {
        $summary['integration'] = 'SKIPPED'
    } else {
        Step 'Docker check' {
            docker version --format '{{.Server.Version}}' | Out-Null
            if ($LASTEXITCODE -ne 0) {
                Write-Host 'Docker is not running. Start Docker Desktop or use -SkipIntegration.' -ForegroundColor Yellow
            }
        }
        Step 'Integration tests' { RunTests 'BasicApi.IntegrationTests' 'integration' }
    }

    Step 'Vulnerable packages' {
        # Per project, not per solution: docker-compose.dcproj in the solution breaks the command.
        $found = @()
        $projects = Get-ChildItem $root -Filter *.csproj -Recurse -Depth 2 |
            Where-Object FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]'
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
        # Client: runtime packages only — dev tooling does not reach the browser or the image.
        if (-not $SkipClient) {
            $audit = npm audit --omit=dev --json --prefix $client | Out-String | ConvertFrom-Json
            if ($audit.vulnerabilities) {
                foreach ($v in $audit.vulnerabilities.PSObject.Properties) {
                    $found += "$($v.Name) ($($v.Value.severity), BasicWebClient)"
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
Report $line
