using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace YATSS.Simulation;

public sealed record SimulatorStatus(int Port, bool Online, bool Connected, bool Silenced, bool Heartbeats,
    string Session, uint Timestamp, byte PowerMask, bool Diagnostics, long Connections, long Commands, long Frames);

// Protocol test double, not firmware or a sensor/electrical timing model.
public sealed class TcpControllerSimulator : IAsyncDisposable
{
    public const string BoardProfile = LapProtocolParser.SimulatorBoardProfile;
    private readonly object _gate = new();
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly uint[] _sequences = new uint[8];
    private readonly Task _accept, _tick;
    private readonly List<Task> _readers = new();
    private TcpClient? _client;
    private StreamWriter? _writer;
    private string _session = NewSession();
    private (string Id, uint Time)? _previous;
    private long _clockOrigin, _lastCommand, _lastHeartbeat;
    private uint _clockOffset;
    private bool _online = true, _silenced, _heartbeats = true, _diagnostics, _watchdog;
    private byte _mask;
    private uint _debounce = 1800;
    private long _connections, _commands, _frames;
    private int _lapInterval;
    private long _nextLap;
    private readonly Action<string>? _log;

    public TcpControllerSimulator(int port = 45992, Action<string>? log = null)
    {
        if (port is < 0 or > 65535 || port == 45991)
            throw new ArgumentOutOfRangeException(nameof(port), "Use a test port other than the real UNO Q port 45991.");
        _log = log;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(4);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _accept = AcceptAsync();
        _tick = TickAsync();
    }

