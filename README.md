# Antigravity Quota Monitor

A lightweight, native Windows desktop monitor that sits in your Taskbar System Tray to track your Antigravity AI model quotas in real time. Designed for developers using editors like Zed who need direct visibility into model limits without keeping Antigravity IDE open.

---

## Features

- **Taskbar System Tray Icon**:
  - High-contrast monochrome rocket icon visible on dark and light taskbars.
  - Hover tooltip displaying live 5-hour and weekly percentages.
  - Color indicator reflecting current quota health.
- **Clean Flyout Dashboard**:
  - Click or double-click the tray icon to open a floating dashboard anchored right above your taskbar.
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

## Prerequisites

1. **Windows 10 or 11** (64-bit).
2. **Antigravity** installed on your system:
   - Antigravity IDE, Antigravity CLI, or the Antigravity background language server.
   - The monitor connects locally to Antigravity's local language server process (`127.0.0.1`) to retrieve live quota data.

---

## Installation and Setup

### 1. Clone the Repository

```bash
git clone https://github.com/<your-username>/ag-tray-monitor.git
cd ag-tray-monitor
```

### 2. Generate Shortcuts (Optional)

Run the included PowerShell script to generate convenient shortcuts for your Desktop and Windows Start Menu:

```powershell
powershell -ExecutionPolicy Bypass -File .\create_shortcut.ps1
```

Or simply right-click `create_shortcut.ps1` and select **Run with PowerShell**.

---

## How to Use

### Starting the Monitor

- **Option 1**: Double-click `start.bat` in the repository folder.
- **Option 2**: Double-click the **Antigravity Quota Monitor** shortcut on your Desktop or in the Start Menu.

When launched, the dashboard pops up immediately in the bottom-right corner to show your current quota status. If the application is already running in the background, launching it again will simply bring the existing dashboard to the front.

### Interacting with the Tray Icon

- **View Dashboard**: Left-click or double-click the rocket icon in the system tray.
- **Dismiss Dashboard**: Click the **Close** button in the top-right corner, press the **Escape** key, or click anywhere outside the window.
- **Quick Glance**: Hover your mouse over the tray icon to view a quick summary tooltip.
- **Right-Click Context Menu**:
  - Select **Refresh Usage** to trigger an instant data sync.
  - Select **Auto-Refresh Interval** to switch between 30 seconds, 60 seconds, or 5 minutes.
  - Check **Start with Windows** to automatically launch the monitor when your PC boots.
  - Select **Exit** to shut down the monitor.

### Using with Zed or Other Editors

1. Make sure Antigravity is installed and logged in with your Google account.
2. Launch **Antigravity Quota Monitor**.
3. Open **Zed** (or your preferred editor) and code freely.
4. Keep track of your Gemini and Claude 5-hour and weekly limits at a glance right from your taskbar.

---

## Project Structure

```text
ag-tray-monitor/
├── .gitignore
├── README.md
├── start.bat                  # One-click batch launcher
├── create_shortcut.ps1        # Generates Desktop and Start Menu shortcuts
├── assets/
│   ├── icon.ico               # Windows system icon (multi-resolution)
│   └── icon.png               # High-contrast application logo
└── src/
    ├── AntigravityApi.ps1     # Process discovery and quota RPC client
    ├── DashboardWindow.xaml   # Clean WPF dashboard UI
    └── TrayApp.ps1            # Main application script and tray logic
```

---

## How It Works

1. **Process Discovery**: Automatically scans local processes for Antigravity's `language_server.exe` and retrieves the active session port and CSRF token.
2. **Local RPC**: Communicates directly with the local server over Connect-RPC endpoints (`/RetrieveUserQuotaSummary` and `/GetUserStatus`).
3. **No External Network Calls**: All communication is strictly local to your machine (`127.0.0.1`).
