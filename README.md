# OmniMix VB.NET Compatible Frontend

OmniMix VB.NET Compatible Frontend is a Windows desktop client for the existing OmniMix backend. Its goal is to provide a lightweight WPF experience while staying compatible with the upstream backend API, module UI endpoints, music library, playback queue, and game integration flow.

This repository is maintained as a frontend compatibility layer. Backend behavior should remain compatible with upstream OmniMix/ChillPatcher builds; frontend-side adapters are preferred when compatibility issues appear.

The VB.NET UI layer is maintained alongside [QING.UIKIT](https://github.com/Dr-hydra/QING.UIKIT), the reusable WPF UI kit extracted from this frontend work.

## Status

Version: `3.1.0`

Version 3.1.0 packages use the backend/runtime contents from the 3.0.8 release package and replace the desktop frontend with the updated VB.NET build.

Primary artifact:

```text
OmniMixPlayer.Gui.Vbnet.exe
```

The executable remains the primary local build artifact and is embedded into the full OmniMix packages. Starting with version 3.0.7, GitHub Releases no longer publish the framework-dependent or self-contained frontend executable as standalone assets.

Published release packages:

- `OmniMixPlayer_V{version}_VBNet_portable.zip`: complete self-contained portable package.
- `OmniMixPlayer_V{version}_VBNet_full-framework-dependent.zip`: complete framework-dependent package.
- `OmniMixPlayer_V{version}_VBNet_installer.exe`: complete Windows installer.

## What This Frontend Does

- Starts or discovers the existing OmniMix backend.
- Displays backend connection state in the title area.
- Provides playback controls, queue/history management, cover loading, repeat/shuffle modes, and draggable progress.
- Reads the backend music library and adds tracks directly to game/player queues.
- Hosts upstream module UI pages inside the VB.NET interface.
- Deploys supported game integration bridge files from existing packaged assets.
- Synchronizes game integration instance IDs and port files with the running backend.
- Cleans stale game integration instances whose IDs no longer match the expected bridge binding.
- Provides settings for backend path, backend lifetime, personalization, service controls, and equalizer controls.
- Adds a QING.UIKIT-aligned personalization panel with configurable opacity, default/custom/solid backgrounds, and HSL custom theme controls.

## Compatibility Notes

The VB.NET frontend does not require backend API changes for normal use. When the frontend needs extra compatibility behavior, it writes or repairs frontend-controlled files such as:

- `.omnimix_instance_id`
- `omnimix_port.txt`
- game bridge DLL deployment files

For FH6-style bridge integrations, root DLLs are copied as real files instead of being installed as symbolic links, reducing game-side DLL loading ambiguity.

Startup order:

- Starting the frontend first is preferred. The frontend can launch/discover the backend and refresh game port files before the game bridge connects.
- Starting the game first can still work if the bridge starts or discovers a backend, but stale port files or stale backend processes may cause the bridge to bind to an old instance.
- The frontend now repairs known game integration bindings whenever it connects to the backend.

## Build

Install the .NET SDK used by the project, then run:

```powershell
dotnet build "OmniMixPlayer/OmniMixPlayer.sln" -c Debug -v minimal
```

Local single-file publish:

```powershell
dotnet publish "OmniMixPlayer/gui_vbnet/OmniMixFrontend/OmniMixFrontend.vbproj" `
  -c Debug `
  -o "OmniMixPlayer/bin/GuiVbnetSingle" `
  /p:PublishSingleFile=true `
  /p:SelfContained=true `
  /p:RuntimeIdentifier=win-x64 `
  /p:EnableCompressionInSingleFile=true `
  /p:PublishReadyToRun=false `
  -v minimal
```

Expected output:

```text
OmniMixPlayer/bin/GuiVbnetSingle/OmniMixPlayer.Gui.Vbnet.exe
```

## Deployment

For local development or compatibility testing, copy the generated executable into an existing OmniMix distribution directory, for example:

```text
E:\FH6\ChillPatcher\OmniMixPlayer.Gui.Vbnet.exe
```

The directory should also contain the backend executable and existing backend assets/modules.

## License

This project is licensed under the GNU General Public License v3.0. See `LICENSE`.

Third-party components keep their own licenses in their original subdirectories.
