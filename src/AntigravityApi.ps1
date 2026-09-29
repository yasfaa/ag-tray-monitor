# Antigravity Local API Client (PowerShell 5.1 & 7+ compatible)
# Discovers running Antigravity Language Server or automatically launches headless daemon

Add-Type -AssemblyName System.Net.Http

# Ignore SSL certificate errors for localhost self-signed certs
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$script:CachedEndpoint = $null
$script:CachedCsrf = $null
$script:SpawnedDaemonProcess = $null

function Format-RemainingTime($isoDate) {
    if (-not $isoDate) { return "Full" }
    try {
        $target = [DateTime]::Parse($isoDate).ToUniversalTime()
        $now = [DateTime]::UtcNow
        $diff = $target - $now
        if ($diff.TotalSeconds -le 0) {
            return "Ready to reset"
        }
        if ($diff.TotalDays -ge 1) {
            $days = [math]::Floor($diff.TotalDays)
            $hours = $diff.Hours
            return "${days}d ${hours}h"
        }
        if ($diff.TotalHours -ge 1) {
            $hours = $diff.Hours
            $mins = $diff.Minutes
            return "${hours}h ${mins}m"
        }
        $mins = [math]::Max(1, $diff.Minutes)
        return "${mins}m"
    } catch {
        return "$isoDate"
    }
}

function Invoke-AntigravityRpc($baseUrl, $path, $csrf, $bodyObj) {
    $bodyJson = if ($bodyObj) { $bodyObj | ConvertTo-Json -Compress } else { '{}' }
    $headers = @{
        'Content-Type' = 'application/json'
        'Connect-Protocol-Version' = '1'
        'X-Codeium-Csrf-Token' = $csrf
    }
    $url = "$baseUrl$path"
    return Invoke-RestMethod -Uri $url -Method Post -Headers $headers -Body $bodyJson -TimeoutSec 3
}

function Find-LanguageServerBinary {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\antigravity\resources\bin\language_server.exe",
        "$env:PROGRAMFILES\Antigravity\resources\bin\language_server.exe",
        "${env:ProgramFiles(x86)}\Antigravity\resources\bin\language_server.exe",
        "$env:LOCALAPPDATA\Programs\antigravity-ide\resources\bin\language_server.exe",
        "$env:USERPROFILE\.gemini\antigravity\resources\bin\language_server.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Start-HeadlessAntigravityDaemon {
    # If a previously spawned daemon is still running, reuse it
    if ($script:SpawnedDaemonProcess -and (-not $script:SpawnedDaemonProcess.HasExited) -and $script:CachedEndpoint -and $script:CachedCsrf) {
        try {
            $test = Invoke-AntigravityRpc $script:CachedEndpoint "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary" $script:CachedCsrf $null
            if ($test -and $test.response -and $test.response.groups) {
                return @{
                    BaseUrl = $script:CachedEndpoint
                    Csrf = $script:CachedCsrf
                    InitialQuota = $test
                }
            }
        } catch {}
    }

    $bin = Find-LanguageServerBinary
    if (-not $bin) { return $null }

    $csrf = [System.Guid]::NewGuid().ToString()
    $daemonArgs = @(
        "--standalone",
        "--override_ide_name", "antigravity",
        "--subclient_type", "hub",
        "--override_ide_version", "2.17.0",
        "--override_user_agent_name", "antigravity",
        "--https_server_port", "0",
        "--csrf_token", $csrf,
        "--app_data_dir", "antigravity",
        "--api_server_url", "https://generativelanguage.googleapis.com",
        "--cloud_code_endpoint", "https://daily-cloudcode-pa.googleapis.com"
    )

    try {
        $proc = Start-Process -FilePath $bin -ArgumentList $daemonArgs -PassThru -WindowStyle Hidden
        $script:SpawnedDaemonProcess = $proc
        $script:CachedCsrf = $csrf

        # Give process brief moment to initialize port
        Start-Sleep -Milliseconds 1500

        $ports = Get-NetTCPConnection -OwningProcess $proc.Id -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort
        foreach ($port in ($ports | Select-Object -Unique)) {
            foreach ($proto in @('https', 'http')) {
                $baseUrl = "$proto`://127.0.0.1:$port"
                try {
                    $test = Invoke-AntigravityRpc $baseUrl "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary" $csrf $null
                    if ($test -and $test.response -and $test.response.groups) {
                        $script:CachedEndpoint = $baseUrl
                        return @{
                            BaseUrl = $baseUrl
                            Csrf = $csrf
                            InitialQuota = $test
                        }
                    }
                } catch {}
            }
        }
    } catch {}

    return $null
}

