namespace Harness.BusinessLogic.Privacy;

internal sealed class SensitiveDisplayGuard : ISensitiveDisplayGuard
{
    private readonly Lock gate = new();
    private SensitiveDisplayKind? visibleKind;
    private int activeVisualCaptures;

    public SensitiveDisplayStatus Current
    {
        get
        {
            lock (gate)
            {
                return new(visibleKind is not null, visibleKind, activeVisualCaptures);
            }
        }
    }

    public bool TryBeginSensitiveDisplay(
        SensitiveDisplayKind kind,
        out ISensitiveDisplayLease? lease)
    {
        if (!Enum.IsDefined(kind))
        {
            lease = null;
            return false;
        }

        lock (gate)
        {
            if (visibleKind is not null || activeVisualCaptures != 0)
            {
                lease = null;
                return false;
            }

            visibleKind = kind;
            lease = new Lease(this, isCapture: false);
            return true;
        }
    }

    public bool TryBeginVisualCapture(out ISensitiveDisplayLease? lease)
    {
        lock (gate)
        {
            if (visibleKind is not null)
            {
                lease = null;
                return false;
            }

            activeVisualCaptures++;
            lease = new Lease(this, isCapture: true);
            return true;
        }
    }

    private void End(bool isCapture)
    {
        lock (gate)
        {
            if (isCapture)
            {
                activeVisualCaptures--;
            }
            else
            {
                visibleKind = null;
            }
        }
    }

    private sealed class Lease(SensitiveDisplayGuard owner, bool isCapture) : ISensitiveDisplayLease
    {
        private SensitiveDisplayGuard? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.End(isCapture);
    }
}
