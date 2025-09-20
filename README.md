# Piperf

A lightweight always-on-top performance overlay for Windows built with WPF. Piperf displays CPU, GPU, disk, and network activity while staying out of the way behind your games or other apps.

## Features

- Transparent click-through overlay that can toggle to interactive mode on demand
- Live CPU/GPU utilisation and temperature readings via LibreHardwareMonitor
- Disk and network throughput sampled from Windows performance counters
- Latency probe that pings a configurable host every 500 ms and shows round-trip time or timeout status
- Tray icon with quick controls to show the window, switch interaction mode, toggle run-on-startup, or quit
- Remembers window position, opacity, font size, and poll interval via a JSON config file

## Requirements

- Windows 10 or later
- .NET 8.0 SDK (for development) or .NET 8.0 runtime (for deployment)
- Access to Windows performance counters (normal user rights are sufficient)

## Building

```powershell
dotnet build
```

The project targets `net8.0-windows` and ships as a single WPF executable. Visual Studio, JetBrains Rider, or `dotnet build` from the CLI all work.

## Running

```powershell
dotnet run --project Piperf.csproj
```

When launched, Piperf shows the overlay immediately and begins loading hardware counters in the background. Metrics appear as soon as the sensors finish initialisation.

## Controls

- **Alt + drag**: Move the overlay
- **Alt + double-click**: Toggle persistent click-through vs interactive mode
- **Ctrl + Shift + P**: Toggle mode (fallback shortcut)
- **Tray icon menu**: Show window, toggle interaction mode, enable/disable Startup, quit

## Configuration

A JSON configuration file is stored under `%APPDATA%\Piperf\config\config.json`. The file tracks:

- `PollIntervalMs`: Metrics refresh interval in milliseconds
- `Opacity`: Overlay opacity (0-1)
- `FontSize`: Text size in points
- `PingTarget`: Hostname or IP address to ping (default `1.1.1.1`)
- `PingIntervalMs`: Ping refresh cadence in milliseconds (default `500`)
- `PositionX` / `PositionY`: Stored window coordinates

Edit the file while Piperf is closed, or add UI in the future to handle live updates.

## Troubleshooting

- **Slow startup**: First launch may take a few seconds while hardware sensors initialise. Subsequent launches should be faster thanks to background initialisation and on-demand updates.
- **Tray icon missing**: Ensure `Assets/Piperf.ico` exists and remains marked as a WPF resource. The tray loader now uses the application resource stream and falls back to `SystemIcons.Application` if the icon cannot be found.
- **Missing metrics**: LibreHardwareMonitor sometimes needs elevated rights for certain sensors. Run once as administrator if GPU temperatures stay blank.

## License

This repository currently has no explicit licence. Add one before distributing builds externally.
