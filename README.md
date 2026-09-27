# FFXIV OptiScaler Companion

A Dalamud plugin that supplies native nameplate positions, bounds, object/icon IDs and camera data to [the FFXIV OptiScaler fork](https://github.com/SirenBrink/OptiScaler_DLSSNR_Multipass_MFG_FFXIV/tree/force-perf-quality).

## Version 0.1.2

The plugin publishes metadata for the exact native NamePlate draw through a versioned, in-process interface. OptiScaler owns the optional replacement renderer. The plugin itself does not rewrite UI nodes or replace targeting and click handling.

With a matching OptiScaler build, the **FFXIV OptiScaler Companion** section supports alignment markers, copied native submissions, and a **30-second depth-tested nameplate replacement test**. The replacement separates selected nameplate drawing from the game's FG image. It retains native depth-tested pixels and transparency, supports wrapped ReShade contexts, and restores native drawing when the test ends or loses readiness.

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

## Optional nameplate test

In OptiScaler's **FFXIV OptiScaler Companion** section, optionally enable **Lightweight 2x position interpolation**, then **Start depth-tested nameplate replacement (30s)**. **Restore native nameplates** ends the test immediately. This is a timed experimental feature, not an always-on higher-refresh HUD.

Compare camera movement, clicking, overlapping nameplates and icons, building occlusion, zoning, and FG enabled/disabled. Alignment markers are a separate optional diagnostic.

## Building

Requires a .NET 10 SDK and matching Dalamud API 15 development assemblies, including FFXIVClientStructs and InteropGenerator.Runtime.

```powershell
./build.ps1 -DalamudLibPath '<Dalamud assembly directory>'
```

The build runs protocol checks and writes the plugin package under `release/`. To also verify native bridge calls, build OptiScaler's `tests/run_companion.cmd` and pass its `CompanionBridgeFixture.dll` as `-BridgeFixture`.

`protocol/CompanionProtocol.h` and `src/Protocol.cs` define the shared layout. Keep them aligned with OptiScaler's matching header. The bridge copies values, validates sizes, timestamps, sessions and counts, and clears stale data. Receipt of metadata alone never authorizes hiding the original draw; OptiScaler's separate readiness checks control that.

Implementation assistance: GPT-6 Astra.
