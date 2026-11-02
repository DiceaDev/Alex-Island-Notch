# Architecture

## Goals
- Native-first Windows 11 desktop application.
- Runs as the interactive user; process manifest is `asInvoker`.
- No Windows service, driver, machine-wide registry write, or elevated installer.
- Offline-capable core with optional integrations isolated behind interfaces.
- Event-driven services; avoid polling when a Windows event/API is available.

## Projects
- `AlexIsland.App`: WinUI 3 application, window composition and dependency wiring.
- `AlexIsland.Core`: platform-independent contracts, settings models and module lifecycle.
- Future projects may separate Windows adapters, integration providers and automated tests when justified.

## Windowing
The island uses a borderless WinUI window positioned relative to the primary display work area. Native Win32 styles keep it topmost and out of the taskbar. Multi-monitor selection and DPI-aware repositioning will be expanded in Phase 2.

## Lifecycle
Modules implement `IIslandModule` and expose asynchronous initialize/start/stop/dispose operations. The host must pass cancellation tokens, isolate module failures and stop modules before disposing shared services.

## Data and permissions
Writable data belongs under the current user's LocalAppData profile. The application must never request administrator elevation. Optional integrations use user-consented OAuth scopes and store secrets through Windows Credential Manager or DPAPI.

## Performance
Prefer event subscriptions over timers. Timers must be scoped, cancellable and paused when their UI is not visible. Media artwork and expensive resources must be cached with explicit size limits.
