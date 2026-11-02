# Development

## Prerequisites
- Windows 11 x64
- .NET 10 SDK
- Visual Studio with WinUI 3 / Windows App SDK support

## Build
```powershell
dotnet restore .\AlexIsland.sln
dotnet build .\AlexIsland.sln -c Debug
dotnet build .\AlexIsland.sln -c Release
```

## Run
Start `AlexIsland.App` from Visual Studio or run its project after a successful build.

## Privilege policy
Run the application normally. Do not use "Run as administrator". The application manifest intentionally requests `asInvoker`.

## Coding conventions
- Keep platform-independent contracts in Core.
- Keep Win32 declarations in the Native layer.
- Use async APIs and propagate CancellationToken.
- Unsubscribe all native/system events during disposal.
- Never block the UI thread on I/O.
- Avoid background polling when event notifications are available.
