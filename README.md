# Alex Island

A native-first Dynamic Island experience for Windows 11, built with C#, WinUI 3 and .NET 10 LTS.

Alex Island is designed to run in the current user's session without requiring administrator privileges. The core application remains useful offline; optional integrations are isolated modules.

## Project status

The project is being developed in staged milestones:

- **Phase 1 — Core architecture:** application lifecycle, modular services, settings, logging and cancellation.
- **Phase 2 — Dynamic Island UI:** top-centered floating window, compact/expanded states, animations and multi-monitor/DPI support.
- **Phase 3 — Windows integration:** media sessions, audio volume, system notifications and basic system state.

## Requirements

- Windows 11 (x64)
- .NET 10 SDK
- Visual Studio 2026 or a compatible .NET 10 toolchain
- Windows App SDK / WinUI 3 workload

## Build

Open `AlexIsland.sln` in Visual Studio or run:

```powershell
dotnet restore .\AlexIsland.sln
dotnet build .\AlexIsland.sln -c Debug
```

## User-level operation

Alex Island must not require elevation. Install and store writable data under the current user's profile (for example, `%LocalAppData%\AlexIsland`). Do not install drivers, Windows services, machine-wide registry entries, or write to protected system folders.

## Security

Never commit access tokens, client secrets, passwords, private certificates, or personal configuration. Optional integrations must request the minimum permissions needed.

## License

TBD.
