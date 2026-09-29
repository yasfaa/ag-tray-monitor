# Antigravity Taskbar System Tray Monitor
# Clean, accessible, native Windows desktop monitor for Antigravity AI quotas

[CmdletBinding()]
param(
    [switch]$StartMinimized = $false
)

# 1. Enforce STA (Single-Threaded Apartment) for WPF and WinForms
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# 2. Single-Instance Check with Wakeup Event
$mutexName = "Local\AntigravityTrayMonitor_Mutex"
$eventName = "Local\AntigravityTrayMonitor_Wakeup"
$createdNew = $false
$script:AppMutex = New-Object System.Threading.Mutex($true, $mutexName, [ref]$createdNew)

if (-not $createdNew) {
    # Another instance is already running. Signal it to open the dashboard
    try {
        $existingEvent = [System.Threading.EventWaitHandle]::OpenExisting($eventName)
        $existingEvent.Set() | Out-Null
        $existingEvent.Dispose()
    } catch {}
    exit
}

# Create wakeup event for subsequent launches
$script:WakeupEvent = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::AutoReset, $eventName)

# 3. Resolve Paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$scriptPath = Join-Path $scriptDir "TrayApp.ps1"
$apiScriptPath = Join-Path $scriptDir "AntigravityApi.ps1"
$xamlPath = Join-Path $scriptDir "DashboardWindow.xaml"
$icoPath = Join-Path $projectDir "assets\icon.ico"
$pngPath = Join-Path $projectDir "assets\icon.png"

# Windows Startup Locations
$startupFolder = [Environment]::GetFolderPath('Startup')
$startupShortcutPath = Join-Path $startupFolder "Antigravity Quota Monitor.lnk"
$regPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$regName = "AntigravityTrayMonitor"

function Test-AutoStartEnabled {
    $inFolder = Test-Path $startupShortcutPath
    $inReg = [bool](Get-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue)
    return ($inFolder -or $inReg)
}

function Set-AutoStart($enable) {
    if ($enable) {
        # 1. Add shortcut to Windows Startup folder
        try {
            $wsh = New-Object -ComObject WScript.Shell
            $sc = $wsh.CreateShortcut($startupShortcutPath)
            $sc.TargetPath = "powershell.exe"
            $sc.Arguments = "-Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`""
            $sc.WorkingDirectory = $projectDir
            $sc.Description = "Antigravity Quota Monitor"
            $sc.IconLocation = "$icoPath,0"
            $sc.Save()
        } catch {}

        # 2. Register in HKCU Run registry key for redundancy
        try {
            $cmd = "powershell.exe -Sta -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`""
            Set-ItemProperty -Path $regPath -Name $regName -Value $cmd -ErrorAction SilentlyContinue
        } catch {}
    } else {
        # Remove from Startup folder
        try {
            if (Test-Path $startupShortcutPath) {
                Remove-Item -Path $startupShortcutPath -Force -ErrorAction SilentlyContinue
            }
        } catch {}

        # Remove from Registry
        try {
            Remove-ItemProperty -Path $regPath -Name $regName -ErrorAction SilentlyContinue
        } catch {}
    }
}

# Import API module
. $apiScriptPath

# 4. Color Helper for Quota Levels
function Get-QuotaBrush($fraction) {
    if ($fraction -ge 0.50) {
        return [System.Windows.Media.BrushConverter]::new().ConvertFromString("#10B981") # Emerald Green
    } elseif ($fraction -ge 0.20) {
        return [System.Windows.Media.BrushConverter]::new().ConvertFromString("#F59E0B") # Amber
    } else {
        return [System.Windows.Media.BrushConverter]::new().ConvertFromString("#EF4444") # Red
    }
}

# 5. Load WPF Dashboard Window
[xml]$xamlContent = Get-Content $xamlPath -Raw
$xmlReader = New-Object System.Xml.XmlNodeReader $xamlContent
$window = [System.Windows.Markup.XamlReader]::Load($xmlReader)

