# Companion ABI v1

The canonical native structures are in `CompanionProtocol.h`; the C# equivalent is `src/Protocol.cs`.
The OptiScaler source contains a matching copy. All calls are synchronous C calling convention on x64.
QPC timestamps use the Windows performance counter and .NET Stopwatch's frequency. All positions are
raw native pixels in the supplied swapchain dimensions; no DPI scaling or mouse prediction is applied.

Exports:

```cpp
uint64_t OptiScalerCompanion_OpenV1(uint32_t version, uint32_t frameSize,
    uint32_t plateSize, uint32_t statusSize, int64_t qpcFrequency);
int OptiScalerCompanion_SubmitV1(uint64_t session, const Frame* frame,
    const Plate* plates, uint32_t plateBytes);
int OptiScalerCompanion_QueryV1(uint64_t session, Status* status, uint32_t bytes);
void OptiScalerCompanion_CloseV1(uint64_t session);
```

Open returns a nonzero session token after checking version, all structure sizes and clock frequency.
Submit copies all data before returning. A zero plate count clears the previous snapshot and accepts
zero dimensions. A nonempty frame must have dimensions from 1 to 16384, unique plate slots below 50,
finite bounded coordinates, terminated UTF-8 names and only known flags. Sequence numbers increase
within a session. Frames from the future or older than 250 ms are rejected. A rejected snapshot also
invalidates the overlay, rather than continuing to draw old positions.

Query returns accepted/rejected counters, latest accepted sequence, age and currently fresh count.
Its capability mask is `1 | 2`: receipt and alignment markers. Neither capability authorizes hiding
originals. The API is a trusted in-process interface, not a sandbox or a validator of arbitrary pointer
addresses. Only buffer contents and layouts are validated; callers must supply valid readable memory.

Names are bounded UTF-8, with truncation at a complete code point. No raw game pointer, COM pointer or
GPU resource crosses this version of the interface. Stable IDs and native slots allow later material
association, but this is not yet a presentation-frame association protocol. Camera matrices are copied
in native storage order without transposition; consumers must establish the native projection convention
before using them. The current overlay uses native screen anchors directly.

There is deliberately no managed callback from a presentation/render thread. The receiver only reads
owned snapshots under a short lock. Native drawing and input remain untouched if either side is missing.

Optional exports added with Companion 0.1.1 (the v1 structures are unchanged):

```cpp
int OptiScalerCompanion_BeginNamePlateV1(uint64_t session);
void OptiScalerCompanion_EndNamePlateV1(uint64_t session);
```

The plugin calls these on the NamePlate addon's PreDraw and PostDraw events, on the same thread.
Begin returns zero when the session, guarded native hooks, or opt-in test is unavailable.
These calls delimit native queue submissions; they do not pass pointers or request another draw.
OptiScaler can substitute owned copies of supported command headers when their original batch is
consumed. Native vertex allocations, resources, order and hit-testing remain unchanged. Unsupported,
stale or changed commands fall back to the native list. This is not higher-refresh presentation.
Older OptiScaler builds may omit both exports; the plugin keeps ordinary snapshot reporting available.
