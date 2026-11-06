# Drives headless Chrome over the DevTools protocol to check the app actually mounts and renders.
param(
    [string]$Url = 'http://localhost:5173/',
    [string]$Script = 'document.body.innerText'
)

$target = (Invoke-RestMethod 'http://localhost:9222/json/new?about:blank' -Method Put)
$wsUrl = $target.webSocketDebuggerUrl
$targetId = $target.id

$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$socket.ConnectAsync([Uri]$wsUrl, [Threading.CancellationToken]::None).Wait()

$script:id = 0
function Send-Cdp([string]$method, [hashtable]$params) {
    $script:id++
    $payload = @{ id = $script:id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $socket.SendAsync([ArraySegment[byte]]::new($bytes), 'Text', $true, [Threading.CancellationToken]::None).Wait()
    return $script:id
}

function Receive-Cdp([int]$waitFor, [int]$timeoutSeconds = 60) {
    $buffer = [byte[]]::new(65536)
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    $logs = @()
    while ((Get-Date) -lt $deadline) {
        # A single CDP message can arrive as several frames, so collect until the end flag.
        $builder = [Text.StringBuilder]::new()
        $complete = $false
        while (-not $complete) {
            $segment = [ArraySegment[byte]]::new($buffer)
            $task = $socket.ReceiveAsync($segment, [Threading.CancellationToken]::None)
            # Never start a second receive while one is outstanding, so wait the whole budget here.
            if (-not $task.Wait([int]([Math]::Max(1000, ($deadline - (Get-Date)).TotalMilliseconds)))) {
                return @{ Result = $null; Logs = $logs }
            }
            [void]$builder.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $task.Result.Count))
            $complete = $task.Result.EndOfMessage
        }
        if (-not $complete) { continue }

        $message = $null
        try { $message = $builder.ToString() | ConvertFrom-Json } catch { continue }
        if ($message.method -eq 'Runtime.consoleAPICalled' -and $message.params.type -in @('error', 'warning')) {
            $logs += "CONSOLE $($message.params.type): " + (($message.params.args | ForEach-Object { $_.value }) -join ' ')
        }
        if ($message.method -eq 'Runtime.exceptionThrown') {
            $logs += 'EXCEPTION: ' + $message.params.exceptionDetails.exception.description
        }
        if ($message.id -eq $waitFor) { return @{ Result = $message; Logs = $logs } }
    }
    return @{ Result = $null; Logs = $logs }
}

Send-Cdp 'Runtime.enable' @{} | Out-Null
Send-Cdp 'Page.enable' @{} | Out-Null
Send-Cdp 'Page.navigate' @{ url = $Url } | Out-Null
Start-Sleep -Seconds 8

$evalId = Send-Cdp 'Runtime.evaluate' @{ expression = $Script; returnByValue = $true; awaitPromise = $true }
$answer = Receive-Cdp $evalId

Write-Output '===== OUTPUT ====='
if ($answer.Result) {
    if ($answer.Result.result.exceptionDetails) {
        Write-Output ('SCRIPT ERROR: ' + $answer.Result.result.exceptionDetails.exception.description)
    }
    Write-Output $answer.Result.result.result.value
} else { Write-Output '(no result)' }
Write-Output '===== CONSOLE ====='
if ($answer.Logs.Count -eq 0) { Write-Output '(clean)' } else { $answer.Logs | ForEach-Object { Write-Output $_ } }

$socket.Dispose()
Invoke-RestMethod "http://localhost:9222/json/close/$targetId" | Out-Null
