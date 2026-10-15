namespace YATSS;

internal enum TimingSessionFrame { Established, Current, Changed, Retired, Unknown, Unsupported }

// Retain session history across transport reconnects, not across app restarts.
internal sealed class ControllerTimingSession
{
    private readonly HashSet<string> _retired = new(StringComparer.Ordinal);
    private bool _hasEstablished;
    internal string? Id { get; private set; }

    internal TimingSessionFrame Observe(LapProtocolMessage message)
    {
        if (message.SessionId is not { } id ||
            (message.ControllerIdentity != null && message.ControllerIdentity is not
                { ProtocolVersion: 5, LaneCount: 8, HasBoardProfile: true }))
            return TimingSessionFrame.Unsupported;
        if (_retired.Contains(id)) return TimingSessionFrame.Retired;
        if (id == Id) return TimingSessionFrame.Current;
        if (message.ControllerIdentity is not { ProtocolVersion: 5, LaneCount: 8, HasBoardProfile: true })
        {
            // Do not let queued old-session replies re-authorize the controller
            // while waiting for the identity belonging to an unfamiliar frame.
            RetireCurrent();
            return TimingSessionFrame.Unknown;
        }
        bool changed = _hasEstablished;
        RetireCurrent();
        Id = id;
        _hasEstablished = true;
        return changed ? TimingSessionFrame.Changed : TimingSessionFrame.Established;
    }

    internal void RetireCurrent()
    {
        if (Id != null) _retired.Add(Id);
        Id = null;
    }
}
