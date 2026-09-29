# Antigravity Quota Monitor

A lightweight, native Windows desktop monitor that sits in your Taskbar System Tray to track your Antigravity AI model quotas in real time. Designed for developers using editors like Zed who need direct visibility into model limits without keeping Antigravity IDE open.

---

## Features

- **Taskbar System Tray Icon**:
  - High-contrast icon visible on dark and light taskbars.
  - Hover tooltip displaying live 5-hour and weekly percentages.
  - Color indicator reflecting current quota health.
- **Human-Crafted Flyout Dashboard**:
  - Click or double-click the tray icon to open a clean dashboard anchored right above your taskbar.
  - **Account Summary**: Name, email, and active plan tier (Pro, Teams).
  - **Gemini Models (Flash and Pro)**:
    - 5-Hour Limit: Progress bar with color grading and countdown (for example: Resets in 4h 49m).
    - Weekly Limit: Progress bar and countdown (for example: Resets in 1d 3h).
  - **Claude and GPT Models (Sonnet, Opus, GPT)**:
    - 5-Hour Limit and Weekly Limit progress bars and countdowns.
  - Auto-hides when you switch tasks or click outside. Keyboard friendly (press Escape to dismiss).
- **Context Menu (Right Click)**:
  - Open Dashboard
  - Refresh Usage
  - Auto-Refresh Interval (30 Seconds, 60 Seconds, 5 Minutes)
  - Open Web Hub
  - Start with Windows (Toggle autostart on system boot)
  - Exit
- **Fast and Native**:
  - Built with native Windows .NET (WPF and WinForms) via PowerShell.
  - No Electron runtime, no external package installations.
  - Uses approximately 30 MB of memory.

---

## How to Launch

1. **Desktop Shortcut**: Double-click **Antigravity Quota Monitor** on your Desktop.
2. **Start Menu**: Search for **Antigravity Quota Monitor** in the Windows Start Menu.
3. **Batch Launcher**: Double-click `start.bat` in this folder.

Whenever you launch it, the dashboard opens immediately on screen so you get instant visual feedback. If it is already running, launching again simply brings the existing window to the front.

---

## Folder Structure

```
D:\Yasfa\2. PROGRAM\ag-tray-monitor\
├── assets/
│   ├── icon.png               # High-contrast application logo
│   └── icon.ico               # Windows system icon
├── src/
│   ├── AntigravityApi.ps1     # Process discovery and quota RPC client
│   ├── DashboardWindow.xaml   # Clean WPF dashboard UI
│   └── TrayApp.ps1            # Main application script and tray logic
├── create_shortcut.ps1        # Generates Desktop and Start Menu shortcuts
├── start.bat                  # One-click batch launcher
└── README.md                  # Project documentation
```
