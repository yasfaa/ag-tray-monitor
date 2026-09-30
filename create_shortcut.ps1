# Creates shortcuts for Antigravity Quota Monitor:
# 1. In the project folder itself (for direct launching or GitHub repo)
# 2. On the Desktop
# 3. In the Windows Start Menu

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $projectDir "AG Quota Tracker.exe"
$icoPath = Join-Path $projectDir "assets\icon.ico"
$desktopPath = [Environment]::GetFolderPath("Desktop")
$startMenuPrograms = [Environment]::GetFolderPath("Programs")

$wsh = New-Object -ComObject WScript.Shell

# 1. Project Folder Shortcut
$folderShortcutPath = Join-Path $projectDir "Antigravity Quota Monitor.lnk"
$shortcutFolder = $wsh.CreateShortcut($folderShortcutPath)
$shortcutFolder.TargetPath = $exePath
$shortcutFolder.WorkingDirectory = $projectDir
$shortcutFolder.Description = "Monitor Antigravity AI model quotas in the Windows taskbar system tray"
$shortcutFolder.IconLocation = "$icoPath,0"
$shortcutFolder.Save()

Write-Host "Created Folder shortcut: $folderShortcutPath" -ForegroundColor Green

# 2. Desktop Shortcut
$desktopShortcutPath = Join-Path $desktopPath "Antigravity Quota Monitor.lnk"
$shortcutDesktop = $wsh.CreateShortcut($desktopShortcutPath)
$shortcutDesktop.TargetPath = $exePath
$shortcutDesktop.WorkingDirectory = $projectDir
$shortcutDesktop.Description = "Monitor Antigravity AI model quotas in the Windows taskbar system tray"
$shortcutDesktop.IconLocation = "$icoPath,0"
$shortcutDesktop.Save()

Write-Host "Created Desktop shortcut: $desktopShortcutPath" -ForegroundColor Green

# 3. Start Menu Shortcut
$startMenuShortcutPath = Join-Path $startMenuPrograms "Antigravity Quota Monitor.lnk"
$shortcutStartMenu = $wsh.CreateShortcut($startMenuShortcutPath)
$shortcutStartMenu.TargetPath = $exePath
$shortcutStartMenu.WorkingDirectory = $projectDir
$shortcutStartMenu.Description = "Monitor Antigravity AI model quotas in the Windows taskbar system tray"
$shortcutStartMenu.IconLocation = "$icoPath,0"
$shortcutStartMenu.Save()

Write-Host "Created Start Menu shortcut: $startMenuShortcutPath" -ForegroundColor Green
