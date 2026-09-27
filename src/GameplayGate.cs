namespace FFXIVOptiScalerCompanion;

// Only consecutive native draws in a stable world can authorize replacement.
internal sealed class GameplayGate
{
    private long started = -1, last;
    private uint territory, width, height;
    private nint addon;
    public void Reset() => started = -1;
    public bool Observe(long now, bool eligible, uint zone, nint owner, uint w, uint h)
    {
        if (!eligible || zone == 0 || owner == 0 || w == 0 || h == 0)
        { Reset(); return false; }
        if (started < 0 || now < last || now - last > 250 ||
            territory != zone || addon != owner || width != w || height != h)
        {
            started = now; territory = zone; addon = owner; width = w; height = h;
        }
        last = now;
        return now - started >= 2000;
    }
}
