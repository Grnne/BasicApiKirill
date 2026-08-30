#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Critical-path tests for the chat-creation / presence contract changes.
  Covers: ChatListItemDto payloads, SignalR ChatCreated, GET /api/chats/{id}/item,
  GET /api/users/{id}/status, POST /api/users/status, and the companion-fields
  regression in GET /api/chats/search.
#>

$BaseUrl = "http://localhost:8080"
$WsUrl   = "ws://localhost:8080/hubs/chat"
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

# Returns @{ Status = <int>; Body = <object|null> }
# The API rate-limits by IP (60 req/min globally, 5/min on auth), so a 429 here
# is the limiter kicking in, not a contract failure — wait out the window and retry.
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
        TimeoutSec = 15
    }
    if ($null -ne $Body) { $params["Body"] = ($Body | ConvertTo-Json -Depth 6) }

    try {
        $response = Invoke-WebRequest @params
        $parsed = $null
        if ($response.Content) { $parsed = $response.Content | ConvertFrom-Json }
        return @{ Status = [int]$response.StatusCode; Body = $parsed }
    } catch {
        $code = 0
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode.value__ }

        if ($code -eq 429 -and $RetriesLeft -gt 0) {
            $wait = 61
            if ($_.Exception.Response.Headers -and $_.Exception.Response.Headers["Retry-After"]) {
                $parsedWait = 0
                if ([int]::TryParse($_.Exception.Response.Headers["Retry-After"], [ref]$parsedWait) -and $parsedWait -gt 0) {
                    $wait = $parsedWait + 1
                }
            }
            Write-Host "  [rate-limited] $Method $Url -- waiting ${wait}s" -ForegroundColor DarkYellow
            Start-Sleep -Seconds $wait
            return Api -Method $Method -Url $Url -Body $Body -Token $Token -RetriesLeft ($RetriesLeft - 1)
        }

        return @{ Status = $code; Body = $null }
    }
}

# ---------- SignalR over a raw WebSocket (JSON protocol, 0x1E record separator) ----------

$RS = [char]0x1E

function Send-Hub {
    param($Ws, [string]$Payload)
    $bytes = [Text.Encoding]::UTF8.GetBytes($Payload + $RS)
    $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
    $cts = New-Object System.Threading.CancellationTokenSource(5000)
    # [void]: GetResult() emits a VoidTaskResult that would otherwise pollute the caller's pipeline
    [void]$Ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $cts.Token).GetAwaiter().GetResult()
}

# Per-socket state: a ReceiveAsync task must never be cancelled (that aborts the
# ClientWebSocket for good), so an unfinished read is parked here and awaited again
# on the next call instead.
$script:Pending = @{}
$script:Acc = @{}

# Reads frames until the socket goes quiet for TimeoutMs. Returns parsed records.
function Receive-Hub {
    param($Ws, [int]$TimeoutMs = 5000)

    $key = $Ws.GetHashCode()
    if (-not $script:Acc.ContainsKey($key)) { $script:Acc[$key] = "" }

    $records = @()
    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)

    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not $script:Pending.ContainsKey($key)) {
            $buffer = New-Object byte[] 16384
            $seg = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
            $script:Pending[$key] = @{
                Task = $Ws.ReceiveAsync($seg, [System.Threading.CancellationToken]::None)
                Buffer = $buffer
            }
        }

        $pending = $script:Pending[$key]
        $remaining = [int][Math]::Max(50, ($deadline - [DateTime]::UtcNow).TotalMilliseconds)

        if (-not $pending.Task.Wait($remaining)) { break } # still nothing — leave it parked

        $script:Pending.Remove($key)
        $result = $pending.Task.Result
        if ($result.Count -gt 0) {
            $script:Acc[$key] += [Text.Encoding]::UTF8.GetString($pending.Buffer, 0, $result.Count)
        }

        while ($script:Acc[$key].Contains($RS)) {
            $idx = $script:Acc[$key].IndexOf($RS)
            $chunk = $script:Acc[$key].Substring(0, $idx)
            $script:Acc[$key] = $script:Acc[$key].Substring($idx + 1)
            if ($chunk.Length -gt 0) {
                $records += ($chunk | ConvertFrom-Json)
            } else {
                $records += ([pscustomobject]@{ type = 0 }) # handshake ack
            }
        }

        if ($records.Count -gt 0) {
            # got something — allow a short grace window for follow-up frames
            $grace = [DateTime]::UtcNow.AddMilliseconds(1000)
            if ($grace -lt $deadline) { $deadline = $grace }
        }
    }

    return $records
}

function Connect-Hub {
    param([string]$Token)

    $ws = New-Object System.Net.WebSockets.ClientWebSocket
    $uri = [Uri]("$WsUrl" + "?access_token=" + $Token)
    $cts = New-Object System.Threading.CancellationTokenSource(10000)
    [void]$ws.ConnectAsync($uri, $cts.Token).GetAwaiter().GetResult()

    # SignalR handshake
    Send-Hub -Ws $ws -Payload '{"protocol":"json","version":1}'
    $handshake = Receive-Hub -Ws $ws -TimeoutMs 5000
    if (@($handshake).Count -eq 0) { throw "no handshake response from hub" }
    if ($ws.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw "hub closed the connection after handshake (state=$($ws.State)) -- check the JWT"
    }
    return $ws
}

