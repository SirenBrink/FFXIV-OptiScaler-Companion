# FFXIV OptiScaler Companion

A Dalamud plugin that supplies native nameplate positions, bounds, object/icon IDs and camera data to [the FFXIV OptiScaler fork](https://github.com/SirenBrink/OptiScaler_DLSSNR_Multipass_MFG_FFXIV/tree/force-perf-quality).

## Version 0.1.3

Adds a gameplay-readiness signal for the persistent HUD controls in a matching OptiScaler build. Replacement waits until logged in, outside loading/logout transitions, with two seconds of consecutive NamePlate draws in the same territory, addon instance and resolution. Missing draws, zoning and addon destruction revoke readiness. Source version 0.1.3 requires a matching OptiScaler receiver; the public plugin feed remains at 0.1.2 until the matching plugin release is published.

The plugin publishes metadata for the exact native NamePlate draw through a versioned, in-process interface. OptiScaler owns the optional replacement renderer. The plugin itself does not rewrite UI nodes or replace targeting and click handling.

With a matching OptiScaler build, the **FFXIV OptiScaler Companion** section provides **HUD replacement (nameplates and icons)** and **HUD interpolation (2x)** checkboxes. Replacement separates selected nameplate drawing from the game's FG image. It retains native depth-tested pixels and transparency, supports wrapped ReShade contexts, and restores native drawing when disabled or unavailable. Both options require an OptiFG DLSS-G or XeFG presenter and are greyed out otherwise. No Dalamud-wide configuration changes are required.

**Lightweight 2x position interpolation** is optional and defaults off. It shows one halfway position between observed samples, then the latest position. It uses small GPU region copies rather than neural frame generation or prediction. Overlapping plates, changed labels, large jumps, stale samples and slow presentation bypass interpolation. It does not match a 6X FG factor: it can at most add one intermediate position per accepted update, and can add one overlay refresh of visual delay. Watch **Midpoints shown** and **Bypassed** to confirm actual activity.

The replacement and interpolation have been tested in-game, but remain experimental. Bounds clipping, moved occlusion edges, HDR appearance, other overlay stacking, and timing relative to generated frames still warrant testing. Use windowed or borderless mode. Native hooking is guarded for a specific supported executable; another game build must be revalidated.

## Installation

1. Install a matching [FFXIV OptiScaler release](https://github.com/SirenBrink/OptiScaler_DLSSNR_Multipass_MFG_FFXIV/releases).
2. Open Dalamud settings with `/xlsettings`. Under **Experimental → Custom Plugin Repositories**, add this URL, enable it, and save:

   ```text
   https://raw.githubusercontent.com/SirenBrink/FFXIV-OptiScaler-Companion/main/repo.json
   ```

3. Open `/xlplugins`, search for **FFXIV OptiScaler Companion**, and install it.
4. Run `/opticompanion` to check connection status. A compatible OptiScaler build must be loaded for the bridge to connect.

## Optional nameplate replacement

Select OptiFG DLSS-G or XeFG and restart the game. In OptiScaler's **FFXIV OptiScaler Companion** section, enable **HUD replacement (nameplates and icons)** and optionally **HUD interpolation (2x)**. Both default off and are saved in OptiScaler.ini. Replacement runs until disabled, with suspension during loading and loss of readiness. Interpolation can be configured while replacement is off. Its count increases only when an intermediate position is actually displayed.

DLSS-G and XeFG gameplay tests passed, including OptiHDR with XeFG. The plain HDR/no-FG presenter is blocked after reproducible crashes; the precise underlying native fault remains unresolved. Detailed diagnostics and the old timed tests are available only with logging enabled. Menus, chat and other UI are not replaced.

## Building

Requires a .NET 10 SDK and matching Dalamud API 15 development assemblies, including FFXIVClientStructs and InteropGenerator.Runtime.

```powershell
./build.ps1 -DalamudLibPath '<Dalamud assembly directory>'
```

The build runs protocol checks and writes the plugin package under `release/`. To also verify native bridge calls, build OptiScaler's `tests/run_companion.cmd` and pass its `CompanionBridgeFixture.dll` as `-BridgeFixture`.

`protocol/CompanionProtocol.h` and `src/Protocol.cs` define the shared layout. Keep them aligned with OptiScaler's matching header. The bridge copies values, validates sizes, timestamps, sessions and counts, and clears stale data. Receipt of metadata alone never authorizes hiding the original draw; OptiScaler's separate readiness checks control that.

Implementation assistance: GPT-6 Astra.
