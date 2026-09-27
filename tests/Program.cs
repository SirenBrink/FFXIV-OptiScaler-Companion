using System.Diagnostics;
using System.Runtime.InteropServices;
using FFXIVOptiScalerCompanion;

unsafe class Program
{
    static void Check(bool passed, string label) { if (!passed) throw new Exception(label); }
    static void Main(string[] args)
    {
        var gate = new GameplayGate();
        Check(!gate.Observe(0, false, 1, 1, 1920, 1080), "Loading never ready");
        for (int t = 0; t < 2000; t += 100)
            Check(!gate.Observe(t, true, 1, 1, 1920, 1080), "World draw warmup");
        Check(gate.Observe(2000, true, 1, 1, 1920, 1080), "Stable world ready");
        Check(!gate.Observe(2100, true, 2, 1, 1920, 1080), "Zoning resets readiness");
        for (int t = 2200; t <= 4100; t += 100) gate.Observe(t, true, 2, 1, 1920, 1080);
        Check(!gate.Observe(4500, true, 2, 1, 1920, 1080), "Missing draws reset readiness");
        Check(!gate.Observe(4600, true, 2, 2, 1920, 1080), "Recreated addon warms up");
        Check(!gate.Observe(4700, true, 2, 2, 2560, 1440), "Resolution change warms up");
        gate.Reset();
        Check(!gate.Observe(4800, true, 2, 2, 2560, 1440), "Explicit reset cannot reuse history");
        Check(sizeof(Frame) == 160 && sizeof(Plate) == 200 && sizeof(BridgeStatus) == 40, "ABI sizes");
        Check(Marshal.OffsetOf<Frame>(nameof(Frame.View)).ToInt32() == 32, "Camera offset");
        Check(Marshal.OffsetOf<Plate>(nameof(Plate.Name)).ToInt32() == 68, "Name offset");
        if (args.Length == 0) { Console.WriteLine("PASS: C# ABI sizes and offsets"); return; }
        // Only load the headless fixture compiled from the production bridge source.
        // Loading the full OptiScaler DLL into a test process would run its hooks.
        var library = NativeLibrary.Load(Path.GetFullPath(args[0]));
        try
        {
            var open = (delegate* unmanaged[Cdecl]<uint, uint, uint, uint, long, ulong>)NativeLibrary.GetExport(library, "OptiScalerCompanion_OpenV1");
            var submit = (delegate* unmanaged[Cdecl]<ulong, Frame*, Plate*, uint, int>)NativeLibrary.GetExport(library, "OptiScalerCompanion_SubmitV1");
            var query = (delegate* unmanaged[Cdecl]<ulong, BridgeStatus*, uint, int>)NativeLibrary.GetExport(library, "OptiScalerCompanion_QueryV1");
            var close = (delegate* unmanaged[Cdecl]<ulong, void>)NativeLibrary.GetExport(library, "OptiScalerCompanion_CloseV1");
            var session = open(1, 160, 200, 40, Stopwatch.Frequency);
            Check(session != 0, "Open C# -> C++");
            Plate plate = new() { ObjectId = 123, Slot = 4, AnchorX = 150, AnchorY = 250 };
            Frame frame = new() { Sequence = 1, Qpc = Stopwatch.GetTimestamp(), Width = 3840, Height = 2160, Count = 1, Flags = Protocol.Preview | Protocol.GameplayReady };
            Check(submit(session, &frame, &plate, 200) == 1, "Cross-language snapshot");
            BridgeStatus status;
            Check(query(session, &status, 40) == 1 && status.Accepted == 1 && status.Sequence == 1 && status.Count == 1, "Cross-language status");
            Check(status.Capabilities == 3, "No replacement-ready capability advertised");
            Check(submit(session, &frame, &plate, 200) == 0, "Duplicate rejected");
            frame.Sequence++; frame.Count = 0; frame.Qpc = Stopwatch.GetTimestamp();
            Check(submit(session, &frame, null, 0) == 1, "Clear");
            Check(query(session, &status, 40) == 1 && status.Count == 0, "Cleared state");
            close(session);
            Check(query(session, &status, 40) == 0, "Unloaded session");
            using (var client = new NativeBridge())
            {
                client.Poll();
                Check(client.Connected, "Discover already-loaded module by exports");
                Check(!client.BeginNamePlate(), "Headless receiver does not claim native hooks are available");
                client.EndNamePlate();
                frame.Sequence = 1; frame.Count = 1; frame.Qpc = Stopwatch.GetTimestamp();
                Check(client.Send(ref frame, new ReadOnlySpan<Plate>(&plate, 1)), "Managed binding Send");
                client.Poll();
                Check(client.Status.Accepted == 1, "Managed binding Query");
                client.Disconnect();
                Check(!client.Connected, "Managed module-reference teardown");
            }
        }
        finally { NativeLibrary.Free(library); }
        Console.WriteLine("PASS: C# ABI and live C# -> production native bridge handshake, publish, query and disconnect");
    }
}
