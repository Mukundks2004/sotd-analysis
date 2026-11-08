<#
.SYNOPSIS
    Says whether Spotify is currently willing to answer this app, so you know when it is worth starting an import.

.DESCRIPTION
    Spotify's rate limit is a rolling window, not a daily allowance, and it never tells you up front how much
    of the window is left. The only honest way to know whether a penalty has expired is to ask it one cheap
    question and look at the answer. This sends a single search and reports one of three things:

      ready      - the app is being served, go ahead and import
      penalised  - still rate limited, with how long Spotify says is left
      broken     - credentials or network problem, which is not a rate limit at all

    It costs exactly one request, so it is safe to run repeatedly while you wait.

.EXAMPLE
    .\check-spotify.ps1
    .\check-spotify.ps1 -Watch
#>

[CmdletBinding()]
param(
    [string]$ConfigPath,
    [switch]$Watch,
    [int]$IntervalSeconds = 300
)

$ErrorActionPreference = 'Stop'

# Calculate the value of the variable $ConfigPath used to get configs
if (-not $ConfigPath) {
    $root = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $ConfigPath = Join-Path $root '..\Importer\config.json'
}

# If config is not found exit early
if (-not (Test-Path $ConfigPath)) {
    Write-Host "no config at $ConfigPath" -ForegroundColor Red
    exit 2
}

# Read config and error if we don't have spotify client or spotify client secret
$config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
if (-not $config.SpotifyClientId -or -not $config.SpotifyClientSecret) {
    Write-Host "config.json has no SpotifyClientId / SpotifyClientSecret" -ForegroundColor Red
    exit 2
}

# Default market to US if it is not in the config
$market = if ($config.SpotifyMarket) { $config.SpotifyMarket } else { 'AU' }

function Test-Spotify {
    $pair = "$($config.SpotifyClientId):$($config.SpotifyClientSecret)"
    $basic = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair))

    # Generate token to make request- if this fails it is unrelated to rate limiting
    try {
        $token = Invoke-RestMethod -Method Post -Uri 'https://accounts.spotify.com/api/token' `
            -Headers @{ Authorization = "Basic $basic" } `
            -Body @{ grant_type = 'client_credentials' }
    }
    catch {
        return [pscustomobject]@{ State = 'broken'; Detail = "could not get a token: $($_.Exception.Message)" }
    }

    # Make a basic request using the token
    try {
        $null = Invoke-RestMethod -Method Get -Uri "https://api.spotify.com/v1/search?type=track&limit=1&market=$market&q=test" `
            -Headers @{ Authorization = "Bearer $($token.access_token)" }
        return [pscustomobject]@{ State = 'ready'; Detail = 'Spotify answered a search normally' }
    }
    catch {
        $resp = $_.Exception.Response

        # If this fails with status '429', we are being rate limited: ready the 'Retry-After' header and print the value
        if ($resp -and [int]$resp.StatusCode -eq 429) {
            $retry = $resp.Headers['Retry-After']
            $seconds = 0
            if ($retry -and [int]::TryParse($retry, [ref]$seconds) -and $seconds -gt 0) {
                $until = (Get-Date).AddSeconds($seconds)
                $human = if ($seconds -ge 3600) { "{0:N1} hours" -f ($seconds / 3600) }
                         elseif ($seconds -ge 60) { "{0:N0} minutes" -f ($seconds / 60) }
                         else { "$seconds seconds" }
                return [pscustomobject]@{
                    State   = 'penalised'
                    Detail  = "rate limited for another $human (until about $($until.ToString('HH:mm')))"
                    Seconds = $seconds
                }
            }

            # Case for if we are unable to parse a time limit from the '429' response
            return [pscustomobject]@{ State = 'penalised'; Detail = 'rate limited, no Retry-After given'; Seconds = 0 }
        }

        # Case for if response is not a '429'- we don't expect this to happen
        return [pscustomobject]@{ State = 'broken'; Detail = $_.Exception.Message }
    }
}

while ($true) {
    $result = Test-Spotify
    $stamp = (Get-Date).ToString('HH:mm:ss')
    switch ($result.State) {
        'ready' {
            Write-Host "$stamp  READY - $($result.Detail)" -ForegroundColor Green
            Write-Host "         start the import with: dotnet run --project Importer"
            exit 0
        }
        'penalised' {
            Write-Host "$stamp  WAIT - $($result.Detail)" -ForegroundColor Yellow

            # If the watch flag is set, this will not exit- instead it will wait the interval time (default 300) and then try again
            if (-not $Watch) { exit 1 }
        }
        'broken' {
            Write-Host "$stamp  PROBLEM - $($result.Detail)" -ForegroundColor Red
            Write-Host "         this is not a rate limit, so waiting will not fix it"
            exit 2
        }
    }
    Start-Sleep -Seconds $IntervalSeconds
}
