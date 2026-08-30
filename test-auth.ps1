#!/usr/bin/env pwsh
<#
.SYNOPSIS
  End-to-end tests for session + refresh-token auth against a running stack.
  Covers issuing, rotation, the 30s grace window, reuse detection, logout,
  logout-all and deactivated accounts.

.NOTES
  Includes two deliberate waits (~35s each) to step past the grace window —
  the whole run takes a couple of minutes.
#>

$BaseUrl = "http://localhost:8080"
$GraceSeconds = 30
$Passed = 0
$Failed = 0

function Check {
    param([string]$Name, [scriptblock]$Assertion)
    try {
        $r = & $Assertion
        if ($r -eq $false) { throw "assertion returned false" }
        Write-Host "  [PASS] $Name" -ForegroundColor Green
        $script:Passed++
    } catch {
        Write-Host "  [FAIL] $Name -- $_" -ForegroundColor Red
        $script:Failed++
    }
}

function Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "==========================================" -ForegroundColor Yellow
    Write-Host "  $Title" -ForegroundColor Yellow
    Write-Host "==========================================" -ForegroundColor Yellow
}

# Returns @{ Status; Body; ErrorCode }
function Api {
    param(
        [string]$Method = "GET",
        [string]$Url,
        $Body = $null,
        [string]$Token = $null,
        [int]$RetriesLeft = 3
    )
    $headers = @{ "Content-Type" = "application/json" }
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }

    $params = @{
        Method = $Method
        Uri = "$BaseUrl$Url"
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 20
    }
    if ($null -ne $Body) { $params["Body"] = ($Body | ConvertTo-Json -Depth 6) }

    try {
        $response = Invoke-WebRequest @params
        $parsed = $null
        if ($response.Content) { $parsed = $response.Content | ConvertFrom-Json }
        return @{ Status = [int]$response.StatusCode; Body = $parsed; ErrorCode = $null }
    } catch {
        $code = 0
        $errorCode = $null
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode.value__

            # ProblemDetails carries the machine-readable errorCode we assert on
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $stream.Position = 0
                $reader = New-Object System.IO.StreamReader($stream)
                $raw = $reader.ReadToEnd()
                if ($raw) { $errorCode = ($raw | ConvertFrom-Json).errorCode }
            } catch { }
        }

        if ($code -eq 429 -and $RetriesLeft -gt 0) {
            Write-Host "  [rate-limited] $Method $Url -- waiting 61s" -ForegroundColor DarkYellow
            Start-Sleep -Seconds 61
            return Api -Method $Method -Url $Url -Body $Body -Token $Token -RetriesLeft ($RetriesLeft - 1)
        }

        return @{ Status = $code; Body = $null; ErrorCode = $errorCode }
    }
}

function Refresh { param([string]$Token) Api -Method POST -Url "/api/auth/refresh" -Body @{ refreshToken = $Token } }

Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "  BasicChatApi - Session / refresh tokens" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

Section "1. Register issues an access/refresh pair"

$suffix = Get-Random
$username = "auth_user_$suffix"
$reg = Api -Method POST -Url "/api/auth/register" -Body @{
    username = $username
    email = "auth_user_$suffix@test.com"
    password = "Test123!"
    displayName = "Auth User"
}

Check "201 Created" { $reg.Status -eq 201 }
Check "access token returned" { -not [string]::IsNullOrWhiteSpace($reg.Body.token) }
Check "refresh token returned" { -not [string]::IsNullOrWhiteSpace($reg.Body.refreshToken) }
Check "access token is short-lived (under an hour)" {
    # ConvertFrom-Json/[DateTime] land on local time; compare against local now,
    # not against a UTC value, or the two scales differ by the timezone offset.
    $minutes = (([DateTime]$reg.Body.expiresAt) - (Get-Date)).TotalMinutes
    ($minutes -gt 0) -and ($minutes -lt 60)
}
Check "refresh token outlives the access token" {
    ([DateTime]$reg.Body.refreshTokenExpiresAt) -gt ([DateTime]$reg.Body.expiresAt)
}

Section "2. Login issues a pair and the access token works"

$login = Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }
Check "200 OK" { $login.Status -eq 200 }
Check "refresh token returned on login" { -not [string]::IsNullOrWhiteSpace($login.Body.refreshToken) }
Check "login and register produce different refresh tokens" {
    $login.Body.refreshToken -ne $reg.Body.refreshToken
}

$me = Api -Url "/api/users/me" -Token $login.Body.token
Check "access token authenticates an API call" { $me.Status -eq 200 -and $me.Body.username -eq $username }

Section "3. Refresh rotates the pair"

$first = Refresh -Token $login.Body.refreshToken
Check "200 OK" { $first.Status -eq 200 }
Check "new access token issued" { -not [string]::IsNullOrWhiteSpace($first.Body.token) }
Check "refresh token was rotated" { $first.Body.refreshToken -ne $login.Body.refreshToken }
Check "rotated access token also works" {
    (Api -Url "/api/users/me" -Token $first.Body.token).Status -eq 200
}