    public int Port { get; }
    private static string NewSession()
    {
        string id;
        do { id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)); }
        while (id == "0000000000000000");
        return id;
    }
    private uint Now => unchecked(_clockOffset + (uint)(_clock.ElapsedMilliseconds - _clockOrigin));
    public SimulatorStatus Status
    {
        get { lock (_gate) return new(Port, _online, _client != null, _silenced, _heartbeats,
            _session, Now, _mask, _diagnostics, _connections, _commands, _frames); }
    }
    private void Log(string message) => _log?.Invoke(message);

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stop.Token);
                lock (_gate)
                {
                    if (!_online || _client != null) { client.Dispose(); continue; }
                    _client = client;
                    client.NoDelay = true;
                    client.GetStream().WriteTimeout = 500;
                    _writer = new StreamWriter(client.GetStream(), Encoding.ASCII, 1024, leaveOpen: true)
                    { AutoFlush = true, NewLine = "\n" };
                    _connections++;
                    Log("Client connected (one client at a time)");
                    Hello();
                    if (_heartbeats) Send("HEARTBEAT:" + Now);
                    _lastHeartbeat = _clock.ElapsedMilliseconds;
                    _readers.RemoveAll(task => task.IsCompleted);
                    _readers.Add(ReadAsync(client));
                }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
    }

    private async Task ReadAsync(TcpClient client)
    {
        try
        {
            using StreamReader reader = new(client.GetStream(), Encoding.ASCII, false, 1024, leaveOpen: true);
            while (!_stop.IsCancellationRequested)
            {
                // Bound a client command rather than allowing ReadLine to grow without limit.
                StringBuilder line = new();
                char[] character = new char[1];
                while (true)
                {
                    int count = await reader.ReadAsync(character.AsMemory(), _stop.Token);
                    if (count == 0) return;
                    if (character[0] == '\n') break;
                    if (line.Length >= 256) return;
                    line.Append(character[0]);
                }
                lock (_gate)
                {
                    if (_client != client) return;
                    Command(line.ToString().TrimEnd('\r'));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
        finally { lock (_gate) { if (_client == client) CloseClient(); } }
    }

    private void Send(string body, string? session = null, bool corrupt = false)
    {
        if (_silenced || _writer == null) return;
        string frame = LapProtocolParser.EncodeFrame(body + ":" + (session ?? _session));
        if (corrupt) frame = frame[..^2] + (frame.EndsWith("00", StringComparison.Ordinal) ? "01" : "00");
        try { _writer.WriteLine(frame); _frames++; }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        { Log("Client write failed: " + ex.Message); CloseClient(); }
    }
    private void Hello(string? session = null) => Send("HELLO:YATSSMC:5:8:" + BoardProfile + ":simulator:0", session);
    private void DiagnosticStatus() => Send($"DIAG:STATUS:00:{_mask:X2}:{_debounce}:0:{Now}");

    private void Command(string frame)
    {
        int marker = frame.LastIndexOf('*');
        if (marker <= 0 || frame.Length != marker + 3 || LapProtocolParser.EncodeFrame(frame[..marker]) != frame)
        { Send("ERR:BAD_CHECKSUM"); return; }
        string body = frame[..marker];
        string? session = null;
        int separator = body.LastIndexOf(':');
        if (separator >= 0 && LapProtocolParser.IsSessionId(body[(separator + 1)..]))
        { session = body[(separator + 1)..]; body = body[..separator]; }
        bool off = body is "TRACK_POWER:OFF" or "TRACK_POWER:MASK:00";
        bool energize = body == "TRACK_POWER:ON" || (body.StartsWith("TRACK_POWER:MASK:") && !off) || body.StartsWith("DIAG:RELAY:PULSE:");
        if (!off && ((session != null && session != _session) || (energize && session != _session)))
        { Send("ERR:STALE_SESSION"); return; }
        _commands++; _lastCommand = _clock.ElapsedMilliseconds; _watchdog = true;
        if (body == "KEEPALIVE") return;
        Log("RX " + body);
        switch (body)
        {
            case "PING": Hello(); return;
            case "RESET": ResetController(preserveClock: true, announce: true, notify: true); return;
            case "TRACK_POWER:OFF": _mask = 0; Send("HELLO:TRACK_POWER:OFF"); return;
            case "TRACK_POWER:ON": _mask = 255; Send("HELLO:TRACK_POWER:ON"); return;
            case "DIAG:START":
                _diagnostics = true; _mask = 0; Send("DIAG:SESSION:STARTED:" + Now); DiagnosticStatus(); return;
            case "DIAG:STOP":
                _diagnostics = false; _mask = 0; Send("DIAG:SESSION:STOPPED:REQUESTED:" + Now); return;
            case "DIAG:STATUS": case "DIAG:CLEAR":
                if (_diagnostics) DiagnosticStatus(); else Send("ERR:DIAG_NOT_ACTIVE"); return;
        }
        string[] parts = body.Split(':');
        if (parts is ["TRACK_POWER", "MASK", var mask])
        {
            if (mask.Length == 2 && byte.TryParse(mask, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte parsed))
            { _mask = parsed; Send("HELLO:TRACK_POWER:MASK:" + mask.ToUpperInvariant()); }
            else Send("ERR:BAD_POWER_MASK");
        }
        else if (parts is ["CONFIG", "DEBOUNCE", var debounce])
        {
            if (uint.TryParse(debounce, out uint parsed) && parsed <= 10000)
            { _debounce = parsed; Send("HELLO:CONFIG:DEBOUNCE:" + parsed); }
            else Send("ERR:BAD_DEBOUNCE");
        }
        else if (body.StartsWith("DIAG:RELAY:PULSE:"))
            Send("ERR:SIMULATOR_RELAY_UNSUPPORTED");
        else Send("ERR:UNKNOWN_COMMAND:" + body);
    }

    private async Task TickAsync()
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(25));
            while (await timer.WaitForNextTickAsync(_stop.Token))
            {
                lock (_gate)
                {
                    long elapsed = _clock.ElapsedMilliseconds;
                    if (_watchdog && elapsed - _lastCommand >= 5000)
                    {
                        bool enabled = _mask != 0;
                        _mask = 0; _watchdog = false;
                        if (enabled) Send("ERR:WINDOWS_WATCHDOG:" + Now);
                    }
                    if (_heartbeats && elapsed - _lastHeartbeat >= 1000)
                    { _lastHeartbeat = elapsed; Send("HEARTBEAT:" + Now); }
                    if (_lapInterval > 0 && !_diagnostics && elapsed >= _nextLap)
                    {
                        _nextLap = elapsed + _lapInterval;
                        for (int lane = 0; lane < 8; lane++) if ((_mask & (1 << lane)) != 0) EmitEdge(lane);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public void EmitEdge(int lane, bool corrupt = false)
    {
        if (lane is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(lane));
        lock (_gate)
        {
            if (_diagnostics) Send($"DIAG:SENSOR:{lane}:ACTIVE:1:1:{Now}");
            else Send($"EDGE:{lane}:{++_sequences[lane]}:{Now}", corrupt: corrupt);
        }
    }
    public void AdvanceClock(uint milliseconds) { lock (_gate) _clockOffset = unchecked(_clockOffset + milliseconds); }
    public void SetClock(uint timestamp) { lock (_gate) { _clockOffset = timestamp; _clockOrigin = _clock.ElapsedMilliseconds; } }
    public void SetAutomaticLaps(int milliseconds)
    {
        if (milliseconds != 0 && milliseconds < 250) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        lock (_gate) { _lapInterval = milliseconds; _nextLap = _clock.ElapsedMilliseconds + milliseconds; }
    }
    public void SetSilenced(bool value) { lock (_gate) { _silenced = value; if (!value) Hello(); } }
    public void SetHeartbeats(bool value) { lock (_gate) _heartbeats = value; }
    public void Disconnect() { lock (_gate) CloseClient(); }
    public void SetOnline(bool value) { lock (_gate) { _online = value; if (!value) CloseClient(); } }
    public void ResetController(bool preserveClock = false, bool announce = true, bool notify = false)
    {
        lock (_gate)
        {
            _previous = (_session, Now);
            if (notify) Send("HELLO:RESETTING");
            do { _session = NewSession(); } while (_session == _previous.Value.Id);
            if (!preserveClock) { _clockOffset = 0; _clockOrigin = _clock.ElapsedMilliseconds; }
            Array.Clear(_sequences); _mask = 0; _diagnostics = false; _watchdog = false;
            Log("New simulated session " + _session);
            if (announce) Hello();
        }
    }
    public void ReplayPrevious(string kind)
    {
        lock (_gate)
        {
            if (_previous is not { } old) throw new InvalidOperationException("Reset the simulator before replaying old-session traffic.");
            switch (kind)
            {
                case "edge": Send($"EDGE:0:999:{unchecked(old.Time + 1000000)}", old.Id); break;
                case "heartbeat": Send("HEARTBEAT:" + unchecked(old.Time + 1000000), old.Id); break;
                case "hello": Hello(old.Id); break;
                case "off": Send("HELLO:TRACK_POWER:OFF", old.Id); break;
                default: throw new ArgumentException("Use stale edge, heartbeat, hello, or off.");
            }
        }
    }
    public void SendInvalidChecksum() { lock (_gate) Send("HEARTBEAT:" + Now, corrupt: true); }
    private void CloseClient()
    {
        _client?.Dispose(); _client = null;
        try { _writer?.Dispose(); }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
        _writer = null;
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        lock (_gate) CloseClient();
        await Task.WhenAll(_accept, _tick);
        await Task.WhenAll(_readers);
        _stop.Dispose();
    }
}