# Get Controls
$imgAppIcon    = $window.FindName("imgAppIcon")
$dotStatus     = $window.FindName("dotStatus")
$txtStatus     = $window.FindName("txtStatus")
$btnRefresh    = $window.FindName("btnRefresh")
$btnClose      = $window.FindName("btnClose")
$txtUserName   = $window.FindName("txtUserName")
$txtUserEmail  = $window.FindName("txtUserEmail")
$txtPlanBadge  = $window.FindName("txtPlanBadge")
$txtLastUpdate = $window.FindName("txtLastUpdated")
$chkAutoStart  = $window.FindName("chkAutoStart")

# Gemini Controls
$txtGemini5hPct   = $window.FindName("txtGemini5hPct")
$barGemini5h      = $window.FindName("barGemini5h")
$txtGemini5hReset = $window.FindName("txtGemini5hReset")
$txtGeminiWkPct   = $window.FindName("txtGeminiWkPct")
$barGeminiWk      = $window.FindName("barGeminiWk")
$txtGeminiWkReset = $window.FindName("txtGeminiWkReset")

# Claude Controls
$txtClaude5hPct   = $window.FindName("txtClaude5hPct")
$barClaude5h      = $window.FindName("barClaude5h")
$txtClaude5hReset = $window.FindName("txtClaude5hReset")
$txtClaudeWkPct   = $window.FindName("txtClaudeWkPct")
$barClaudeWk      = $window.FindName("barClaudeWk")
$txtClaudeWkReset = $window.FindName("txtClaudeWkReset")

# Set App Icon in WPF window
if (Test-Path $pngPath) {
    $bitmapImage = New-Object System.Windows.Media.Imaging.BitmapImage
    $bitmapImage.BeginInit()
    $bitmapImage.UriSource = New-Object System.Uri($pngPath)
    $bitmapImage.EndInit()
    $imgAppIcon.Source = $bitmapImage
}

# Position Window above taskbar in bottom-right corner
function Set-FlyoutPosition {
    $workArea = [System.Windows.SystemParameters]::WorkArea
    $window.Left = $workArea.Right - $window.Width - 12
    $window.Top = $workArea.Bottom - $window.Height - 12
}

# 6. Setup Taskbar System Tray Icon (NotifyIcon)
$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
if (Test-Path $icoPath) {
    $notifyIcon.Icon = New-Object System.Drawing.Icon($icoPath)
} else {
    $notifyIcon.Icon = [System.Drawing.SystemIcons]::Application
}
$notifyIcon.Text = "Antigravity Quota Monitor"
$notifyIcon.Visible = $true

# Context Menu
$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip

$menuOpen = $contextMenu.Items.Add("Open Dashboard")
$menuOpen.Font = New-Object System.Drawing.Font($contextMenu.Font, [System.Drawing.FontStyle]::Bold)

$contextMenu.Items.Add("-") | Out-Null

$menuRefresh = $contextMenu.Items.Add("Refresh Usage")
$menuHub = $contextMenu.Items.Add("Open Web Hub")

$menuInterval = New-Object System.Windows.Forms.ToolStripMenuItem("Auto-Refresh Interval")
$int30 = $menuInterval.DropDownItems.Add("30 Seconds")
$int60 = $menuInterval.DropDownItems.Add("60 Seconds (Default)")
$int300 = $menuInterval.DropDownItems.Add("5 Minutes")
$int60.Checked = $true
$contextMenu.Items.Add($menuInterval) | Out-Null

$contextMenu.Items.Add("-") | Out-Null

$menuStartup = New-Object System.Windows.Forms.ToolStripMenuItem("Start with Windows")
$isStartupInit = Test-AutoStartEnabled
$menuStartup.Checked = $isStartupInit
if ($chkAutoStart) { $chkAutoStart.IsChecked = $isStartupInit }
$contextMenu.Items.Add($menuStartup) | Out-Null

$contextMenu.Items.Add("-") | Out-Null

$menuExit = $contextMenu.Items.Add("Exit")

$notifyIcon.ContextMenuStrip = $contextMenu

# 7. Core Update Routine
$script:CurrentHubUrl = "https://antigravity.google"
$maxBarWidth = 308.0

