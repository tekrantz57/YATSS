using System.Globalization;

namespace YATSS
{
    public enum LapProtocolMessageKind
    {
        Edge,
        Hello,
        Heartbeat,
        Diagnostic,
        Error,
        Ignored,
        Invalid
    }

    public sealed record LapEdge(int LaneIndex, uint Sequence, uint TimestampMillis);

    public sealed record ControllerIdentity(
        int ProtocolVersion,
        int LaneCount,
        string BoardProfile,
        string FirmwareVersion,
        long? FlashCapacityBytes = null)
    {
        public bool HasBoardProfile => !string.IsNullOrWhiteSpace(BoardProfile);
    }

    public abstract record ControllerDiagnostic;

    public sealed record ControllerDiagnosticStatus(
        byte SensorActiveMask,
        byte TrackPowerEnabledMask,
        uint DebounceMilliseconds,
        uint DroppedEvents,
        uint TimestampMillis) : ControllerDiagnostic;

    public sealed record ControllerDiagnosticSensor(
        int LaneIndex,
        bool Active,
        uint TransitionCount,
        uint AcceptedEdgeCount,
        uint TimestampMillis) : ControllerDiagnostic;

    public sealed record ControllerDiagnosticRelay(
        int LaneIndex,
        string State,
        byte TrackPowerEnabledMask,
        uint TimestampMillis) : ControllerDiagnostic;

    public sealed record ControllerDiagnosticSession(
        string State,
        string Reason,
        uint TimestampMillis) : ControllerDiagnostic;

    public sealed record LapProtocolMessage(
        LapProtocolMessageKind Kind,
        LapEdge? Edge,
        uint? ControllerTimestampMillis,
        string RawLine,
        string Detail,
        ControllerDiagnostic? Diagnostic = null,
        ControllerIdentity? ControllerIdentity = null)
    {
        public static LapProtocolMessage Invalid(string rawLine, string detail) =>
            new(LapProtocolMessageKind.Invalid, null, null, rawLine, detail);

        public static LapProtocolMessage Ignored(string rawLine, string detail) =>
            new(LapProtocolMessageKind.Ignored, null, null, rawLine, detail);
    }

    public static class LapProtocolParser
    {
        public const int LaneCount = 8;

        public static string EncodeFrame(string body) =>
            $"{body}*{CalculateChecksum(body):X2}";

        public static LapProtocolMessage Parse(string? line)
        {
            string rawLine = (line ?? string.Empty).Trim();
            if (rawLine.Length == 0)
            {
                return LapProtocolMessage.Ignored(rawLine, "empty line");
            }

            if (!TryStripChecksum(rawLine, out string body, out string checksumError))
            {
                return LapProtocolMessage.Invalid(rawLine, checksumError);
            }

            string[] parts = body.Split(':', StringSplitOptions.TrimEntries);
            string command = parts[0].ToUpperInvariant();

            if (command == "HELLO")
            {
                return new LapProtocolMessage(
                    LapProtocolMessageKind.Hello,
                    null,
                    null,
                    rawLine,
                    body,
                    ControllerIdentity: ParseControllerIdentity(parts));
            }

            if (command == "HEARTBEAT")
            {
                if (parts.Length == 2 &&
                    uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint timestamp))
                {
                    return new LapProtocolMessage(LapProtocolMessageKind.Heartbeat, null, timestamp, rawLine, body);
                }

                return new LapProtocolMessage(LapProtocolMessageKind.Heartbeat, null, null, rawLine, body);
            }

            if (command == "ERR")
            {
                uint? timestamp = parts.Length == 3 &&
                    string.Equals(parts[1], "WINDOWS_WATCHDOG", StringComparison.OrdinalIgnoreCase) &&
                    uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint watchdogTimestamp)
                        ? watchdogTimestamp
                        : null;
                return new LapProtocolMessage(LapProtocolMessageKind.Error, null, timestamp, rawLine, body);
            }

            if (command == "DIAG")
            {
                return ParseDiagnostic(rawLine, body, parts);
            }

            if (command == "EDGE")
            {
                if (parts.Length != 4)
                {
                    return LapProtocolMessage.Invalid(rawLine, "EDGE requires lane, sequence, and timestamp");
                }

                if (!TryParseLane(parts[1], out int laneIndex))
                {
                    return LapProtocolMessage.Invalid(rawLine, "lane out of range");
                }

                if (!uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint sequence))
                {
                    return LapProtocolMessage.Invalid(rawLine, "invalid sequence");
                }

