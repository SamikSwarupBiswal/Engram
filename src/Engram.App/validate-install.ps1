# ============================================================
# Engram Post-Install Validation Script
# Run this after installing Engram on a clean machine
# ============================================================

param(
    [string]$BaseUrl = "http://127.0.0.1:5000",
    [int]$RequestCount = 50,
    [switch]$Full
)

$ErrorActionPreference = "Continue"
$results = @()
$startTime = Get-Date

function Write-Check($name, $passed, $detail = "") {
    $status = if ($passed) { "PASS" } else { "FAIL" }
    $color = if ($passed) { "Green" } else { "Red" }
    Write-Host "  [$status] $name" -ForegroundColor $color
    if ($detail) { Write-Host "        $detail" -ForegroundColor Gray }
    $script:results += @{ Name = $name; Passed = $passed; Detail = $detail }
}

function Wait-ForState($targetState, $timeoutSeconds = 120) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSeconds) {
        try {
            $health = Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 3
            if ($health.state -eq $targetState) { return $health }
            if ($health.state -eq "Error") { return $health }
            Write-Host "    Waiting... state=$($health.state)" -ForegroundColor DarkGray
        } catch {
            Write-Host "    Waiting for API..." -ForegroundColor DarkGray
        }
        Start-Sleep -Seconds 3
    }
    return $null
}

Write-Host ""
Write-Host "  ENGRAM POST-INSTALL VALIDATION" -ForegroundColor Cyan
Write-Host "  ===============================" -ForegroundColor Cyan
Write-Host ""

# ── Check 1: API Reachable ──
Write-Host "[1/8] API Reachable" -ForegroundColor Yellow
try {
    $root = Invoke-RestMethod "$BaseUrl/" -TimeoutSec 5
    Write-Check "API responds" ($root.service -eq "Engram API") "service=$($root.service)"
} catch {
    Write-Check "API responds" $false "Connection failed: $_"
    Write-Host ""
    Write-Host "  FATAL: API not reachable. Check sidecar process." -ForegroundColor Red
    Write-Host "  Look for Engram.Api.exe in Task Manager." -ForegroundColor Red
    exit 1
}

# ── Check 2: Lifecycle States ──
Write-Host ""
Write-Host "[2/8] Startup Lifecycle" -ForegroundColor Yellow
$health = Wait-ForState "Ready" 180
if ($health) {
    Write-Check "Reached Ready state" ($health.state -eq "Ready") "state=$($health.state)"
    Write-Check "Not false-ready" ($health.state -ne "Ready" -or $health.modelLoaded) "modelLoaded=$($health.modelLoaded)"
    Write-Check "State history includes DetectingBackend" ($health.stateHistory | Where-Object { $_ -match "DetectingBackend" }) "history=$($health.stateHistory -join ', ')"
    Write-Check "State history includes LoadingModel" ($health.stateHistory | Where-Object { $_ -match "LoadingModel" }) ""
} else {
    Write-Check "Reached Ready state" $false "Timed out after 180s"
}

# ── Check 3: Backend Detection ──
Write-Host ""
Write-Host "[3/8] Backend Detection" -ForegroundColor Yellow
if ($health) {
    Write-Check "Backend detected" (-not [string]::IsNullOrEmpty($health.backend)) "backend=$($health.backend)"
    Write-Check "GPU info available" ($null -ne $health.metadata.gpuDevice) "device=$($health.metadata.gpuDevice)"
}

# ── Check 4: First Inference ──
Write-Host ""
Write-Host "[4/8] First Inference" -ForegroundColor Yellow
try {
    $body = @{
        messages = @(@{ role = "user"; content = "Say hello in one word." })
        maxTokens = 10
    } | ConvertTo-Json -Depth 3

    $response = Invoke-RestMethod "$BaseUrl/v1/chat/completions" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 60
    $content = $response.choices[0].message.content
    $hasContent = -not [string]::IsNullOrWhiteSpace($content)
    Write-Check "Inference returns content" $hasContent "response='$content'"
    Write-Check "KV telemetry present" ($null -ne $response._kv) "cleanup=$($response._kv.cleanupResult)"
    Write-Check "Cleanup succeeded" ($response._kv.cleanupResult -eq "Success") "result=$($response._kv.cleanupResult)"
    Write-Check "KV reset to 0" ($response._kv.tokensAfterCleanup -eq 0) "tokens=$($response._kv.tokensAfterCleanup)"
} catch {
    Write-Check "First inference" $false "Error: $_"
}

# ── Check 5: Cleanup Telemetry ──
Write-Host ""
Write-Host "[5/8] Cleanup Telemetry" -ForegroundColor Yellow
try {
    $diag = Invoke-RestMethod "$BaseUrl/api/diagnostics/export" -TimeoutSec 10
    $cleanup = $diag.cleanup
    Write-Check "Cleanup telemetry available" ($null -ne $cleanup) ""
    if ($cleanup) {
        Write-Check "Cleanup success rate" ($cleanup.successRate -ge 0.99) "rate=$([math]::Round($cleanup.successRate * 100, 1))%"
        Write-Check "No verification failures" ($cleanup.verificationFailures -eq 0) "failures=$($cleanup.verificationFailures)"
        Write-Check "No cleanup failures" ($cleanup.failedCleanups -eq 0) "failures=$($cleanup.failedCleanups)"
    }
    $surv = $diag.survivability
    if ($surv) {
        Write-Check "Runtime operational" $surv.runtimeOperational ""
        Write-Check "No consecutive failures" ($surv.consecutiveFailures -eq 0) "count=$($surv.consecutiveFailures)"
    }
} catch {
    Write-Check "Diagnostics export" $false "Error: $_"
}

