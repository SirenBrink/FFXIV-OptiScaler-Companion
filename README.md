# FFXIV OptiScaler Companion

A Dalamud plugin that supplies native nameplate positions, bounds, object/icon IDs and camera data to [the FFXIV OptiScaler fork](https://github.com/SirenBrink/OptiScaler_DLSSNR_Multipass_MFG_FFXIV/tree/force-perf-quality).

## Version 0.1.2

The plugin publishes metadata for the exact native NamePlate draw through a versioned, in-process interface. OptiScaler owns the optional replacement renderer. The plugin itself does not rewrite UI nodes or replace targeting and click handling.

With a matching OptiScaler build, the **FFXIV OptiScaler Companion** section supports alignment markers, copied native submissions, and a **30-second depth-tested nameplate replacement test**. The replacement separates selected nameplate drawing from the game's FG image. It retains native depth-tested pixels and transparency, supports wrapped ReShade contexts, and restores native drawing when the test ends or loses readiness.

**Lightweight 2x position interpolation** is optional and defaults off. It shows one halfway position between observed samples, then the latest position. It uses small GPU region copies rather than neural frame generation or prediction. Overlapping plates, changed labels, large jumps, stale samples and slow presentation bypass interpolation. It does not match a 6X FG factor: it can at most add one intermediate position per accepted update, and can add one overlay refresh of visual delay. Watch **Midpoints shown** and **Bypassed** to confirm actual activity.

The replacement and interpolation have been tested in-game, but remain experimental. Bounds clipping, moved occlusion edges, HDR appearance, other overlay stacking, and timing relative to generated frames still warrant testing. Use windowed or borderless mode. Native hooking is guarded for a specific supported executable; another game build must be revalidated.

## Local installation and testing

1. Build the plugin as described below. Install a matching Companion-enabled OptiScaler DLL using the game's usual proxy filename, with the game closed.
2. In Dalamud's **Experimental → Dev Plugin Locations**, register the built `release/FFXIVOptiScalerCompanion/FFXIVOptiScalerCompanion.dll`. Load it through the installed development plugins list.
3. Run `/opticompanion` to check connection status. Older OptiScaler builds leave it disconnected.
4. In OptiScaler's **FFXIV OptiScaler Companion** section, optionally enable **Lightweight 2x position interpolation**, then **Start depth-tested nameplate replacement (30s)**. **Restore native nameplates** ends the test immediately.
5. Compare camera movement with interpolation on/off, clicking, overlapping nameplates and icons, building occlusion, zoning, and FG enabled/disabled. Alignment markers are a separate optional diagnostic.

The test is not an always-on higher-refresh HUD release. No custom Dalamud repository feed is published by these source changes.

## Building

Requires a .NET 10 SDK and matching Dalamud API 15 development assemblies, including FFXIVClientStructs and InteropGenerator.Runtime.

```powershell
./build.ps1 -DalamudLibPath '<Dalamud assembly directory>'
```

The build runs protocol checks and writes the plugin package under `release/`. To also verify native bridge calls, build OptiScaler's `tests/run_companion.cmd` and pass its `CompanionBridgeFixture.dll` as `-BridgeFixture`.

`protocol/CompanionProtocol.h` and `src/Protocol.cs` define the shared layout. Keep them aligned with OptiScaler's matching header. The bridge copies values, validates sizes, timestamps, sessions and counts, and clears stale data. Receipt of metadata alone never authorizes hiding the original draw; OptiScaler's separate readiness checks control that.

Implementation assistance: GPT-6 Astra.
