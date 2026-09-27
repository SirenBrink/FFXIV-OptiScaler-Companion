using System.Runtime.InteropServices;

namespace FFXIVOptiScalerCompanion;

// Mirror of protocol/CompanionProtocol.h. No pointers, strings or COM objects in a snapshot.
internal static class Protocol
{
    public const uint Version = 1;
    public const int MaxPlates = 50;
    public const uint Preview = 1, CameraValid = 2;
    public const uint WorldValid = 1, BoundsValid = 2;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct Frame
{
    public ulong Sequence;
    public long Qpc;
    public uint Width, Height, Count, Flags;
    public fixed float View[16];
    public fixed float Projection[16];
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal unsafe struct Plate
{
    public ulong ObjectId;
    public uint Slot, Flags;
    public float WorldX, WorldY, WorldZ;
    public float AnchorX, AnchorY;
    public float Left, Top, Right, Bottom;
    public int NameIcon, MarkerIcon;
    public uint TextColor, EdgeColor;
    public fixed byte Name[128];
    public uint Reserved;
}

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct BridgeStatus
{
    public ulong Accepted, Rejected, Sequence;
    public long AgeQpc;
    public uint Count, Capabilities;
}