# ── Check 6: Soak Test (50 requests) ──
Write-Host ""
Write-Host "[6/8] Soak Test ($RequestCount requests)" -ForegroundColor Yellow
$soakSuccess = 0
$soakFail = 0
$kvNotReset = 0

for ($i = 0; $i -lt $RequestCount; $i++) {
    try {
        $body = @{
            messages = @(@{ role = "user"; content = "Count to 3." })
            maxTokens = 20
        } | ConvertTo-Json -Depth 3

        $response = Invoke-RestMethod "$BaseUrl/v1/chat/completions" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 120
        
        if ($response.choices[0].finish_reason -eq "stop") {
            $soakSuccess++
            if ($response._kv.tokensAfterCleanup -gt 0) {
                $kvNotReset++
            }
        } else {
            $soakFail++
        }
    } catch {
        $soakFail++
    }

    if (($i + 1) % 10 -eq 0) {
        Write-Host "    Request $($i + 1)/$RequestCount (success=$soakSuccess, fail=$soakFail)" -ForegroundColor DarkGray
    }
}

$soakRate = $soakSuccess / $RequestCount
Write-Check "Soak success rate >= 95%" ($soakRate -ge 0.95) "rate=$([math]::Round($soakRate * 100, 1))% ($soakSuccess/$RequestCount)"
Write-Check "KV reset every time" ($kvNotReset -eq 0) "misses=$kvNotReset/$RequestCount"
Write-Check "No collapse (old bug)" ($soakSuccess -gt 33) "survived $soakSuccess requests (old collapse at 33)"

# ── Check 7: Post-Soak Health ──
Write-Host ""
Write-Host "[7/8] Post-Soak Health" -ForegroundColor Yellow
try {
    $postHealth = Invoke-RestMethod "$BaseUrl/api/health" -TimeoutSec 5
    Write-Check "Still Ready after soak" ($postHealth.state -eq "Ready") "state=$($postHealth.state)"
    Write-Check "Runtime still operational" $postHealth.runtimeOperational ""
    Write-Check "Consecutive failures = 0" ($postHealth.consecutiveFailures -eq 0) "count=$($postHealth.consecutiveFailures)"
} catch {
    Write-Check "Post-soak health" $false "Error: $_"
}

# ── Check 8: Environmental Degradation and Transparency ──
Write-Host ""
Write-Host "[8/8] Environmental Degradation and Transparency" -ForegroundColor Yellow
try {
    $transparency = Invoke-RestMethod "$BaseUrl/api/health/transparency" -TimeoutSec 5
    Write-Check "Transparency profile available" ($null -ne $transparency) ""
    if ($transparency) {
        Write-Check "Environmental Confidence score present" ($null -ne $transparency.environmentalConfidence) "confidence=$($transparency.environmentalConfidence)"
        Write-Check "Safe Mode status reported" ($null -ne $transparency.safeModeActive) "safeModeActive=$($transparency.safeModeActive)"
        Write-Host "    Active Degradations: $($transparency.activeDegradations.Keys -join ', ')" -ForegroundColor Gray
        Write-Host "    Sandbox Root: $($transparency.sandboxRoot)" -ForegroundColor Gray
    }
} catch {
    Write-Check "Transparency check" $false "Error: $_"
}

# ── Summary ──
$elapsed = (Get-Date) - $startTime
$passed = ($results | Where-Object { $_.Passed }).Count
$failed = ($results | Where-Object { -not $_.Passed }).Count
$total = $results.Count

Write-Host ""
Write-Host "  ===============================" -ForegroundColor Cyan
Write-Host "  RESULTS: $passed/$total passed" -ForegroundColor $(if ($failed -eq 0) { "Green" } else { "Red" })
Write-Host "  Time: $([math]::Round($elapsed.TotalSeconds, 1))s" -ForegroundColor Gray
Write-Host ""

if ($failed -gt 0) {
    Write-Host "  FAILURES:" -ForegroundColor Red
    $results | Where-Object { -not $_.Passed } | ForEach-Object {
        Write-Host "    - $($_.Name): $($_.Detail)" -ForegroundColor Red
    }
    Write-Host ""
}

# Export diagnostics if requested
if ($Full) {
    $diagPath = "engram-validation-$((Get-Date).ToString('yyyyMMdd-HHmmss')).json"
    try {
        $diag = Invoke-RestMethod "$BaseUrl/api/diagnostics/export" -TimeoutSec 10
        $diag | ConvertTo-Json -Depth 10 | Out-File $diagPath -Encoding UTF8
        Write-Host "  Diagnostics exported to: $diagPath" -ForegroundColor Green
    } catch {
        Write-Host "  Failed to export diagnostics" -ForegroundColor Red
    }
}

exit $(if ($failed -eq 0) { 0 } else { 1 })
