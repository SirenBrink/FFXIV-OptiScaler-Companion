using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Configuration;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace FFXIVOptiScalerCompanion;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool Enabled = true;
    public bool AlignmentMarkers;
}

public sealed unsafe class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface Interface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static INamePlateGui NamePlates { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle Addons { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    private readonly Configuration config;
    private readonly NativeBridge bridge = new();
    private readonly object captureGate = new();
    private bool disposed;
    private bool nativeScope;
    private readonly Plate[] metadata = new Plate[Protocol.MaxPlates];
    private readonly Plate[] snapshot = new Plate[Protocol.MaxPlates];
    private ulong sequence;
    private long lastDraw, nextPoll;
    private bool window, faulted;
    private uint lastCount;
    private string captureStatus = "Waiting for NamePlate drawing";

    public Plugin()
    {
        config = Interface.GetPluginConfig() as Configuration ?? new();
        // Never leave test markers active on the next launch.
        config.AlignmentMarkers = false;
        Commands.AddHandler("/opticompanion", new CommandInfo(OnCommand) { HelpMessage = "Open FFXIV OptiScaler Companion controls." });
        Interface.UiBuilder.Draw += Draw;
        Interface.UiBuilder.OpenMainUi += Open;
        Interface.UiBuilder.OpenConfigUi += Open;
        NamePlates.OnPostDataUpdate += OnNameplateData;
        Addons.RegisterListener(AddonEvent.PreDraw, "NamePlate", OnNameplateBegin);
        Addons.RegisterListener(AddonEvent.PostDraw, "NamePlate", OnNameplateDraw);
        Addons.RegisterListener(AddonEvent.PreFinalize, "NamePlate", OnFinalize);
        Framework.Update += Update;
    }

    private void Open() => window = true;
    private void OnCommand(string command, string arguments) => Open();
    private void Save() => Interface.SavePluginConfig(config);

    private void OnNameplateData(INamePlateUpdateContext context, IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        lock (captureGate) { if (!disposed) CopyMetadata(handlers); }
    }

    private void CopyMetadata(IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        if (!config.Enabled || faulted) return;
        try
        {
            // Handlers and game object wrappers are frame-scoped. Copy values only.
            Array.Clear(metadata);
            foreach (var handler in handlers)
            {
                var slot = handler.NamePlateIndex;
                if ((uint)slot >= Protocol.MaxPlates) continue;
                ref var p = ref metadata[slot];
                p.ObjectId = handler.GameObjectId;
                p.Slot = (uint)slot;
                p.NameIcon = handler.NameIconId;
                p.MarkerIcon = handler.MarkerIconId;
                p.TextColor = handler.TextColor;
                p.EdgeColor = handler.EdgeColor;
                var name = handler.Name.TextValue;
                fixed (byte* destination = p.Name)
                {
                    var output = new Span<byte>(destination, 128);
                    output.Clear();
                    Encoding.UTF8.GetEncoder().Convert(name.AsSpan(), output[..127], true, out _, out _, out _);
                }
            }
        }
        catch (Exception e) { Fail(e); }
    }

    private static bool Visible(AtkResNode* node)
    {
        // Bound traversal handles malformed/cyclic UI trees without hanging a render callback.
        for (int depth = 0; node != null && depth < 64; depth++, node = node->ParentNode)
            if ((node->NodeFlags & NodeFlags.Visible) == 0 || node->Color.A == 0) return false;
        return node == null;
    }

    private static void Bounds(AtkResNode* node, ref Plate plate)
    {
        if (node == null || !Visible(node) || node->Width == 0 || node->Height == 0) return;
        var origin = new Vector2(node->ScreenX, node->ScreenY);
        var x = new Vector2(node->Transform.M11, node->Transform.M12) * node->Width;
        var y = new Vector2(node->Transform.M21, node->Transform.M22) * node->Height;
        var low = Vector2.Min(Vector2.Min(origin, origin + x), Vector2.Min(origin + y, origin + x + y));
        var high = Vector2.Max(Vector2.Max(origin, origin + x), Vector2.Max(origin + y, origin + x + y));
        if ((plate.Flags & Protocol.BoundsValid) == 0)
        { plate.Left = low.X; plate.Top = low.Y; plate.Right = high.X; plate.Bottom = high.Y; }
        else
        { plate.Left = Math.Min(plate.Left, low.X); plate.Top = Math.Min(plate.Top, low.Y); plate.Right = Math.Max(plate.Right, high.X); plate.Bottom = Math.Max(plate.Bottom, high.Y); }
        plate.Flags |= Protocol.BoundsValid;
    }

