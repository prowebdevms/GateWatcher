# GateWatcher

GateWatcher is a .NET **C#** console + **WinForms** application that monitors Gate.io **spot** markets and notifies you when the **24h change %** moves by a configured threshold within a short window (default: **±20% over the past 20 seconds**).

It shows live **Top-10 gainers/losers**, plots their 24h% over time, and writes daily **CSV** logs for analysis.

> No API keys required. Only public Gate.io endpoints are used.

---

## Table of Contents

- [Features](#features)
- [Folder Structure](#folder-structure)
- [Build & Run](#build--run)
- [Configuration](#configuration)
  - [Live edits / Hot-reload](#live-edits--hot-reload)
- [Console Commands](#console-commands)
- [UI Overview](#ui-overview)
  - [Header (Recent Alerts / Config)](#header-recent-alerts--config)
  - [Top-10 (Text)](#top-10-text)
  - [Charts](#charts)
- [Alerts](#alerts)
- [CSV Output](#csv-output)
- [Troubleshooting](#troubleshooting)
- [Roadmap](#roadmap)
- [License (MIT)](#license-mit)
- [Donate](#donate)

---

## Features

- 🔁 Polls all spot tickers every **N** seconds (default **5s**).
- 🎚️ Triggers alerts if `24h% (now) – 24h% (N seconds ago)` ≥ **IncreaseThreshold** or ≤ **–DecreaseThreshold**.
- 🧰 Live **console** controls (thresholds, filters, interval, include/exclude lists, UI visibility).
- 🪟 **WinForms UI**:
  - **Recent Alerts** panel (color-coded, newest on top).
  - **Config** panel (collapsed by default). Edits auto-save on focus-out and hot-reload without restart.
  - **Top-10 Increase/Decrease** (text tables).
  - **Two charts** (Increase / Decrease) with auto-focus on time; manual pan/zoom disables auto-focus until re-enabled.
- 🧾 **CSV** logging (daily file).
- 🔔 **Console** and optional **Telegram** notifications.

---

## Folder Structure

GateWatcher/
├─ Models/
│ ├─ AlertDirectionType.cs
│ ├─ AlertItem.cs
│ ├─ AppConfig.cs
│ ├─ CurrencyPairInfo.cs
│ ├─ IAlertSource.cs
│ ├─ INotifier.cs
│ ├─ SpotTicker.cs
│ ├─ StatRow.cs
│ └─ StatsSnapshot.cs
├─ Services/
│ ├─ CommandLoop.cs
│ ├─ CompositeNotifier.cs
│ ├─ ConfigManager.cs
│ ├─ ConsoleNotifier.cs
│ ├─ CsvLogger.cs
│ ├─ GateIoClient.cs
│ ├─ MarketMonitor.cs
│ └─ TelegramNotifier.cs
├─ appsettings.json
├─ MainForm.cs
├─ Program.cs
└─ UiController.cs

**Key components**

- **Program.cs** – app entry; starts monitor + command loop; bootstraps UI.
- **UiController.cs** – runs WinForms on an STA thread; exposes `Open/Close/PushSnapshot/PushAlert`.
- **MainForm.cs** – UI (alerts panel, config panel, Top-10 grids, charts).
- **Services/**
  - **MarketMonitor.cs** – polling, filtering, threshold checks, CSV logging, emits snapshots/alerts.
  - **GateIoClient.cs** – HTTP client for Gate.io public endpoints.
  - **ConfigManager.cs** – atomic save, debounced hot-reload, safe concurrent read/write.
  - **ConsoleNotifier.cs / TelegramNotifier.cs / CompositeNotifier.cs** – notifications.
  - **CommandLoop.cs** – interactive console.
  - **CsvLogger.cs** – daily CSV append.
- **Models/**
  - **AppConfig.cs** – strongly typed config with **defaults** for first run.
  - **SpotTicker.cs / CurrencyPairInfo.cs** – Gate.io DTOs.
  - **StatRow.cs / StatsSnapshot.cs** – payloads for UI.
  - **AlertItem.cs / AlertDirectionType.cs / IAlertSource.cs / INotifier.cs** – alert and notifier types.

---

## Build & Run

Requirements: **.NET 8 SDK** (or .NET 7; adjust `TargetFramework` accordingly)

```bash
# build
dotnet build -c Release

# run
dotnet run -c Release
```

On Windows, the WinForms UI launches; you can hide/show it via console commands.

## Configuration
The app reads/writes `appsettings.json`. If missing, it is generated with sensible defaults at first start.

Default config (example):
```json
{
  "Polling": {
    "PollIntervalSeconds": 5,
    "LookbackSeconds": 20,
    "SamplesWindow": 300
  },
  "Thresholds": {
    "IncreaseThresholdPercent": 20.0,
    "DecreaseThresholdPercent": 20.0
  },
  "Filters": {
    "IncludePairs": [],
    "ExcludePairs": [],
    "QuoteCurrencies": [ "USDT" ],
    "MinQuoteVolume24h": 10000.0,
    "MinBaseVolume24h": 0.0,
    "NewOnly": false,
    "NewSinceDays": 30
  },
  "Notifications": {
    "Console": { "Enabled": true },
    "Telegram": {
      "Enabled": false,
      "BotToken": "",
      "ChatId": ""
    }
  },
  "GateIo": {
    "BaseUrl": "https://api.gateio.ws/api/v4"
  }
}
```

## Live edits / Hot-reload
- Change values in the UI Config panel (auto-saved on focus-out) → no restart.
- Or use console commands (below).
- The config manager performs atomic writes, debounced reloads, and shared-read to avoid file locks.

## Console Commands
Type help in the app console for the list:

```sql
help
config show
config set increase <pct>       # e.g., 20
config set decrease <pct>       # e.g., 20
config set interval <sec>       # polling interval, >=1
config set lookback <sec>       # horizon for alert comparison
config set window <n>           # samples to keep; auto-raised to lookbackSteps + 1

config include <PAIR...>        # track only these pairs; empty => ALL
config exclude <PAIR...>        # exclude these pairs

filter quotes <QUOTE...>        # e.g., USDT USDC; empty => any
filter minqv <num>              # 24h quote_volume ≥ num
filter minbv <num>              # 24h base_volume  ≥ num
filter new <true|false> [days]  # only “new” pairs listed within last days

ui open                         # show the window
ui close                        # hide the window (app continues running)
ui toggle

quit
```

## Alert logic
If now 24h% – (24h% N seconds ago) ≥ `IncreaseThresholdPercent` ⇒ increase alert.
If ≤ `-DecreaseThresholdPercent` ⇒ decrease alert.

## UI Overview
The window contains three areas:

## Header (Recent Alerts / Config)
- Recent Alerts (default visible): newest first, color-coded
  Green = increase, Red = decrease.

- Config (collapsed by default): click the header to toggle.

  - Interval (sec) – polling frequency.

  - Lookback (sec) – alert window length.

  - Window (samples) – samples to retain/plot.

  - Increase % / Decrease % – thresholds.

  - Quotes – comma-separated quote currencies (e.g., `USDT, USDC`).

  - Min QuoteVol/BaseVol (24h) – volume filters.

  - New only / New since days – list only recently listed pairs.

  - Auto focus graphs – when ON, charts step their X-axis each tick. Manual pan/zoom disables auto-focus; re-enable to resume.

  - Reset view – one-time autoscale and re-enable focus.

## Top-10 (Text)
Two tables:
 - Top 10 – Increase (Text)
 - Top 10 – Decrease (Text)

Columns: `Rank`, `Pair`, `24h % (prev)`, `Quote Vol (24h) (prev)`, `Last (prev)`, `Time`.
Values display current value and the previous value in parentheses. Cells color green/red compared to previous tick.

## Charts
Two plots:

 - Increase – 24h% over time
 - Decrease – 24h% over time

Behavior:

 - The first real update triggers a one-time AutoScale(). After that, only X steps (if auto-focus enabled); Y stays fixed unless you Reset view.
 - Each new symbol gets a colored vertical start marker and a small label aligned with its first point.

## Alerts
 - ConsoleNotifier – prints alerts and pushes to the UI Recent Alerts panel.
 - TelegramNotifier – optional. Configure:
   - `Notifications.Telegram.Enabled = true`
   - `BotToken`, `ChatId`

Alert titles contain direction (↑ increase / ↓ decrease). The UI colors by title (not by hyphens within message text).

## CSV Output
Daily file `stats_YYYY-MM-DD.csv` in the app folder with:

```lua
time,type,rank,pair,last,quote_volume,change_percentage
```

The file is appended when Top-10 lists are produced each tick.

## Troubleshooting
- “Reload failed: The process cannot access the file”
The config manager writes atomically and debounces reloads. If your editor locks the file, the watcher will retry on the next event. Avoid saving partial/empty JSON.

- Duplicate alerts in the UI
Ensure the alert source is hooked once by `UiController`. The current code guards against double subscriptions.

- Charts keep snapping
Manual pan/zoom disables auto-focus and unchecks Auto focus graphs. Re-check or press Reset view to resume.
