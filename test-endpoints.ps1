#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Tests all REST endpoints of BasicChatApi.
#>

$BaseUrl = "http://localhost:8080"
$Passed = 0
$Failed = 0

function Test-Endpoint {
    param(
        [string]$Name,
        [string]$Method = "GET",
        [string]$Url,
        [string]$Body = $null,
        [string]$Token = $null,
        [int]$ExpectedStatus = 200,
        [scriptblock]$Validate = $null
    )

    Write-Host "`n=== $Method $Url ===" -ForegroundColor Cyan
    Write-Host ">>> $Name" -ForegroundColor Gray

    $headers = @{
        "Content-Type" = "application/json"
    }
    if ($Token) {
        $headers["Authorization"] = "Bearer $Token"
    }

    $params = @{
        Method = $Method
        Uri = "$BaseUrl$Url"
        Headers = $headers
        UseBasicParsing = $true
        TimeoutSec = 15
    }

    if ($Body) {
        $params["Body"] = $Body
    }

    try {
        $response = Invoke-WebRequest @params
        $status = [int]$response.StatusCode

        if ($status -eq $ExpectedStatus) {
            Write-Host "  [PASS] HTTP $status" -ForegroundColor Green
            $script:Passed++
            
            if ($Validate) {
                try {
                    $content = $response.Content | ConvertFrom-Json
                    & $Validate $content
                    Write-Host "  [PASS] Validation" -ForegroundColor Green
                } catch {
                    Write-Host "  [FAIL] Validation: $_" -ForegroundColor Red
                    $script:Failed++
                }
            }
            
            return $response.Content | ConvertFrom-Json
        } else {
            Write-Host "  [FAIL] Expected $ExpectedStatus, got $status" -ForegroundColor Red
            Write-Host "  Body: $($response.Content)" -ForegroundColor DarkRed
            $script:Failed++
        }
    } catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -eq $ExpectedStatus) {
            Write-Host "  [PASS] HTTP $statusCode (expected error)" -ForegroundColor Green
            $script:Passed++
        } else {
            Write-Host "  [FAIL] $_" -ForegroundColor Red
            $script:Failed++
        }
    }
}

Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "  BasicChatApi - Endpoint Tests" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "Base URL: $BaseUrl"
Write-Host ""

# ============================
# 1. Register users
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  1. AUTH - Registration" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

$suffix = Get-Random
$user1Email = "curl_user1_${suffix}@test.com"
$user1Username = "curluser1_${suffix}"
$user2Email = "curl_user2_${suffix}@test.com"
$user2Username = "curluser2_${suffix}"

$user1 = Test-Endpoint -Name "Register user 1" -Method POST -Url "/api/auth/register" `
    -Body (@{
        username = $user1Username
        email = $user1Email
        password = "Test123!"
        displayName = "Curl User 1"
    } | ConvertTo-Json) -ExpectedStatus 201 -Validate {
        param($body)
        if (-not $body.token) { throw "No token in response" }
        if (-not $body.userId) { throw "No userId in response" }
    }

$user2 = Test-Endpoint -Name "Register user 2" -Method POST -Url "/api/auth/register" `
    -Body (@{
        username = $user2Username
        email = $user2Email
        password = "Test123!"
        displayName = "Curl User 2"
    } | ConvertTo-Json) -ExpectedStatus 201 -Validate {
        param($body)
        if (-not $body.token) { throw "No token in response" }
        if (-not $body.userId) { throw "No userId in response" }
    }

$token1 = $user1.token
$token2 = $user2.token
$userId1 = $user1.userId
$userId2 = $user2.userId

Write-Host "`n  User 1: $userId1 ($user1Username)" -ForegroundColor DarkGray
Write-Host "  User 2: $userId2 ($user2Username)" -ForegroundColor DarkGray

# ============================
# 2. Login
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  2. AUTH - Login" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

# FIXED: was 'email', now 'usernameOrEmail' matches LoginRequestDto
Test-Endpoint -Name "Login user 1" -Method POST -Url "/api/auth/login" `
    -Body (@{ usernameOrEmail = $user1Email; password = "Test123!" } | ConvertTo-Json) -ExpectedStatus 200 -Validate {
        param($body)
        if (-not $body.token) { throw "No token" }
    }

Test-Endpoint -Name "Login user 2" -Method POST -Url "/api/auth/login" `
    -Body (@{ usernameOrEmail = $user2Email; password = "Test123!" } | ConvertTo-Json) -ExpectedStatus 200 -Validate {
        param($body)
        if (-not $body.token) { throw "No token" }
    }

# ============================
# 3. GetUserId
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  3. USERS - Lookup" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Get user ID by username" -Method GET `
    -Url "/api/users/GetUserId/$user1Username" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if (-not $body.userId) { throw "No userId" }
    }

# ============================
# 4. Chats - empty list
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  4. CHATS - Empty list" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Get user chats (empty)" -Method GET `
    -Url "/api/chats" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body -isnot [System.Array]) { throw "Expected array" }
    }

# ============================
# 5. Create private chat
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  5. CHATS - Create private chat" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

$privateChat = Test-Endpoint -Name "Create private chat (user1 -> user2)" -Method POST `
    -Url "/api/chats/private/$userId2" -Token $token1 -ExpectedStatus 201 -Validate {
        param($body)
        if (-not $body.chatId) { throw "No chatId" }
    }

$chatId = $privateChat.chatId
Write-Host "  Private chat ID: $chatId" -ForegroundColor DarkGray

# Repeat - should return 200 (existing chat)
Test-Endpoint -Name "Create private chat again (should return 200)" -Method POST `
    -Url "/api/chats/private/$userId2" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.chatId -ne $chatId) { throw "Expected same chatId" }
    }