function Get-HubEvent {
    param($Records, [string]$Target)
    return $Records | Where-Object { $_.type -eq 1 -and $_.target -eq $Target } | Select-Object -First 1
}

function Close-Hub {
    param($Ws)
    try {
        $cts = New-Object System.Threading.CancellationTokenSource(5000)
        [void]$Ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "bye", $cts.Token).GetAwaiter().GetResult()
    } catch { }
    $Ws.Dispose()
}

# ============================================================
Write-Host "==========================================" -ForegroundColor Magenta
Write-Host "  BasicChatApi - New contract / critical path" -ForegroundColor Magenta
Write-Host "==========================================" -ForegroundColor Magenta

Section "0. Setup: three users"

$suffix = Get-Random
$users = @{}
foreach ($n in 1..3) {
    $username = "ct_user${n}_$suffix"
    $r = Api -Method POST -Url "/api/auth/register" -Body @{
        username = $username
        email = "ct_user${n}_$suffix@test.com"
        password = "Test123!"
        displayName = "CT User $n"
    }
    if ($r.Status -ne 201) {
        Write-Host "  [FATAL] cannot register user $n (HTTP $($r.Status))" -ForegroundColor Red
        exit 1
    }
    $users[$n] = @{ Id = $r.Body.userId; Token = $r.Body.token; Username = $username }
    Write-Host "  user${n}: $($r.Body.userId) ($username)" -ForegroundColor DarkGray
}

$u1 = $users[1]; $u2 = $users[2]; $u3 = $users[3]

Section "1. SignalR ChatCreated carries a full ChatListItemDto"

# user2 listens on the hub while user1 creates the chat
$ws2 = Connect-Hub -Token $u2.Token
Start-Sleep -Milliseconds 500

$create = Api -Method POST -Url "/api/chats/private/$($u2.Id)" -Token $u1.Token
$chatId = $create.Body.chatId

$events = Receive-Hub -Ws $ws2 -TimeoutMs 6000
$chatCreated = Get-HubEvent -Records $events -Target "ChatCreated"

Check "ChatCreated received by the other participant" { $null -ne $chatCreated }
Check "ChatCreated has exactly one argument" { $chatCreated.arguments.Count -eq 1 }

$evt = $chatCreated.arguments[0]
Check "ChatCreated.chatId matches created chat" { $evt.chatId -eq $chatId }
Check "ChatCreated.type = private" { $evt.type -eq "private" }
Check "ChatCreated.title is null" { $null -eq $evt.title }
Check "REGRESSION: ChatCreated.companionId is the CREATOR, not the recipient" {
    if ($evt.companionId -eq $u2.Id) { throw "recipient sees themselves as companion" }
    $evt.companionId -eq $u1.Id
}
Check "ChatCreated.companionUsername is the creator's username" { $evt.companionUsername -eq $u1.Username }
Check "ChatCreated.companionName is set" { -not [string]::IsNullOrEmpty($evt.companionName) }
Check "ChatCreated.lastMessage is null for a fresh chat" { $null -eq $evt.lastMessage }
Check "ChatCreated.unreadCount = 0" { $evt.unreadCount -eq 0 }

Section "2. POST /api/chats/private/{userId} returns a full ChatListItemDto"

Check "201 Created on first call" { $create.Status -eq 201 }
Check "chatId still present (old clients keep working)" { -not [string]::IsNullOrEmpty($chatId) }
Check "companionId = the other user" { $create.Body.companionId -eq $u2.Id }
Check "companionUsername = the other user's username" { $create.Body.companionUsername -eq $u2.Username }
Check "type = private" { $create.Body.type -eq "private" }
Check "unreadCount = 0" { $create.Body.unreadCount -eq 0 }

$again = Api -Method POST -Url "/api/chats/private/$($u2.Id)" -Token $u1.Token
Check "200 OK when the chat already exists" { $again.Status -eq 200 }
Check "same chatId on repeat" { $again.Body.chatId -eq $chatId }
Check "repeat response also carries companionId" { $again.Body.companionId -eq $u2.Id }

Section "3. GET /api/chats/{chatId}/item"

$item1 = Api -Url "/api/chats/$chatId/item" -Token $u1.Token
Check "200 for a member" { $item1.Status -eq 200 }
Check "companion resolved for the caller (user1 sees user2)" { $item1.Body.companionId -eq $u2.Id }
Check "companionUsername present" { $item1.Body.companionUsername -eq $u2.Username }

$item2 = Api -Url "/api/chats/$chatId/item" -Token $u2.Token
Check "companion is viewer-specific (user2 sees user1)" { $item2.Body.companionId -eq $u1.Id }

$itemForbidden = Api -Url "/api/chats/$chatId/item" -Token $u3.Token
Check "403 for a non-member" { $itemForbidden.Status -eq 403 }

$itemMissing = Api -Url "/api/chats/$([Guid]::NewGuid())/item" -Token $u1.Token
Check "404 for a missing chat" { $itemMissing.Status -eq 404 }

