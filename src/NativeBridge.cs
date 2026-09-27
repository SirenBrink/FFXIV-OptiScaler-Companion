using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FFXIVOptiScalerCompanion;

// Bind only to an already loaded module with the full versioned export set.
// Hold a module reference while delegates exist; never load a proxy DLL from disk.
internal sealed unsafe class NativeBridge : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate ulong OpenFn(uint version, uint frameSize, uint plateSize, uint statusSize, long frequency);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SubmitFn(ulong session, Frame* frame, Plate* plates, uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueryFn(ulong session, out BridgeStatus status, uint bytes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CloseFn(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int BeginNamePlateFn(ulong session);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void EndNamePlateFn(ulong session);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetModuleHandleExW(uint flags, string name, out nint module);
    [DllImport("kernel32")] private static extern bool FreeLibrary(nint module);
    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint module, string name);
    private nint module;
    private readonly object gate = new();
    private ulong session;
    private SubmitFn? submit;
    private QueryFn? query;
    private CloseFn? close;
    private BeginNamePlateFn? beginNamePlate;
    private EndNamePlateFn? endNamePlate;
    private long nextRetry;
    public string Message { get; private set; } = "Waiting for compatible OptiScaler";
    public bool Connected { get { lock (gate) return session != 0; } }
    private BridgeStatus statusValue;
    public BridgeStatus Status { get { lock (gate) return statusValue; } }

    private static T? Export<T>(nint handle, string name) where T : Delegate
    {
        var address = GetProcAddress(handle, name);
        return address == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
    }

    public void Poll()
    {
        lock (gate) PollLocked();
    }

    private void PollLocked()
    {
        if (Connected)
        {
            if (query!(session, out var status, (uint)sizeof(BridgeStatus)) == 1) statusValue = status;
            else Disconnect();
            return;
        }
        if (Environment.TickCount64 < nextRetry) return;
        nextRetry = Environment.TickCount64 + 2000;
        using var process = Process.GetCurrentProcess();
        foreach (ProcessModule candidate in process.Modules)
        {
            // NativeLibrary.Load would run DllMain again for a missing library. Never do that.
            if (!GetModuleHandleExW(0, candidate.FileName, out var handle)) continue;
            var adopted = false;
            try
            {
                var open = Export<OpenFn>(handle, "OptiScalerCompanion_OpenV1");
                var send = Export<SubmitFn>(handle, "OptiScalerCompanion_SubmitV1");
                var read = Export<QueryFn>(handle, "OptiScalerCompanion_QueryV1");
                var end = Export<CloseFn>(handle, "OptiScalerCompanion_CloseV1");
                if (open == null || send == null || read == null || end == null) continue;
                var token = open(Protocol.Version, (uint)sizeof(Frame), (uint)sizeof(Plate), (uint)sizeof(BridgeStatus), Stopwatch.Frequency);
                if (token == 0) { Message = "OptiScaler rejected the protocol or another Companion is connected"; continue; }
                module = handle; session = token; submit = send; query = read; close = end;
                beginNamePlate = Export<BeginNamePlateFn>(handle, "OptiScalerCompanion_BeginNamePlateV1");
                endNamePlate = Export<EndNamePlateFn>(handle, "OptiScalerCompanion_EndNamePlateV1");
                adopted = true;
                Message = $"Connected through {candidate.ModuleName}";
                return;
            }
            finally { if (!adopted) FreeLibrary(handle); }
        }
    }

    public bool Send(ref Frame frame, ReadOnlySpan<Plate> plates)
    {
        lock (gate) return SendLocked(ref frame, plates);
    }

    private bool SendLocked(ref Frame frame, ReadOnlySpan<Plate> plates)
    {
        if (!Connected || plates.Length > Protocol.MaxPlates) return false;
        frame.Count = (uint)plates.Length;
        fixed (Frame* f = &frame)
        fixed (Plate* p = plates)
            return submit!(session, f, p, (uint)(plates.Length * sizeof(Plate))) == 1;
    }

    public void Disconnect()
    {
        lock (gate) DisconnectLocked();
    }

    private void DisconnectLocked()
    {
        if (session != 0) close?.Invoke(session);
        session = 0; submit = null; query = null; close = null; statusValue = default;
        beginNamePlate = null; endNamePlate = null;
        if (module != 0) { FreeLibrary(module); module = 0; }
        Message = "Disconnected; native nameplates remain active";
    }
    public void Dispose() => Disconnect();

    public bool BeginNamePlate()
    {
        lock (gate)
            return session != 0 && beginNamePlate != null && endNamePlate != null && beginNamePlate(session) == 1;
    }
    public void EndNamePlate()
    {
        lock (gate) { if (session != 0) endNamePlate?.Invoke(session); }
    }
}
