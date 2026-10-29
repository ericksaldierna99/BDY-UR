# UR Robot Interfaz

Windows HMI (WPF, C#, .NET 10) for a Universal Robots UR12e and an Allen-Bradley Micro820.

## Phase 0

This repository currently contains the solution skeleton:

- Layered projects (`UrHmi.App`, `UrHmi.Ui`, `UrHmi.Presentation`, `UrHmi.Application`, `UrHmi.Domain`, `UrHmi.Infrastructure`, `UrHmi.Tools.Abstractions`)
- Generic Host and dependency injection in the WPF startup project
- JSON configuration (`hmi.json`) without secrets
- Placeholder ports for Dashboard Server, RTDE, and `IPlcChannel`
- Navigation to Dashboard, Alarm Management, Tool Management, and Settings
- Indicator primitives for Normal, Warning, Fault, Safety, and CommsLoss, plus freshness captions

Spec section 5.4 is unspecified and has no screen.

## Build

Requirements: .NET 10 SDK, Windows for running the WPF app. The solution targets `net10.0-windows` / x64.

```bash
dotnet restore UrHmi.slnx
dotnet build UrHmi.slnx -c Release
dotnet test UrHmi.slnx -c Release
```

Open `UrHmi.slnx` in Visual Studio 2026 to run `UrHmi.App`.