function Update-DashboardUI {
    try {
        $data = Get-AntigravityUsageData
        $brushGreen = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#10B981")
        $brushAmber = [System.Windows.Media.BrushConverter]::new().ConvertFromString("#F59E0B")

        if ($data.IsConnected) {
            $dotStatus.Fill = $brushGreen
            $txtStatus.Text = "Connected"
            $txtStatus.Foreground = $brushGreen

            if ($data.BaseUrl) { $script:CurrentHubUrl = $data.BaseUrl }
            $txtUserName.Text = $data.UserName
            $txtUserEmail.Text = $data.UserEmail
            $txtPlanBadge.Text = $data.Plan.ToUpper()

            # Gemini 5h
            $g5 = $data.Gemini.FiveHour
            $txtGemini5hPct.Text = "$($g5.Pct)%"
            $txtGemini5hPct.Foreground = Get-QuotaBrush $g5.Remaining
            $barGemini5h.Background = Get-QuotaBrush $g5.Remaining
            $barGemini5h.Width = [math]::Max(6, $maxBarWidth * $g5.Remaining)
            $txtGemini5hReset.Text = "Resets in $($g5.ResetStr)"

            # Gemini Weekly
            $gw = $data.Gemini.Weekly
            $txtGeminiWkPct.Text = "$($gw.Pct)%"
            $txtGeminiWkPct.Foreground = Get-QuotaBrush $gw.Remaining
            $barGeminiWk.Background = Get-QuotaBrush $gw.Remaining
            $barGeminiWk.Width = [math]::Max(6, $maxBarWidth * $gw.Remaining)
            $txtGeminiWkReset.Text = "Resets in $($gw.ResetStr)"

            # Claude 5h
            $c5 = $data.Claude.FiveHour
            $txtClaude5hPct.Text = "$($c5.Pct)%"
            $txtClaude5hPct.Foreground = Get-QuotaBrush $c5.Remaining
            $barClaude5h.Background = Get-QuotaBrush $c5.Remaining
            $barClaude5h.Width = [math]::Max(6, $maxBarWidth * $c5.Remaining)
            $txtClaude5hReset.Text = "Resets in $($c5.ResetStr)"

            # Claude Weekly
            $cw = $data.Claude.Weekly
            $txtClaudeWkPct.Text = "$($cw.Pct)%"
            $txtClaudeWkPct.Foreground = Get-QuotaBrush $cw.Remaining
            $barClaudeWk.Background = Get-QuotaBrush $cw.Remaining
            $barClaudeWk.Width = [math]::Max(6, $maxBarWidth * $cw.Remaining)
            $txtClaudeWkReset.Text = "Resets in $($cw.ResetStr)"

            # Clean Tooltip without weird characters (max 63 chars per line for Windows tooltip)
            $tt = "Antigravity Quota`nGemini: 5h $($g5.Pct)%, Wk $($gw.Pct)%`nClaude: 5h $($c5.Pct)%, Wk $($cw.Pct)%"
            if ($tt.Length -gt 63) { $tt = $tt.Substring(0, 63) }
            $notifyIcon.Text = $tt

        } else {
            $dotStatus.Fill = $brushAmber
            $txtStatus.Text = "Searching..."
            $txtStatus.Foreground = $brushAmber
            $txtUserEmail.Text = "Waiting for Antigravity..."
            $notifyIcon.Text = "Antigravity: Disconnected`nWaiting for service to start"
        }

        $timeStr = [DateTime]::Now.ToString("HH:mm:ss")
        $txtLastUpdate.Text = "Updated: $timeStr"
    } catch {
        # Graceful error handling
    }
}

# 8. Event Handlers
function Show-Dashboard {
    Set-FlyoutPosition
    Update-DashboardUI
    $window.Show()
    $window.Activate()
}

function Toggle-Dashboard {
    if ($window.IsVisible) {
        $window.Hide()
    } else {
        Show-Dashboard
    }
}

# Tray icon mouse click toggles dashboard
$notifyIcon.Add_MouseClick({
    param($s, $e)
    if ($e.Button -eq [System.Windows.Forms.MouseButtons]::Left) {
        Toggle-Dashboard
    }
})

$notifyIcon.Add_DoubleClick({
    Toggle-Dashboard
})

# Hide when window loses focus
$window.Add_Deactivated({
    $window.Hide()
})

# Intercept Window Closing so Alt+F4 or system close hides the window without destroying it
$script:IsExiting = $false
$window.Add_Closing({
    param($sender, $e)
    if (-not $script:IsExiting) {
        $e.Cancel = $true
        $window.Hide()
    }
})

