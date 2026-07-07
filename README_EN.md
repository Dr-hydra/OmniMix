# OmniMix

OmniMix is a Windows desktop music and game-integration tool maintained long-term by Dr.Hydra. The project includes the OmniMix backend, module system, native audio components, game integration bridges, and a VB.NET/WPF desktop frontend.

This repository now treats the VB.NET/WPF branch as the primary maintained branch. The `main` branch is kept as a historical baseline and for possible future comparison or selective pulls. The original ChillPatcher project is part of OmniMix's history and is referenced only for credits and compatibility context.

中文主文档：[README.md](README.md)

## Status

Current version: `4.2.1`

Primary local artifact:

```text
OmniMixPlayer.Gui.Vbnet.exe
```

Release packages usually include:

- `OmniMixPlayer_V{version}_VBNet_portable.zip`: complete self-contained portable package.
- `OmniMixPlayer_V{version}_VBNet_full-framework-dependent.zip`: complete framework-dependent package.
- `OmniMixPlayer_V{version}_VBNet_installer.exe`: complete Windows installer.

Since `3.0.7`, Releases no longer publish the VB.NET frontend executable as a standalone asset. It is included in the full OmniMix packages.

## Features

- Starts or discovers the OmniMix backend and displays connection state.
- Provides playback controls, progress seeking, volume, repeat, and shuffle.
- Manages the music library, queue, playback history, and cover display.
- Hosts module UI pages inside the desktop frontend.
- Supports module enablement, settings, links, and launchpad entries.
- Supports backend service install/start/stop/autostart controls.
- Supports equalizer and instance configuration.
- Installs game integration bridges and repairs instance IDs and port files.
- Supports FH6 integration with both Steam and Xbox directory layouts.
- Provides personalization options, including background, opacity, theme colors, and HSL controls.

The VB.NET UI layer is maintained alongside [QING.UIKIT](https://github.com/Dr-hydra/QING.UIKIT), a reusable WPF UI kit extracted from this frontend work.

## Usage

### Release Packages

Regular users should download the complete package or installer instead of a standalone exe.

Portable package:

1. Extract the full package into a writable directory.
2. Run `OmniMixPlayer.Gui.Vbnet.exe`.
3. If prompted for a backend path, select `OmniMixPlayer.Backend.exe` in the same package.
4. Check backend state, modules, and music library paths in Settings.

Installer:

1. Run `OmniMixPlayer_V{version}_VBNet_installer.exe`.
2. Complete the setup wizard.
3. Start OmniMix from the Start menu or install directory.

### Game Integration

Open the "Plugins - Game Integration" page:

1. Select a supported game.
2. Choose the game directory.
3. Install the matching game integration bridge.
4. Prefer starting OmniMix before the game so it can refresh port files and instance bindings.

FH6 layout detection:

- Steam: `fh6/forzahorizon6.exe` and `fh6/media`
- Xbox: `fh6/Content/forzahorizon6.exe` and `fh6/media`

For FH6, `version.dll`, `OmniPcmShared.dll`, `.omnimix_instance_id`, and `omnimix_port.txt` are written to the actual runtime directory. This is the game root for Steam and `Content` for Xbox. Custom radio UI and generated media files are written to the real `media` directory.

### Custom Radio UI

The FH6 integration page provides a radio UI replacement workflow:

1. Select a valid FH6 directory.
2. Click the radio UI replacement action.
3. Choose a custom PNG.
4. OmniMix runs the media generator, backs up original files, and writes generated files into the game's `media` directory.

Use the restore action to recover the original radio UI.

## Build

Install the required .NET SDK, Node.js, Go, CMake, and Visual Studio C++ toolchain. For a quick desktop frontend build, run:

```powershell
dotnet build "OmniMixPlayer/gui_vbnet/OmniMixFrontend.sln" -c Debug -v minimal
```

The player build uses the shared task-tree script. The repository now has one desktop frontend, the VB.NET/WPF frontend. The `player` target publishes the backend, builds the embedded WebUI, publishes the VB.NET frontend, publishes the media generator, packages game-integration assets, and assembles `playerbuild/`:

```powershell
python scripts/build_all.py player
```

Use `--full` to also run restore and native component builds:

```powershell
python scripts/build_all.py player --full
```

Preview the task tree without running it:

```powershell
python scripts/build_all.py player --full --dry-run
```

`playerbuild/` is the base directory for installers and release packages. Important entries include:

- `OmniMixPlayer.Gui.Vbnet.exe`
- `OmniMixPlayer.Backend.exe`
- `chill-gen-media.exe`
- `OmniMixAssets/ChillPatcher.zip`
- `OmniMixAssets/FH6OmniBridge.zip`
- `modules/`
- `native/x64/`
- `wwwroot/`

Create release zip packages:

```powershell
python scripts/package_release.py 4.2.1
```

Create the Windows installer:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build_installer.ps1 -Version 4.2.1
```

The embedded backend WebUI now lives in `OmniMixPlayer/gui_web/` and is built with Vite + Svelte + TypeScript. To run it separately during development:

```powershell
npm install
npm run dev
```

## Branches

- The current VB.NET/WPF branch is the primary maintained branch.
- `main` is kept as a historical baseline and for possible future comparison or selective pulls.
- If original-project updates are needed, use a temporary sync branch and migrate only the required backend, SDK, module, or build-script changes.

## Original Project

Some historical OmniMix implementation work came from BeyondtheApex's ChillPatcher project. Thanks to the original author for the early foundation. This repository now continues as Dr.Hydra's long-term maintained branch.

Original repository:

```text
https://github.com/BeyondtheApex/ChillPatcher
```

## License

This project is licensed under the GNU General Public License v3.0. See [LICENSE](LICENSE).

Third-party components keep their own licenses in their original subdirectories.
