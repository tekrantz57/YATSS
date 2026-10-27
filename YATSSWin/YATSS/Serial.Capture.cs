using System.Globalization;

namespace YATSS;

public sealed partial class Serial
{
    private byte _practiceCaptureMask = 0xFF;
    private byte _requestedCaptureMask;
    private string? _captureRequest, _captureSession;
    private bool _captureConfirmed;
    private long _captureSentAt;

    public byte PracticeCaptureMask { get { lock (_controlGate) return (byte)(_practiceCaptureMask & ActiveCaptureMask); } }
    private byte ActiveCaptureMask => (byte)((1 << _form.ActiveLaneCount) - 1);
    private string PracticeCaptureSummary => PracticeCaptureMask == 0 ? "none" :
        string.Join(", ", Enumerable.Range(0, _form.ActiveLaneCount).Where(lane => (PracticeCaptureMask & (1 << lane)) != 0).Select(lane => lane + 1));
    private byte DesiredCaptureMask => _heatRace.State == HeatRaceState.Practice && !QualifyingActive
        ? (byte)(_practiceCaptureMask & ActiveCaptureMask) : ActiveCaptureMask;
    private bool CaptureReady => _captureConfirmed && _captureSession == _timingSession.Id &&
        _requestedCaptureMask == DesiredCaptureMask;

    internal bool CanOfferFirmwareUpdate
    {
        get
        {
            lock (_controlGate) return CanSelectPracticeLanes && !_trackPowerEnabled && !_firmwareUpdateActive &&
                !_startCountdownInProgress && _controllerSession.IsReady && IsPortOpen() && CanUpdateControllerFirmware(out _);
        }
    }

    public bool CanSelectPracticeLanes
    { get { lock (_controlGate) return _heatRace.State == HeatRaceState.Practice && !QualifyingActive &&
        !_diagnosticsActive && !_archiveBrowsing && _rerunRequest == Guid.Empty && !DemoLapStreamActive && EventChangesAllowed; } }

    public bool SetPracticeCaptureMask(byte mask)
    {
        lock (_controlGate)
        {
            if (!CanSelectPracticeLanes) return false;
            _practiceCaptureMask = (byte)(mask & ActiveCaptureMask);
            EnsureCaptureConfiguration(force: true);
            _log.Info($"Practice sensor selection requested: {_practiceCaptureMask:X2}; relay mask unchanged");
            _form.SetStatusMessage("Waiting for controller sensor-arming acknowledgement");
            return true;
        }
    }

    private void InvalidateCaptureConfiguration()
    {
        _captureConfirmed = false;
        _captureRequest = _captureSession = null;
    }

    private void EnsureCaptureConfiguration(bool force = false)
    {
        if (_timingSession.Id is not { } session || !IsPortOpen()) return;
        byte desired = DesiredCaptureMask;
        if (force || _captureSession != session || _captureRequest == null || _requestedCaptureMask != desired)
        {
            _captureConfirmed = false;
            _captureSession = session;
            _requestedCaptureMask = desired;
            do { _captureRequest = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(); }
            while (_captureRequest == "00000000");
            _captureSentAt = 0;
        }
        if (_captureConfirmed || Environment.TickCount64 - _captureSentAt < 1000) return;
        _captureSentAt = Environment.TickCount64;
        WriteLine($"CONFIG:CAPTURE:{desired:X2}:{_captureRequest}");
    }

    private void ObserveCaptureReply(LapProtocolMessage message)
    {
        const string prefix = "HELLO:CONFIG:CAPTURE:";
        if (!message.Detail.StartsWith(prefix, StringComparison.Ordinal) || message.SessionId != _captureSession) return;
        string[] parts = message.Detail[prefix.Length..].Split(':');
        if (parts.Length != 2 || parts[0].Length != 2 ||
            !byte.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte mask) ||
            mask != _requestedCaptureMask || parts[1] != _captureRequest || mask != DesiredCaptureMask || _captureConfirmed) return;
        // The acknowledgement is a stream barrier: prior queued edges have been discarded.
        _captureConfirmed = true;
        if (_heatRace.State == HeatRaceState.Practice && !QualifyingActive)
            _race.ResetCaptureBaselines();
        _log.Info($"Controller sensor capture confirmed: {mask:X2}; request {_captureRequest}");
        _form.SetStatusMessage($"Controller sensors armed: {mask:X2}");
    }
}
