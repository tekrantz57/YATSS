using System.Globalization;

namespace tlp
{
    public enum LapProtocolMessageKind
    {
        Edge,
        Hello,
        Heartbeat,
        Error,
        Ignored,
        Invalid
    }

    public sealed record LapEdge(int LaneIndex, uint? Sequence, uint TimestampMillis);

    public sealed record LapProtocolMessage(
        LapProtocolMessageKind Kind,
        LapEdge? Edge,
        string RawLine,
        string Detail)
    {
        public static LapProtocolMessage Invalid(string rawLine, string detail) =>
            new(LapProtocolMessageKind.Invalid, null, rawLine, detail);

        public static LapProtocolMessage Ignored(string rawLine, string detail) =>
            new(LapProtocolMessageKind.Ignored, null, rawLine, detail);
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
                return new LapProtocolMessage(LapProtocolMessageKind.Hello, null, rawLine, body);
            }

            if (command == "HEARTBEAT")
            {
                return new LapProtocolMessage(LapProtocolMessageKind.Heartbeat, null, rawLine, body);
            }

            if (command == "ERR")
            {
                return new LapProtocolMessage(LapProtocolMessageKind.Error, null, rawLine, body);
            }

            if (command == "EDGE")
            {
                if (parts.Length != 4)
                {
                    return LapProtocolMessage.Invalid(rawLine, "EDGE requires lane, sequence, and timestamp");
                }

                if (!TryParseLane(parts[1], zeroBasedOnly: true, out int laneIndex))
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
                    rawLine,
                    "edge");
            }

            if (parts.Length == 2 && TryParseLane(parts[0], zeroBasedOnly: true, out int legacyLane))
            {
                if (!uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint timestamp))
                {
                    return LapProtocolMessage.Invalid(rawLine, "invalid legacy timestamp");
                }

                return new LapProtocolMessage(
                    LapProtocolMessageKind.Edge,
                    new LapEdge(legacyLane, null, timestamp),
                    rawLine,
                    "legacy lane:timestamp edge");
            }

            if (parts.Length == 3 && TryParseLane(parts[0], zeroBasedOnly: false, out int oldLane))
            {
                if (!uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint timestamp))
                {
                    return LapProtocolMessage.Invalid(rawLine, "invalid old timestamp");
                }

                return new LapProtocolMessage(
                    LapProtocolMessageKind.Edge,
                    new LapEdge(oldLane, null, timestamp),
                    rawLine,
                    "old lane:laps:timestamp edge");
            }

            return LapProtocolMessage.Invalid(rawLine, "unknown protocol line");
        }

        private static bool TryStripChecksum(string rawLine, out string body, out string error)
        {
            body = rawLine;
            error = string.Empty;
            int marker = rawLine.LastIndexOf('*');

            if (marker < 0)
            {
                return true;
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

        private static bool TryParseLane(string value, bool zeroBasedOnly, out int laneIndex)
        {
            laneIndex = -1;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int lane))
            {
                return false;
            }

            if (!zeroBasedOnly && lane >= 1 && lane <= LaneCount)
            {
                laneIndex = lane - 1;
                return true;
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