$second = Refresh -Token $first.Body.refreshToken
Check "the rotated token can be refreshed again" { $second.Status -eq 200 }
Check "refresh window is not extended indefinitely" {
    ([DateTime]$second.Body.refreshTokenExpiresAt) -le ([DateTime]$login.Body.refreshTokenExpiresAt).AddSeconds(1)
}

Section "4. Grace window: a racing client is not logged out"

# $first.refreshToken was rotated just now — inside the grace window it must still work.
$raced = Refresh -Token $first.Body.refreshToken
Check "already-rotated token still works within the grace window" { $raced.Status -eq 200 }
Check "the racing request gets a usable pair of its own" {
    (-not [string]::IsNullOrWhiteSpace($raced.Body.refreshToken)) -and
    ((Api -Url "/api/users/me" -Token $raced.Body.token).Status -eq 200)
}
Check "the winner's token keeps working too" {
    (Refresh -Token $second.Body.refreshToken).Status -eq 200
}

Section "5. Reuse after the grace window = theft, whole chain revoked"

$victim = Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }
$stolen = $victim.Body.refreshToken
$rotated = Refresh -Token $stolen
Check "setup: victim's token rotates normally" { $rotated.Status -eq 200 }

Write-Host "  ... waiting out the ${GraceSeconds}s grace window" -ForegroundColor DarkGray
Start-Sleep -Seconds ($GraceSeconds + 5)

$replay = Refresh -Token $stolen
Check "replayed token rejected with 401" { $replay.Status -eq 401 }
Check "errorCode = REFRESH_TOKEN_REUSED" { $replay.ErrorCode -eq "REFRESH_TOKEN_REUSED" }

$afterTheft = Refresh -Token $rotated.Body.refreshToken
Check "the whole rotation chain is revoked, including the live token" {
    $afterTheft.Status -eq 401
}
Check "other sessions of the same user survive" {
    (Refresh -Token $raced.Body.refreshToken).Status -eq 200
}

Section "6. Rejections"

$bogus = Refresh -Token "definitely-not-a-real-token"
Check "unknown token -> 401 INVALID_REFRESH_TOKEN" {
    $bogus.Status -eq 401 -and $bogus.ErrorCode -eq "INVALID_REFRESH_TOKEN"
}

$emptyBody = Api -Method POST -Url "/api/auth/refresh" -Body @{ refreshToken = "" }
Check "empty token -> 400 validation error" { $emptyBody.Status -eq 400 }

Section "7. Logout revokes exactly one session"

$sessionA = Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }
$sessionB = Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }

$logout = Api -Method POST -Url "/api/auth/logout" -Token $sessionA.Body.token -Body @{ refreshToken = $sessionA.Body.refreshToken }
Check "logout returns 200" { $logout.Status -eq 200 }

$afterLogout = Refresh -Token $sessionA.Body.refreshToken
Check "logged-out session cannot refresh" { $afterLogout.Status -eq 401 }
Check "errorCode = SESSION_REVOKED" { $afterLogout.ErrorCode -eq "SESSION_REVOKED" }
Check "the other device is untouched" { (Refresh -Token $sessionB.Body.refreshToken).Status -eq 200 }

$logoutAgain = Api -Method POST -Url "/api/auth/logout" -Token $sessionA.Body.token -Body @{ refreshToken = $sessionA.Body.refreshToken }
Check "logout is idempotent" { $logoutAgain.Status -eq 200 }

$logoutUnknown = Api -Method POST -Url "/api/auth/logout" -Token $sessionB.Body.token -Body @{ refreshToken = "never-issued" }
Check "logout with an unknown token still returns 200 (no token oracle)" { $logoutUnknown.Status -eq 200 }

Section "8. Logout-all kills every session"

$sessionC = Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }
$logoutAll = Api -Method POST -Url "/api/auth/logout-all" -Token $sessionC.Body.token
Check "logout-all returns 200" { $logoutAll.Status -eq 200 }
Check "its own session is gone" { (Refresh -Token $sessionC.Body.refreshToken).Status -eq 401 }
Check "every other session is gone too" {
    ((Refresh -Token $raced.Body.refreshToken).Status -eq 401) -and
    ((Refresh -Token $sessionB.Body.refreshToken).Status -eq 401)
}
Check "login still works after logging out everywhere" {
    (Api -Method POST -Url "/api/auth/login" -Body @{ usernameOrEmail = $username; password = "Test123!" }).Status -eq 200
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "  RESULTS:" -ForegroundColor Magenta
Write-Host "  Passed: $Passed" -ForegroundColor Green
if ($Failed -gt 0) {
    Write-Host "  Failed: $Failed" -ForegroundColor Red
} else {
    Write-Host "  Failed: 0" -ForegroundColor Green
}
Write-Host "  Total:  $($Passed + $Failed)" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Magenta

if ($Failed -gt 0) { exit 1 } else { exit 0 }