Section "4. Companion fields in chat list and chat search"

$list = Api -Url "/api/chats" -Token $u1.Token
$listed = $list.Body | Where-Object { $_.chatId -eq $chatId }
Check "GET /api/chats exposes companionId" { $listed.companionId -eq $u2.Id }
Check "GET /api/chats exposes companionUsername" { $listed.companionUsername -eq $u2.Username }

$search = Api -Url "/api/chats/search?q=CT+User+2&type=private&limit=20" -Token $u1.Token
$found = $search.Body.items | Where-Object { $_.chatId -eq $chatId }
Check "search returns the chat" { $null -ne $found }
Check "REGRESSION: search keeps companionId" { $found.companionId -eq $u2.Id }
Check "REGRESSION: search keeps companionUsername" { $found.companionUsername -eq $u2.Username }

Section "5. GET /api/users/{userId}/status"

$s2 = Api -Url "/api/users/$($u2.Id)/status" -Token $u1.Token
Check "200 for a chat companion" { $s2.Status -eq 200 }
Check "user2 reported online while its socket is open" { $s2.Body.isOnline -eq $true }
Check "response carries the requested userId" { $s2.Body.userId -eq $u2.Id }

$sSelf = Api -Url "/api/users/$($u1.Id)/status" -Token $u1.Token
Check "200 for self" { $sSelf.Status -eq 200 }

$sStranger = Api -Url "/api/users/$($u3.Id)/status" -Token $u1.Token
Check "404 for a user with no shared chat" { $sStranger.Status -eq 404 }

Section "6. POST /api/users/status (batch)"

$batch = Api -Method POST -Url "/api/users/status" -Token $u1.Token -Body @{ userIds = @($u2.Id, $u3.Id) }
Check "200 OK" { $batch.Status -eq 200 }
Check "stranger filtered out of the response" { @($batch.Body.items | Where-Object { $_.userId -eq $u3.Id }).Count -eq 0 }
Check "companion present and online" { @($batch.Body.items | Where-Object { $_.userId -eq $u2.Id })[0].isOnline -eq $true }

$dup = Api -Method POST -Url "/api/users/status" -Token $u1.Token -Body @{ userIds = @($u2.Id, $u2.Id, $u2.Id) }
Check "duplicate ids collapsed" { @($dup.Body.items).Count -eq 1 }

$empty = Api -Method POST -Url "/api/users/status" -Token $u1.Token -Body @{ userIds = @() }
Check "400 on empty userIds" { $empty.Status -eq 400 }

$manyIds = @(1..201 | ForEach-Object { [Guid]::NewGuid().ToString() })
$tooMany = Api -Method POST -Url "/api/users/status" -Token $u1.Token -Body @{ userIds = $manyIds }
Check "400 when over the 200-id limit" { $tooMany.Status -eq 400 }

Section "7. Critical path: message -> list item -> presence goes offline"

# user1 opens a socket, joins the chat and sends a message
$ws1 = Connect-Hub -Token $u1.Token
Send-Hub -Ws $ws1 -Payload (@{ type = 1; target = "JoinChat"; arguments = @($chatId) } | ConvertTo-Json -Compress)
Start-Sleep -Milliseconds 500
Send-Hub -Ws $ws1 -Payload (@{ type = 1; target = "SendMessage"; arguments = @($chatId, "hello from user1") } | ConvertTo-Json -Compress)

$u2Events = Receive-Hub -Ws $ws2 -TimeoutMs 6000
Check "user2 receives ChatListUpdated" { $null -ne (Get-HubEvent -Records $u2Events -Target "ChatListUpdated") }

$afterMsg = Api -Url "/api/chats/$chatId/item" -Token $u2.Token
Check "/item shows the last message" { $afterMsg.Body.lastMessage.text -eq "hello from user1" }
Check "/item last message carries chatId" { $afterMsg.Body.lastMessage.chatId -eq $chatId }
Check "/item shows unreadCount for the recipient" { $afterMsg.Body.unreadCount -ge 1 }

# user2 disconnects -> presence must flip to an explicit false
Close-Hub -Ws $ws2
Start-Sleep -Seconds 2

$offline = Api -Url "/api/users/$($u2.Id)/status" -Token $u1.Token
Check "single status returns explicit isOnline=false after disconnect" {
    ($offline.Status -eq 200) -and ($offline.Body.isOnline -eq $false)
}

$offlineBatch = Api -Method POST -Url "/api/users/status" -Token $u1.Token -Body @{ userIds = @($u2.Id) }
Check "batch reports offline user explicitly (not omitted)" {
    $row = @($offlineBatch.Body.items | Where-Object { $_.userId -eq $u2.Id })
    ($row.Count -eq 1) -and ($row[0].isOnline -eq $false)
}

$legacy = Api -Url "/api/users/status" -Token $u1.Token
Check "legacy GET /api/users/status still omits offline users" {
    ($legacy.Status -eq 200) -and (@($legacy.Body.items | Where-Object { $_.userId -eq $u2.Id }).Count -eq 0)
}

Close-Hub -Ws $ws1

# ============================================================
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
