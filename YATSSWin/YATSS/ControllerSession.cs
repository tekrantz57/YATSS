namespace YATSS
{
    internal sealed class ControllerSession
    {
        internal const long HeartbeatTimeoutMilliseconds = 3000;
        private readonly Func<long> _ticks;
        private bool _identityReceived;
        private bool _heartbeatReceived;
        private bool _powerOffAcknowledged;
        private long _lastHeartbeatAt;
        private long _lastTimestampAt;
        private bool _connectionOpen;

        public ControllerSession(Func<long>? ticks = null) => _ticks = ticks ?? (() => Environment.TickCount64);

        public bool HasTimestamp { get; private set; }
        public uint LastConfirmedTimestamp { get; private set; }
        public bool NeedsResume { get; private set; } = true;
        public bool IsReady => _identityReceived && _heartbeatReceived && _powerOffAcknowledged && !HeartbeatExpired;
        public bool HeartbeatExpired => _connectionOpen &&
            _ticks() - _lastHeartbeatAt >= HeartbeatTimeoutMilliseconds;
        public bool PowerAllowed => IsReady && !NeedsResume;

        public void BeginConnection()
        {
            _connectionOpen = true;
            RequireRecovery();
        }

        public void EndConnection()
        {
            _connectionOpen = false;
            RequireRecovery();
        }

        public void RequireRecovery()
        {
            NeedsResume = true;
            _identityReceived = false;
            _heartbeatReceived = false;
            _powerOffAcknowledged = false;
            _lastHeartbeatAt = _ticks();
        }

        public void ObserveIdentity() => _identityReceived = true;

        public void ObservePowerOff() => _powerOffAcknowledged = true;

        public bool IsBackwardTimestamp(uint timestamp) => HasTimestamp &&
            unchecked(timestamp - LastConfirmedTimestamp) > int.MaxValue;

        public bool ObserveTimestamp(uint timestamp, bool heartbeat, bool newClock = false)
        {
            if (!newClock && IsBackwardTimestamp(timestamp))
            {
                return false;
            }

            HasTimestamp = true;
            LastConfirmedTimestamp = timestamp;
            _lastTimestampAt = _ticks();
            if (heartbeat)
            {
                _heartbeatReceived = true;
                _lastHeartbeatAt = _ticks();
            }
            return true;
        }

        public uint EstimateTimestamp() => HasTimestamp
            ? unchecked(LastConfirmedTimestamp + (uint)Math.Clamp(
                _ticks() - _lastTimestampAt, 0, uint.MaxValue))
            : 0;

        public bool AuthorizeResume()
        {
            if (!IsReady)
            {
                return false;
            }

            NeedsResume = false;
            return true;
        }

        public bool SuspendRace(HeatRaceController heat, QualifyingController qualifying, LapRace laps)
        {
            if (heat.State == HeatRaceState.Running)
            {
                heat.Pause(LastConfirmedTimestamp);
            }

            bool qualifierInterrupted = qualifying.InterruptCurrent();
            laps.InterruptTiming();
            RequireRecovery();
            return qualifierInterrupted;
        }
    }
}
