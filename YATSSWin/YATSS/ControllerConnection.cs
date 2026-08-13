using System.IO.Ports;
using System.Net.Sockets;
using System.Text;

namespace YATSS
{
    internal interface IControllerConnection : IDisposable
    {
        bool IsOpen { get; }
        string ReadLine();
        void WriteLine(string value);
        void DiscardBuffers();
        void Close();
    }

    internal sealed class SerialControllerConnection : IControllerConnection
    {
        private readonly SerialPort _port;

        public SerialControllerConnection(SerialPort port) => _port = port;

        public bool IsOpen => _port.IsOpen;
        public string ReadLine() => _port.ReadLine();
        public void WriteLine(string value) => _port.WriteLine(value);

        public void DiscardBuffers()
        {
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public void Close() => _port.Close();
        public void Dispose() => _port.Dispose();
    }

    internal sealed class TcpControllerConnection : IControllerConnection
    {
        private readonly TcpClient _client;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private bool _closed;

        public TcpControllerConnection(string host, int port, int readTimeout, int writeTimeout)
        {
            _client = new TcpClient { NoDelay = true };
            try
            {
                _client.Connect(host, port);
                NetworkStream stream = _client.GetStream();
                stream.ReadTimeout = readTimeout;
                stream.WriteTimeout = writeTimeout;
                _reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
                _writer = new StreamWriter(stream, Encoding.ASCII, 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };
            }
            catch
            {
                _client.Dispose();
                throw;
            }
        }

        public bool IsOpen => !_closed && _client.Connected;

        public string ReadLine()
        {
            try
            {
                return _reader.ReadLine() ?? throw new IOException("Controller TCP connection closed.");
            }
            catch (IOException ex) when (IsReadTimeout(ex))
            {
                throw new TimeoutException("Controller TCP read timed out.", ex);
            }
        }

        internal static bool IsReadTimeout(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socketException &&
                    (socketException.SocketErrorCode == SocketError.TimedOut ||
                     socketException.NativeErrorCode == 10060))
                {
                    return true;
                }
            }

            // Wine may discard the nested SocketException while retaining WSAETIMEDOUT.
            return exception.Message.Contains("0x274c", StringComparison.OrdinalIgnoreCase);
        }

        public void WriteLine(string value) => _writer.WriteLine(value);
        public void DiscardBuffers() { }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _client.Close();
        }

        public void Dispose()
        {
            Close();
            _reader.Dispose();
            _writer.Dispose();
            _client.Dispose();
        }
    }

    internal static class ControllerEndpoint
    {
        public const string UnoQ = "TCP:127.0.0.1:45991";

        public static bool TryParseTcp(string value, out string host, out int port)
        {
            host = string.Empty;
            port = 0;
            if (!value.StartsWith("TCP:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string address = value[4..];
            int separator = address.LastIndexOf(':');
            if (separator <= 0 || separator == address.Length - 1)
            {
                return false;
            }

            host = address[..separator].Trim();
            return host.Length > 0 &&
                   int.TryParse(address[(separator + 1)..], out port) &&
                   port is >= 1 and <= 65535;
        }
    }
}