                if (!uint.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint timestamp))
                {
                    return LapProtocolMessage.Invalid(rawLine, "invalid timestamp");
                }

                return new LapProtocolMessage(
                    LapProtocolMessageKind.Edge,
                    new LapEdge(laneIndex, sequence, timestamp),
                    timestamp,
                    rawLine,
                    "edge");
            }

            return LapProtocolMessage.Invalid(rawLine, "unknown protocol line");
        }

        private static ControllerIdentity? ParseControllerIdentity(string[] parts)
        {
            if (parts.Length < 4 ||
                !string.Equals(parts[1], "YATSSMC", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int protocolVersion) ||
                !int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int laneCount) ||
                protocolVersion < 1 || laneCount < 1)
            {
                return null;
            }

            string boardProfile = parts.Length >= 5 ? parts[4].Trim().ToUpperInvariant() : string.Empty;
            string firmwareVersion = parts.Length >= 6 ? parts[5].Trim() : string.Empty;
            long? flashCapacityBytes = parts.Length >= 7 &&
                long.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out long parsedCapacity) &&
                parsedCapacity > 0
                    ? parsedCapacity
                    : null;
            return new ControllerIdentity(
                protocolVersion,
                laneCount,
                boardProfile,
                firmwareVersion,
                flashCapacityBytes);
        }

        private static LapProtocolMessage ParseDiagnostic(string rawLine, string body, string[] parts)
        {
            if (parts.Length == 7 &&
                string.Equals(parts[1], "STATUS", StringComparison.OrdinalIgnoreCase) &&
                TryParseMask(parts[2], out byte sensorMask) &&
                TryParseMask(parts[3], out byte powerMask) &&
                uint.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out uint debounce) &&
                uint.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out uint dropped) &&
                uint.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out uint statusTimestamp))
            {
                return new LapProtocolMessage(
                    LapProtocolMessageKind.Diagnostic,
                    null,
                    statusTimestamp,
                    rawLine,
                    body,
                    new ControllerDiagnosticStatus(sensorMask, powerMask, debounce, dropped, statusTimestamp));
            }

            if (parts.Length == 7 &&
                string.Equals(parts[1], "SENSOR", StringComparison.OrdinalIgnoreCase) &&
                TryParseLane(parts[2], out int laneIndex) &&
                TryParseSensorState(parts[3], out bool active) &&
                uint.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out uint transitions) &&
                uint.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out uint acceptedEdges) &&
                uint.TryParse(parts[6], NumberStyles.None, CultureInfo.InvariantCulture, out uint sensorTimestamp))
            {
                return new LapProtocolMessage(
                    LapProtocolMessageKind.Diagnostic,
                    null,
                    sensorTimestamp,
                    rawLine,
                    body,
                    new ControllerDiagnosticSensor(laneIndex, active, transitions, acceptedEdges, sensorTimestamp));
            }

            if (parts.Length == 6 &&
                string.Equals(parts[1], "RELAY", StringComparison.OrdinalIgnoreCase) &&
                TryParseLane(parts[2], out int relayLaneIndex) &&
                (string.Equals(parts[3], "PULSING", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(parts[3], "RESTORED", StringComparison.OrdinalIgnoreCase)) &&
                TryParseMask(parts[4], out byte relayPowerMask) &&
                uint.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out uint relayTimestamp))
            {
                return new LapProtocolMessage(
                    LapProtocolMessageKind.Diagnostic,
                    null,
                    relayTimestamp,
                    rawLine,
                    body,
                    new ControllerDiagnosticRelay(
                        relayLaneIndex,
                        parts[3].ToUpperInvariant(),
                        relayPowerMask,
                        relayTimestamp));
            }

            if ((parts.Length == 4 || parts.Length == 5) &&
                string.Equals(parts[1], "SESSION", StringComparison.OrdinalIgnoreCase) &&
                uint.TryParse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture, out uint sessionTimestamp))
            {
                string reason = parts.Length == 5 ? parts[3].ToUpperInvariant() : string.Empty;
                return new LapProtocolMessage(
                    LapProtocolMessageKind.Diagnostic,
                    null,
                    sessionTimestamp,
                    rawLine,
                    body,
                    new ControllerDiagnosticSession(parts[2].ToUpperInvariant(), reason, sessionTimestamp));
            }

            return LapProtocolMessage.Invalid(rawLine, "invalid diagnostic line");
        }

        private static bool TryParseMask(string value, out byte mask)
        {
            mask = 0;
            return value.Length == 2 &&
                byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out mask);
        }

        private static bool TryParseSensorState(string value, out bool active)
        {
            if (string.Equals(value, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                active = true;
                return true;
            }

            if (string.Equals(value, "CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                active = false;
                return true;
            }

            active = false;
            return false;
        }

        private static bool TryStripChecksum(string rawLine, out string body, out string error)
        {
            body = rawLine;
            error = string.Empty;
            int marker = rawLine.LastIndexOf('*');

            if (marker < 0)
            {
                error = "checksum required";
                return false;
            }

            body = rawLine[..marker];
            string checksumText = rawLine[(marker + 1)..];
            if (checksumText.Length != 2 ||
                !byte.TryParse(checksumText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte expected))
            {
                error = "invalid checksum format";
                return false;
            }

            byte actual = CalculateChecksum(body);
            if (actual != expected)
            {
                error = $"checksum mismatch expected {expected:X2} actual {actual:X2}";
                return false;
            }

            return true;
        }

        private static byte CalculateChecksum(string body)
        {
            byte checksum = 0;
            foreach (char c in body)
            {
                checksum ^= (byte)c;
            }

            return checksum;
        }

        private static bool TryParseLane(string value, out int laneIndex)
        {
            laneIndex = -1;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int lane))
            {
                return false;
            }

            if (lane >= 0 && lane < LaneCount)
            {
                laneIndex = lane;
                return true;
            }

            return false;
        }
    }
}