    private void OnNameplateDraw(AddonEvent type, AddonArgs args)
    {
        lock (captureGate)
        {
            if (disposed) return;
            try
            {
                Capture(args);
                // Publish metadata for this exact native draw before its commands
                // are handed to OptiScaler. This permits guarded temporal pairing.
                if (nativeScope) bridge.EndNamePlate();
                nativeScope = false;
            }
            catch (Exception e) { nativeScope = false; Fail(e); }
        }
    }

    private void OnNameplateBegin(AddonEvent type, AddonArgs args)
    {
        lock (captureGate)
        {
            if (disposed || !config.Enabled || faulted) return;
            try { nativeScope = bridge.BeginNamePlate(); }
            catch (Exception e) { nativeScope = false; Fail(e); }
        }
    }

    private void Capture(AddonArgs args)
    {
        if (!config.Enabled || !bridge.Connected || faulted) return;
        try
        {
            var addon = (AddonNamePlate*)args.Addon.Address;
            var device = Device.Instance();
            var ui = UIModule.Instance();
            if (addon == null || addon->NamePlateObjectArray == null || device == null || ui == null || ui->ShouldExitGame)
            { Clear(); return; }
            var ui3d = ui->GetUI3DModule();
            if (ui3d == null || device->SwapChain == null) { Clear(); return; }
            Frame frame = new()
            {
                Sequence = ++sequence, Qpc = Stopwatch.GetTimestamp(),
                Width = device->SwapChain->Width, Height = device->SwapChain->Height,
                Flags = config.AlignmentMarkers ? Protocol.Preview : 0,
            };
            var cameras = CameraManager.Instance();
            var camera = cameras != null ? cameras->GetActiveCamera() : null;
            if (camera != null && camera->SceneCamera.RenderCamera != null)
            {
                Unsafe.CopyBlock(frame.View, &camera->SceneCamera.RenderCamera->ViewMatrix, 64);
                Unsafe.CopyBlock(frame.Projection, &camera->SceneCamera.RenderCamera->ProjectionMatrix, 64);
                frame.Flags |= Protocol.CameraValid;
            }
            var count = 0;
            for (int slot = 0; slot < Protocol.MaxPlates; slot++)
            {
                var native = &addon->NamePlateObjectArray[slot];
                var root = (AtkResNode*)native->RootComponentNode;
                var id = ui3d->NamePlateObjectIds[slot].Id;
                if (root == null || !Visible(root) || id == 0 || id == 0xE0000000) continue;
                var p = metadata[slot];
                // Do not associate an old name/icon with a slot recycled for a different object.
                if (p.ObjectId != id) p = default;
                p.ObjectId = id; p.Slot = (uint)slot;
                p.AnchorX = root->ScreenX; p.AnchorY = root->ScreenY;
                Bounds(native->NameContainer, ref p);
                Bounds((AtkResNode*)native->NameText, ref p);
                Bounds((AtkResNode*)native->NameIcon, ref p);
                Bounds((AtkResNode*)native->MarkerIcon, ref p);
                Bounds((AtkResNode*)native->GaugeBackground, ref p);
                Bounds((AtkResNode*)native->GaugeFill, ref p);
                // Sorted list index and plate slot are different. Match by native plate index.
                for (int i = 0; i < Math.Clamp(ui3d->NamePlateObjectInfoCount, 0, Protocol.MaxPlates); i++)
                {
                    var info = ui3d->NamePlateObjectInfoPointers[i].Value;
                    if (info == null || info->NamePlateIndex != slot) continue;
                    p.WorldX = info->NamePlatePos.X; p.WorldY = info->NamePlatePos.Y; p.WorldZ = info->NamePlatePos.Z;
                    p.Flags |= Protocol.WorldValid;
                    break;
                }
                snapshot[count++] = p;
            }
            lastDraw = Environment.TickCount64;
            lastCount = (uint)count;
            captureStatus = bridge.Send(ref frame, snapshot.AsSpan(0, count)) ? "Native post-draw snapshot accepted" : "Snapshot rejected; originals untouched";
        }
        catch (Exception e) { Fail(e); }
    }

