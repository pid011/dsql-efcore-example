[CmdletBinding()]
param(
    [string]$BaseUrl = "https://localhost:7179",
    [int]$DurationMinutes = 5,
    [int]$IntervalSeconds = 60,
    [int]$TimeoutSeconds = 30,
    [bool]$SkipCertificateCheck = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($DurationMinutes -le 0) {
    throw "DurationMinutes must be greater than 0."
}

if ($IntervalSeconds -le 0) {
    throw "IntervalSeconds must be greater than 0."
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$endAt = (Get-Date).AddMinutes($DurationMinutes)

$endpointMetrics = @{
    create  = @{ total = 0; success = 0; fail = 0 }
    update  = @{ total = 0; success = 0; fail = 0 }
    profile = @{ total = 0; success = 0; fail = 0 }
    list    = @{ total = 0; success = 0; fail = 0 }
}

$summary = @{
    cycles = 0
    total  = 0
    success = 0
    fail = 0
}

$errors = New-Object System.Collections.Generic.List[string]
$random = [System.Random]::new()

function Invoke-Api {
    param(
        [Parameter(Mandatory = $true)][string]$MetricKey,
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [object]$Body = $null
    )

    $summary.total++
    $endpointMetrics[$MetricKey].total++

    try {
        $invokeArgs = @{
            Method = $Method
            Uri = $Uri
            Headers = @{ Accept = "application/json" }
            TimeoutSec = $TimeoutSeconds
            SkipCertificateCheck = $SkipCertificateCheck
        }

        if ($null -ne $Body) {
            $invokeArgs.ContentType = "application/json"
            $invokeArgs.Body = ($Body | ConvertTo-Json -Depth 8 -Compress)
        }

        $response = Invoke-RestMethod @invokeArgs

        $summary.success++
        $endpointMetrics[$MetricKey].success++
        return @{ Ok = $true; Body = $response }
    }
    catch {
        $summary.fail++
        $endpointMetrics[$MetricKey].fail++

        $detail = $_.Exception.Message
        if (-not [string]::IsNullOrWhiteSpace($_.ErrorDetails?.Message)) {
            $detail = "$detail | $($_.ErrorDetails.Message)"
        }

        $errors.Add(("[{0:HH:mm:ss}] {1} {2} failed: {3}" -f (Get-Date), $Method, $Uri, $detail))
        return @{ Ok = $false; Body = $null }
    }
}

Write-Host "Token rotation probe started."
Write-Host "BaseUrl: $BaseUrl"
Write-Host "DurationMinutes: $DurationMinutes"
Write-Host "IntervalSeconds: $IntervalSeconds"
Write-Host "EndAt: $endAt"

while ((Get-Date) -lt $endAt) {
    $cycleStart = Get-Date
    $summary.cycles++

    Write-Host ""
    Write-Host ("[{0:HH:mm:ss}] Cycle #{1} started" -f $cycleStart, $summary.cycles)

    $name = "token-probe-$($summary.cycles)-$($random.Next(1000, 9999))"
    $create = Invoke-Api -MetricKey "create" -Method "POST" -Uri "$BaseUrl/players" -Body @{ name = $name }

    $playerId = $null
    if ($create.Ok -and $null -ne $create.Body -and $null -ne $create.Body.id) {
        $playerId = [string]$create.Body.id
        Write-Host "Created player: $playerId"
    }
    else {
        Write-Host "Create player failed. Update/Profile calls skipped for this cycle."
    }

    if (-not [string]::IsNullOrWhiteSpace($playerId)) {
        $matches = [Math]::Max($summary.cycles, 1)
        $wins = $random.Next(0, $matches + 1)
        $losses = $matches - $wins

        $updateBody = @{
            matchesPlayed = $matches
            wins = $wins
            losses = $losses
            draws = 0
            currentWinStreak = [Math]::Min($wins, 5)
            bestWinStreak = [Math]::Min($wins, 10)
            totalKills = $matches * 3
            totalDeaths = $matches
            totalAssists = $matches * 2
            totalScore = $matches * 100
            rating = 1000 + ($wins * 5)
            highestRating = 1000 + ($wins * 5)
            lastMatchAt = (Get-Date).ToUniversalTime().ToString("o")
        }

        Invoke-Api -MetricKey "update" -Method "PUT" -Uri "$BaseUrl/players/$playerId/stats" -Body $updateBody | Out-Null
        Invoke-Api -MetricKey "profile" -Method "GET" -Uri "$BaseUrl/players/$playerId/profile" | Out-Null
    }

    Invoke-Api -MetricKey "list" -Method "GET" -Uri "$BaseUrl/players" | Out-Null

    $now = Get-Date
    if ($now -ge $endAt) {
        break
    }

    $nextAt = $cycleStart.AddSeconds($IntervalSeconds)
    if ($nextAt -gt $now) {
        $sleepSeconds = [int][Math]::Ceiling(($nextAt - $now).TotalSeconds)
        Write-Host ("Sleeping {0}s (next cycle at {1:HH:mm:ss})" -f $sleepSeconds, $nextAt)
        Start-Sleep -Seconds $sleepSeconds
    }
}

Write-Host ""
Write-Host "Token rotation probe completed."
Write-Host ("Cycles: {0}" -f $summary.cycles)
Write-Host ("Total Calls: {0}, Success: {1}, Fail: {2}" -f $summary.total, $summary.success, $summary.fail)
Write-Host ""
Write-Host "Per endpoint:"
Write-Host ("- create  total={0}, success={1}, fail={2}" -f $endpointMetrics.create.total, $endpointMetrics.create.success, $endpointMetrics.create.fail)
Write-Host ("- update  total={0}, success={1}, fail={2}" -f $endpointMetrics.update.total, $endpointMetrics.update.success, $endpointMetrics.update.fail)
Write-Host ("- profile total={0}, success={1}, fail={2}" -f $endpointMetrics.profile.total, $endpointMetrics.profile.success, $endpointMetrics.profile.fail)
Write-Host ("- list    total={0}, success={1}, fail={2}" -f $endpointMetrics.list.total, $endpointMetrics.list.success, $endpointMetrics.list.fail)

if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Host "Failures:"
    foreach ($line in $errors) {
        Write-Host "- $line"
    }
}
