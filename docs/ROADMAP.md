# Roadmap

## Phase 1 — Core architecture
- [x] Establish WinUI 3 / .NET 10 project baseline
- [x] Set process execution level to asInvoker
- [x] Add core module lifecycle contract
- [x] Add per-user settings persistence
- [ ] Add dependency injection and module host
- [ ] Add structured logging and crash handling
- [ ] Add unit-test project and CI build

## Phase 2 — Dynamic Island UI
- [x] Create initial floating island window
- [x] Center island at the top of the primary display
- [x] Keep window topmost and out of the taskbar
- [ ] Add compact/expanded state machine and transitions
- [ ] Add multi-monitor and DPI-change handling
- [ ] Add settings-driven size, position and appearance

## Phase 3 — Windows integration
- [ ] Media session service using GSMTC
- [ ] Audio endpoint service using Windows Core Audio
- [ ] Windows notification integration
- [ ] Battery, network and basic system state
- [ ] Event subscription cleanup and reconnection handling

## Release gates
A phase is complete only when it builds in Debug and Release, has relevant automated tests, runs without elevation on a standard account, and documents known limitations.