# Keyboard accessibility: Close on Escape key (R-32)
$window.Add_KeyDown({
    param($sender, $e)
    if ($e.Key -eq [System.Windows.Input.Key]::Escape) {
        $window.Hide()
    }
})

# Close button in header hides window
$btnClose.Add_Click({
    $window.Hide()
})

# Refresh button
$btnRefresh.Add_Click({
    Update-DashboardUI
})

# Auto-start toggle handlers (UI checkbox and Tray context menu synchronized)
if ($chkAutoStart) {
    $chkAutoStart.Add_Click({
        $targetState = [bool]$chkAutoStart.IsChecked
        Set-AutoStart $targetState
        $menuStartup.Checked = $targetState
    })
}

$menuStartup.Add_Click({
    $targetState = -not $menuStartup.Checked
    Set-AutoStart $targetState
    $menuStartup.Checked = $targetState
    if ($chkAutoStart) { $chkAutoStart.IsChecked = $targetState }
})

# Context Menu Handlers
$menuOpen.Add_Click({ Show-Dashboard })
$menuRefresh.Add_Click({ Update-DashboardUI })
$menuHub.Add_Click({ [System.Diagnostics.Process]::Start($script:CurrentHubUrl) | Out-Null })

# Menu Interval Handlers
$dispatcherTimer = New-Object System.Windows.Threading.DispatcherTimer
$dispatcherTimer.Interval = [TimeSpan]::FromSeconds(60)

$int30.Add_Click({
    $int30.Checked = $true
    $int60.Checked = $false
    $int300.Checked = $false
    $dispatcherTimer.Interval = [TimeSpan]::FromSeconds(30)
})

$int60.Add_Click({
    $int30.Checked = $false
    $int60.Checked = $true
    $int300.Checked = $false
    $dispatcherTimer.Interval = [TimeSpan]::FromSeconds(60)
})

$int300.Add_Click({
    $int30.Checked = $false
    $int60.Checked = $false
    $int300.Checked = $true
    $dispatcherTimer.Interval = [TimeSpan]::FromSeconds(300)
})

# Clean, Exception-Free Exit Handler
$menuExit.Add_Click({
    $script:IsExiting = $true
    try { $dispatcherTimer.Stop() } catch {}
    try { $wakeupTimer.Stop() } catch {}
    try {
        if ($notifyIcon) {
            $notifyIcon.Visible = $false
            $notifyIcon.Dispose()
        }
    } catch {}
    try {
        if ($script:WakeupEvent) {
            $script:WakeupEvent.Dispose()
        }
    } catch {}
    try {
        if ($script:AppMutex) {
            try { $script:AppMutex.ReleaseMutex() } catch {}
            $script:AppMutex.Dispose()
        }
    } catch {}
    try {
        Stop-HeadlessAntigravityDaemon
    } catch {}
    try {
        $window.Close()
    } catch {}
    try {
        if ($app) { $app.Shutdown() }
    } catch {}
    try {
        [System.Windows.Forms.Application]::Exit()
    } catch {}
    [System.Environment]::Exit(0)
})

# 9. Periodic Quota Refresh Timer
$dispatcherTimer.Add_Tick({
    Update-DashboardUI
})
$dispatcherTimer.Start()

# 10. Wakeup Listener Timer (detects when user launches shortcut or start.bat again)
$wakeupTimer = New-Object System.Windows.Threading.DispatcherTimer
$wakeupTimer.Interval = [TimeSpan]::FromMilliseconds(500)
$wakeupTimer.Add_Tick({
    if ($script:WakeupEvent -and $script:WakeupEvent.WaitOne(0)) {
        Show-Dashboard
    }
})
$wakeupTimer.Start()

# Initial Data Load
Update-DashboardUI

# 11. Run WPF Message Loop with OnExplicitShutdown
$app = New-Object System.Windows.Application
$app.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown

# Open the dashboard window immediately on initial launch so user sees it right away
if (-not $StartMinimized) {
    Show-Dashboard
}

# Show helpful notification in tray
$notifyIcon.ShowBalloonTip(3000, "Antigravity Quota Monitor", "Active in system tray. Click anytime to view your quota.", [System.Windows.Forms.ToolTipIcon]::Info)

$app.Run() | Out-Null