    private void Fail(Exception e)
    {
        faulted = true;
        captureStatus = "Capture stopped after an error; reload the plugin to retry";
        bridge.Disconnect();
        Log.Error(e, "Companion capture stopped. Native nameplates were not modified.");
    }

    private void Clear()
    {
        lastCount = 0; Array.Clear(metadata);
        // An empty snapshot explicitly invalidates old targets during zoning or addon destruction.
        Frame frame = new() { Sequence = ++sequence, Qpc = Stopwatch.GetTimestamp() };
        bridge.Send(ref frame, ReadOnlySpan<Plate>.Empty);
    }
    private void OnFinalize(AddonEvent type, AddonArgs args)
    {
        lock (captureGate) { if (!disposed) Clear(); }
    }
    private void Update(IFramework framework)
    {
        lock (captureGate) { if (!disposed) Poll(); }
    }

    private void Poll()
    {
        if (!config.Enabled || faulted) return;
        var now = Environment.TickCount64;
        if (now < nextPoll) return;
        nextPoll = now + 250;
        try
        {
            bridge.Poll();
            if (lastCount != 0 && now - lastDraw > 250) Clear();
        }
        catch (Exception e) { Fail(e); }
    }

    private void Draw()
    {
        if (!window) return;
        ImGui.SetNextWindowSize(new Vector2(540, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("FFXIV OptiScaler Companion", ref window, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (ImGui.Checkbox("Send native nameplate data", ref config.Enabled))
            {
                if (!config.Enabled) bridge.Disconnect();
                Save();
            }
            if (ImGui.Checkbox("Show alignment markers in OptiScaler", ref config.AlignmentMarkers)) Save();
            ImGui.TextWrapped(bridge.Message);
            ImGui.TextWrapped(captureStatus);
            var status = bridge.Status;
            ImGui.Text($"Visible nameplates: {lastCount} | Accepted: {status.Accepted} | Rejected: {status.Rejected}");
            if (status.Sequence != 0) ImGui.Text($"Snapshot age: {1000.0 * status.AgeQpc / Stopwatch.Frequency:F1} ms");
            ImGui.Separator();
            ImGui.TextWrapped("Alignment test: green crosses mark native anchors; boxes show native UI node bounds. Check Striking Dummy while rotating and zooming the camera.");
            ImGui.TextWrapped("Native nameplates and mouse targeting remain enabled. This build does not hide or replace the HUD, and does not increase its refresh rate.");
            ImGui.TextWrapped("OptiScaler's Companion section also offers copied nameplate submissions. That test substitutes command copies at the native draw point, preserving the original appearance and refresh rate.");
        }
        ImGui.End();
    }

    public void Dispose()
    {
        lock (captureGate) disposed = true;
        Framework.Update -= Update;
        NamePlates.OnPostDataUpdate -= OnNameplateData;
        Addons.UnregisterListener(AddonEvent.PreDraw, "NamePlate", OnNameplateBegin);
        Addons.UnregisterListener(AddonEvent.PostDraw, "NamePlate", OnNameplateDraw);
        Addons.UnregisterListener(AddonEvent.PreFinalize, "NamePlate", OnFinalize);
        Interface.UiBuilder.Draw -= Draw;
        Interface.UiBuilder.OpenMainUi -= Open;
        Interface.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/opticompanion");
        bridge.Dispose();
    }
}