# ============================
# 6. Chat details
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  6. CHATS - Chat details" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Get chat details" -Method GET `
    -Url "/api/chats/$chatId" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.chatId -ne $chatId) { throw "Wrong chatId" }
        if ($body.type -ne "private") { throw "Expected private type" }
        if (-not $body.participants -or $body.participants.Count -lt 2) { throw "Expected 2+ participants" }
    }

# ============================
# 7. Chat list after creation
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  7. CHATS - List after creation" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Get user chats (user1)" -Method GET `
    -Url "/api/chats" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.Count -lt 1) { throw "Expected at least 1 chat" }
    }

Test-Endpoint -Name "Get user chats (user2)" -Method GET `
    -Url "/api/chats" -Token $token2 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.Count -lt 1) { throw "Expected at least 1 chat" }
    }

# ============================
# 8. Search chats
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  8. CHATS - Search" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Search chats (all types)" -Method GET `
    -Url '/api/chats/search?q=Curl&limit=20' -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items.Count -lt 1) { throw "Expected at least 1 result" }
        if ($body.totalCount -lt 1) { throw "Expected totalCount >= 1" }
        if (-not $body.query) { throw "Expected query" }
    }

Test-Endpoint -Name "Search chats (private type)" -Method GET `
    -Url '/api/chats/search?q=Curl&type=private&limit=20' -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items.Count -lt 1) { throw "Expected at least 1 private result" }
        foreach ($item in $body.items) {
            if ($item.type -ne "private") { throw "Expected all private chats" }
        }
    }

Test-Endpoint -Name "Search chats (group type, empty)" -Method GET `
    -Url '/api/chats/search?q=something&type=group&limit=20' -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items.Count -ne 0) { throw "Expected 0 group results" }
    }

Test-Endpoint -Name "Search chats (by companion name)" -Method GET `
    -Url '/api/chats/search?q=Curl+User+2&type=private&limit=20' -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items.Count -eq 0) { throw "Expected at least 1 result" }
        if ($body.items[0].companionName -notmatch "Curl User") { throw "Expected companion name match" }
    }

# ============================
# 9. Forbidden - not a member
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  9. CHATS - Forbidden" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

$user3Email = "curl_user3_${suffix}@test.com"
$user3Username = "curluser3_${suffix}"

$user3 = Test-Endpoint -Name "Register user 3 (no chat)" -Method POST -Url "/api/auth/register" `
    -Body (@{
        username = $user3Username
        email = $user3Email
        password = "Test123!"
        displayName = "Curl User 3"
    } | ConvertTo-Json) -ExpectedStatus 201

$token3 = $user3.token

Test-Endpoint -Name "Get chat details (not member) - Forbidden" -Method GET `
    -Url "/api/chats/$chatId" -Token $token3 -ExpectedStatus 403

# ============================
# 10. Messages - cursor
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  10. MESSAGES - Cursor pagination" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Get messages (empty)" -Method GET `
        -Url "/api/chats/$chatId/messages/cursor?limit=20" -Token $token1 -ExpectedStatus 200 -Validate {
            param($body)
            if ($body.items -isnot [System.Array]) { throw "Expected items array" }
        }

Test-Endpoint -Name "Get messages (not member) - Forbidden" -Method GET `
        -Url "/api/chats/$chatId/messages/cursor?limit=20" -Token $token3 -ExpectedStatus 403

# ============================
# 11. Messages at date
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  11. MESSAGES - Jump to date" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

$date = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
Test-Endpoint -Name "Get messages at date" -Method GET `
    -Url "/api/chats/$chatId/messages/at?date=$date" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items -isnot [System.Array]) { throw "Expected items array" }
    }

# ============================
# 12. Search messages
# ============================
Write-Host "`n==========================================" -ForegroundColor Yellow
Write-Host "  12. MESSAGES - Full-text search" -ForegroundColor Yellow
Write-Host "==========================================" -ForegroundColor Yellow

Test-Endpoint -Name "Search messages (empty)" -Method GET `
    -Url "/api/chats/$chatId/messages/search?q=hello" -Token $token1 -ExpectedStatus 200 -Validate {
        param($body)
        if ($body.items -isnot [System.Array]) { throw "Expected items array" }
        if (-not $body.query) { throw "Expected query" }
    }

Test-Endpoint -Name "Search messages (not member) - Forbidden" -Method GET `
    -Url "/api/chats/$chatId/messages/search?q=hello" -Token $token3 -ExpectedStatus 403

Test-Endpoint -Name "Search messages (short query) - BadRequest" -Method GET `
    -Url "/api/chats/$chatId/messages/search?q=x" -Token $token1 -ExpectedStatus 400

# ============================
# RESULTS
# ============================
Write-Host ""
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "  RESULTS:" -ForegroundColor Magenta
Write-Host "  Passed: $Passed" -ForegroundColor Green
Write-Host "  Failed: $Failed" -ForegroundColor $(if ($Failed -gt 0) { "Red" } else { "Green" })
Write-Host "  Total:  $(($Passed + $Failed))" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Magenta

if ($Failed -gt 0) {
    Write-Host "  [FAIL] Some tests failed!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "  [PASS] All tests passed!" -ForegroundColor Green
    exit 0
}
