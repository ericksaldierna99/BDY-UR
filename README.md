# UR Robot Interfaz

Windows HMI (WPF, C#, .NET 10) for a Universal Robots UR12e and an Allen-Bradley Micro820.

## Phase 1

Dashboard Server (TCP 29999) and RTDE (TCP 30004, protocol v2) are wired for UR12e PolyScope 5.25.2.

- Live / Stale / Unavailable on process values. A disconnect does not write “robot stopped”.
- Dashboard tiles that have a robot source use controller data; PLC, station status, UPH, and cycle times stay placeholders.
- Settings includes connection diagnostics (link, sample age, last error, Test connection, Reconnect).
- Communication alarms increment the Dashboard active-alarm count. Alarm history/CSV is a later phase.

Spec section 5.4 is unspecified and has no screen. Play/stop/move are not implemented.

Set `robot.ip` in `hmi.json` on the HMI PC. An empty IP does not open sockets.

## Build

Requirements: any .NET 10 SDK (10.0.100 or later) and Windows to run the WPF app. `global.json` pins `10.0.100` with `rollForward: latestMinor` so Visual Studio can use whichever 10.x SDK is installed. The solution targets `net10.0-windows` / x64.

```bash
dotnet restore UrHmi.slnx
dotnet build UrHmi.slnx -c Release
dotnet test UrHmi.slnx -c Release
```

Open `UrHmi.slnx` in Visual Studio 2026 to run `UrHmi.App`.
