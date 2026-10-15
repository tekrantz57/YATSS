namespace YATSS;

public partial class Serial
{
    internal static string EncodeControllerCommand(string body, string? session)
    {
        bool unbound = body is "TRACK_POWER:OFF" or "TRACK_POWER:MASK:00" or "PING" or "KEEPALIVE";
        return LapProtocolParser.EncodeFrame(!unbound && session != null ? body + ":" + session : body);
    }
}
