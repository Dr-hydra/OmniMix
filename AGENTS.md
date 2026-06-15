# AGENTS.md

## Project Role

This repository is the VB.NET/WPF compatible frontend for OmniMix. Treat it as a frontend compatibility layer, not as the primary upstream backend implementation.

## Upstream

- Upstream repository: https://github.com/BeyondtheApex/ChillPatcher
- Upstream full backend package is published as `playerbuild.zip` in the upstream Release assets.
- Backend behavior should stay compatible with upstream OmniMix/ChillPatcher builds.
- Prefer frontend-side adapters and compatibility fixes over backend API changes.

## Upstream Sync Procedure

- Do not directly merge or pull upstream `main` into this repository's `main`.
- Upstream `main` does not carry this repository's VB.NET frontend directory, so a normal merge may treat `OmniMixPlayer/gui_vbnet/` as deleted.
- For upstream refresh work, create a sync branch from `upstream/main`, then restore this repository's owned files:
  - `README.md`
  - `README_ZH.md`
  - `AGENTS.md`
  - `OmniMixPlayer/gui_vbnet/`
- The current sync branch pattern is:
  - base: `BeyondtheApex/ChillPatcher` `main`
  - overlay: this repository's README files, `AGENTS.md`, and VB.NET frontend tree
- After syncing, verify that the upstream SDK and Flutter Windows SDK client are present before adapting the VB.NET frontend:
  - `OmniMixPlayer/OmniMixPlayer.SDK/`
  - `OmniMixPlayer/gui_flutter/lib/services/omni_sdk_client.dart`
  - `OmniMixPlayer/gui_flutter/lib/services/omni_sdk_bindings.dart`

## Release Packaging

- The primary local artifact is `OmniMixPlayer.Gui.Vbnet.exe`.
- Starting with `3.0.7`, GitHub Releases must not publish the framework-dependent or self-contained VB.NET executable as standalone assets.
- Public releases should contain only the complete portable package, complete framework-dependent package, and Windows installer unless a later packaging decision explicitly changes this rule.
- Full-package releases should be based on the upstream `playerbuild.zip`, then replace the upstream desktop Flutter GUI with `OmniMixPlayer.Gui.Vbnet.exe`.
- When creating a full package, preserve upstream backend/runtime assets such as:
  - `OmniMixPlayer.Backend.exe`
  - `modules/`
  - `native/`
  - `wwwroot/`
  - `appsettings.json`
  - media generator files and package metadata when present
- Only remove the upstream desktop GUI runtime when replacing it, such as:
  - `omnimix_gui.exe`
  - `flutter_windows.dll`
  - Flutter desktop `data/`
  - Flutter desktop plugin DLLs
- Include a short provenance note in full packages, for example `BACKEND_UPSTREAM.txt`, with upstream repo, tag, asset URL, and SHA256.

## Backend Discovery

- The VB.NET frontend expects the backend executable to be named `OmniMixPlayer.Backend.exe`.
- Keep the frontend able to run when placed beside an existing upstream backend distribution.
- Do not assume the backend is owned by this repository unless the task explicitly says to maintain a forked backend.

## API And SDK Direction

- Upstream guidance says the Flutter Windows client now uses the SDK to communicate with the backend.
- For future VB.NET integration work, prefer studying the Flutter Windows SDK integration instead of hand-writing duplicate compatible API implementations.
- The SDK route should reduce drift because API endpoints and backend control are centralized upstream.
- The Web client may use native Dart HTTP/WebSocket API calls, but the desktop frontend should favor the SDK-style approach when practical.
- The VB.NET frontend now references `OmniMixPlayer.SDK` and uses `.NET` gRPC-Web clients for core library, playback, queue/history, equalizer, instance, archive, and profile operations.
- Upstream `v3.0` removed the older profile playback mode/queue fields and moved queue-like state into `PlaybackTimelineState`; keep `OmniMixApiClient` as the compatibility adapter between existing WPF page code and the SDK timeline model.
- Treat `InstanceProfile.Capabilities.ServerControlledPlayback` as the current SDK-side signal for server-managed playback behavior when the UI needs to infer the legacy mode string.
- Keep REST calls only for upstream surfaces that are not currently covered by the SDK, such as backend config, backend stop, module enablement, and module UI/link/settings endpoints.
- Preserve the existing `OmniMixApiClient` public method signatures when possible so page-level WPF code does not need to know whether a call is backed by SDK/gRPC or REST.

## Compatibility Model

- GUI clients and game mods are both clients of the same backend model.
- They can control similar endpoints and observe similar backend state; their main difference is the user-facing surface and audio output role.
- Keep module UI, playback state, queue control, game bridge files, port files, and instance IDs compatible with upstream expectations.

## Build Notes

- Use `rg`/`rg --files` first when searching the repository.
- Keep edits scoped to the frontend unless the user explicitly asks for backend changes.
- Upstream `v3.0` moved build orchestration into `scripts/build_tree.py`; this repository keeps `scripts/build_all.py` as a command-line compatibility wrapper for old `build.cmd` flows and for publishing the VB.NET frontend into `playerbuild/`.
- Use `python scripts/build_all.py player --skip-flutter` to build the full backend/modules package while replacing the desktop Flutter GUI with `OmniMixPlayer.Gui.Vbnet.exe`.
- When `--skip-flutter` is used, the wrapper disables only the Flutter desktop copy step during `playerbuild` assembly; do not remove backend `wwwroot/` web assets.
- Before release work, check the current GitHub Release assets and upstream Release asset digest.
- Avoid deleting or rewriting generated/upstream package contents unless the release task specifically requires that replacement.

## Terminal

- Use PowerShell 7 (`pwsh`) for all terminal operations.

## Documentation Notes

- `README.md` is the canonical English project overview.
- `README_ZH.md` may contain encoding issues; verify content carefully before editing it.
- If release packaging rules change, update this file and the README together.