function Stop-HeadlessAntigravityDaemon {
    if ($script:SpawnedDaemonProcess -and (-not $script:SpawnedDaemonProcess.HasExited)) {
        try {
            Stop-Process -Id $script:SpawnedDaemonProcess.Id -Force -ErrorAction SilentlyContinue
        } catch {}
        $script:SpawnedDaemonProcess = $null
    }
}

function Find-AntigravityConnection {
    # 1. Test cached connection first if present
    if ($script:CachedEndpoint -and $script:CachedCsrf) {
        try {
            $test = Invoke-AntigravityRpc $script:CachedEndpoint "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary" $script:CachedCsrf $null
            if ($test -and $test.response -and $test.response.groups) {
                return @{
                    BaseUrl = $script:CachedEndpoint
                    Csrf = $script:CachedCsrf
                    InitialQuota = $test
                }
            }
        } catch {
            $script:CachedEndpoint = $null
            $script:CachedCsrf = $null
        }
    }

    # 2. Query Windows processes for Antigravity language_server or hub-port
    $procs = Get-CimInstance Win32_Process -Filter "CommandLine LIKE '%language_server%' OR CommandLine LIKE '%--hub-port%'" -ErrorAction SilentlyContinue

    if ($procs) {
        # Sort candidates prioritizing standalone Antigravity
        $sortedProcs = $procs | Sort-Object -Property @{
            Expression = {
                $cmd = ""
                if ($_.CommandLine) { $cmd = $_.CommandLine.ToLower() }
                $score = 0
                if ($cmd -like "*--standalone*") { $score += 10 }
                if ($cmd -like "*antigravity\resources\bin\language_server.exe*") { $score += 8 }
                if ($cmd -like "*--csrf_token*") { $score += 5 }
                if ($cmd -like "*--hub-port*") { $score += 4 }
                $score
            }
        } -Descending

        foreach ($p in $sortedProcs) {
            $cmd = $p.CommandLine
            if (-not $cmd) { continue }

            $csrf = $null
            if ($cmd -match '--csrf_token[=\s]+"([^"]+)"' -or $cmd -match "--csrf_token[=\s]+'([^']+)'" -or $cmd -match '--csrf_token[=\s]+([^\s"'']+)') {
                $csrf = $matches[1].Trim()
            }

            $hubPort = $null
            if ($cmd -match '--hub-port(?:=|\s+)(?:"(\d{1,5})"|''(\d{1,5})''|(\d{1,5}))') {
                $val = ($matches[1], $matches[2], $matches[3] | Where-Object { $_ })[0]
                $hubPort = [int]$val
            }

            $ports = @()
            if ($csrf) {
                $ports = Get-NetTCPConnection -OwningProcess $p.ProcessId -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty LocalPort
            } elseif ($hubPort) {
                try {
                    $hubHtml = (Invoke-WebRequest -Uri "http://127.0.0.1:$hubPort/" -UseBasicParsing -TimeoutSec 2).Content
                    if ($hubHtml -match '__APP_CONFIG__\s*=\s*(\{.*?\})\s*;') {
                        $json = $matches[1] | ConvertFrom-Json
                        if ($json.csrfToken) {
                            $csrf = $json.csrfToken
                            $ports = @($hubPort)
                        }
                    }
                } catch {}
            }

            if ($csrf -and $ports.Count -gt 0) {
                $uniquePorts = $ports | Select-Object -Unique
                foreach ($port in $uniquePorts) {
                    foreach ($proto in @('https', 'http')) {
                        $baseUrl = "$proto`://127.0.0.1:$port"
                        try {
                            $test = Invoke-AntigravityRpc $baseUrl "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary" $csrf $null
                            if ($test -and $test.response -and $test.response.groups) {
                                $script:CachedEndpoint = $baseUrl
                                $script:CachedCsrf = $csrf
                                return @{
                                    BaseUrl = $baseUrl
                                    Csrf = $csrf
                                    InitialQuota = $test
                                }
                            }
                        } catch {}
                    }
                }
            }
        }
    }

    # 3. If no active process found, auto-spawn headless background daemon
    $headless = Start-HeadlessAntigravityDaemon
    if ($headless) {
        return $headless
    }

    return $null
}

