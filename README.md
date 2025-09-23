# Piperf

A lightweight always-on-top performance overlay for Windows built with WPF. Piperf displays CPU, GPU, disk, and network activity while staying out of the way behind your games or other apps.

<img width="700" height="170" alt="image" src="https://github.com/user-attachments/assets/7492d945-cdd4-481a-8f29-83d0b896ae3a" />


## Features

- Transparent click-through overlay with Alt + drag repositioning
- Live CPU/GPU utilisation and temperature readings via LibreHardwareMonitor
- Disk and network throughput sampled from Windows performance counters
- Latency probe that pings a configurable host every 1000 ms and shows round-trip time or timeout status
- Tray icon with quick controls to show/hide the overlay, toggle run-on-startup, or quit
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
- **Tray icon menu**: Show/Hide overlay, enable/disable Startup, quit

## Notes

- Piperf needs administrator permission to read CPU temperature sensors; metrics still work without it if you do not care about CPU temps.
- Windows Defender may flag LibreHardwareMonitorLib because it calls low-level APIs to read hardware sensors; the library is safe for this use.

## Installer

1. `dotnet publish -c Release -r win-x64 --self-contained false -o publish`
2. `dotnet wix build Installer\PiperfInstaller.wixproj -c Release`

The WiX build produces an MSI in `Installer\bin\Release\x64`. The installer defaults to adding Piperf to Windows startup (you can uncheck the box on the final screen).

## Configuration

A JSON configuration file is stored under `%APPDATA%\Piperf\config\config.json`. The file tracks:

- `PollIntervalMs`: Metrics refresh interval in milliseconds
- `Opacity`: Overlay opacity (0-1)
- `FontSize`: Text size in points
- `PingTarget`: Hostname or IP address to ping (default `1.1.1.1`)
- `PingIntervalMs`?: Ping refresh cadence in milliseconds (default `1000`)
- `PositionX` / `PositionY`: Stored window coordinates

Edit the file while Piperf is closed, or add UI in the future to handle live updates.

## Troubleshooting

- **Slow startup**: First launch may take a few seconds while hardware sensors initialise. Subsequent launches should be faster thanks to background initialisation and on-demand updates.
- **Tray icon missing**: Ensure `Assets/Piperf.ico` exists and remains marked as a WPF resource. The tray loader now uses the application resource stream and falls back to `SystemIcons.Application` if the icon cannot be found.
- **Missing metrics**: LibreHardwareMonitor sometimes needs elevated rights for certain sensors. Run once as administrator if CPU temperatures stay blank.

## License

This repository currently has no explicit licence. Add one before distributing builds externally.