function Get-AntigravityUsageData {
    $conn = Find-AntigravityConnection
    if (-not $conn) {
        return @{
            IsConnected = $false
            Error = "Antigravity language_server not found. Ensure Antigravity is installed."
            LastUpdated = [DateTime]::Now
        }
    }

    $baseUrl = $conn.BaseUrl
    $csrf = $conn.Csrf

    # 1. Fetch Quota Summary
    $quotaData = $conn.InitialQuota
    if (-not $quotaData) {
        try {
            $quotaData = Invoke-AntigravityRpc $baseUrl "/exa.language_server_pb.LanguageServerService/RetrieveUserQuotaSummary" $csrf $null
        } catch {
            return @{
                IsConnected = $false
                Error = "Failed to fetch quota: $($_.Exception.Message)"
                LastUpdated = [DateTime]::Now
            }
        }
    }

    # 2. Fetch User Status
    $userStatus = $null
    try {
        $userStatus = Invoke-AntigravityRpc $baseUrl "/exa.language_server_pb.LanguageServerService/GetUserStatus" $csrf @{ metadata = @{ ideName = 'antigravity-ide' } }
    } catch {}

    # Extract user profile
    $userName = "User"
    $userEmail = "Local Antigravity"
    $planName = "Pro"
    if ($userStatus -and $userStatus.userStatus) {
        $us = $userStatus.userStatus
        if ($us.name) { $userName = $us.name }
        if ($us.email) { $userEmail = $us.email }
        if ($us.planStatus -and $us.planStatus.planInfo -and $us.planStatus.planInfo.planName) {
            $planName = $us.planStatus.planInfo.planName
        }
    }

    # Default Quota Groups
    $gemini5h = @{ Pct = 100.0; Remaining = 1.0; ResetStr = "Full"; ResetTime = $null }
    $geminiWk = @{ Pct = 100.0; Remaining = 1.0; ResetStr = "Full"; ResetTime = $null }
    $claude5h = @{ Pct = 100.0; Remaining = 1.0; ResetStr = "Full"; ResetTime = $null }
    $claudeWk = @{ Pct = 100.0; Remaining = 1.0; ResetStr = "Full"; ResetTime = $null }

    if ($quotaData.response -and $quotaData.response.groups) {
        foreach ($grp in $quotaData.response.groups) {
            $grpName = ""
            if ($grp.displayName) { $grpName = $grp.displayName.ToLower() }
            $isGemini = ($grpName -like "*gemini*") -or ($grpName -like "*flash*")

            foreach ($b in $grp.buckets) {
                $rem = 1.0
                if ($null -ne $b.remainingFraction) { $rem = [double]$b.remainingFraction }
                $pct = [math]::Round($rem * 100, 1)
                $resetStr = Format-RemainingTime $b.resetTime
                $item = @{
                    Pct = $pct
                    Remaining = $rem
                    ResetStr = $resetStr
                    ResetTime = $b.resetTime
                }

                $win = ""
                if ($b.window) { $win = $b.window.ToLower() }
                if ($isGemini) {
                    if ($win -eq "5h") { $gemini5h = $item }
                    elseif ($win -eq "weekly") { $geminiWk = $item }
                } else {
                    if ($win -eq "5h") { $claude5h = $item }
                    elseif ($win -eq "weekly") { $claudeWk = $item }
                }
            }
        }
    }

    return @{
        IsConnected = $true
        BaseUrl = $baseUrl
        UserName = $userName
        UserEmail = $userEmail
        Plan = $planName
        Gemini = @{
            FiveHour = $gemini5h
            Weekly = $geminiWk
        }
        Claude = @{
            FiveHour = $claude5h
            Weekly = $claudeWk
        }
        LastUpdated = [DateTime]::Now
        Error = $null
    }
}
