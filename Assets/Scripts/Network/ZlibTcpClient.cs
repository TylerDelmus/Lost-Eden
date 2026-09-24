using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using Ionic.Zlib;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Serialization.Serializers;
using UnityEngine;
using StreamReader = SmokeLounge.AOtomation.Messaging.Serialization.StreamReader;

class ZlibTcpClient : TcpClient
{
    const ushort HeaderSize = 16;
    const ushort RecvBufferSize = 8192;
    const int InflateBufferSize = 16384;

    readonly List<byte> _buffer = new List<byte>();
    readonly HeaderSerializer _headerSerializer = new HeaderSerializer();
    readonly byte[] _recvBuffer = new byte[RecvBufferSize];

    bool _usingZlib;
    ZlibCodec _inflater;
    byte[] _inflateBuffer;
    long _bytesReceived;
    int _packetsReceived;

    public event Action<byte[]> PacketRecv;
    public event Action Disconnected;

    public ZlibTcpClient() : base(AddressFamily.InterNetwork)
    {
        ReceiveTimeout = 180000;
    }

    public void Send(byte[] bytes)
    {
        if (!Connected)
            return;

        try
        {
            GetStream().BeginWrite(bytes, 0, bytes.Length, SendCallback, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Failed to begin send: {e}");
        }
    }

    void SendCallback(IAsyncResult result)
    {
        try
        {
            GetStream().EndWrite(result);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Failed to send message: {e}");
        }
    }

    /// <summary>
    /// Always reads raw bytes off the socket; once the server starts compression they are inflated
    /// here (see Inflate) rather than through ZlibStream, whose Read does one inflate pass per socket
    /// read — it returned 0 on a live connection (which we took for a disconnect) and could leave
    /// decoded bytes stuck in the inflater until the server happened to send more.
    /// </summary>
    public void BeginReceiving()
    {
        if (!Connected)
            return;

        try
        {
            GetStream().BeginRead(_recvBuffer, 0, RecvBufferSize, ReceiveCallback, null);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Network] BeginReceive failed: {e.Message}");
            LogClosed($"BeginReceive threw {e.GetType().Name}: {e.Message}");
            Disconnected?.Invoke();
        }
    }

    void ReceiveCallback(IAsyncResult result)
    {
        if (!Connected)
            return;

        try
        {
            int bytesRead = GetStream().EndRead(result);

            // On the raw socket stream, 0 is end of stream: the server closed the connection.
            if (bytesRead == 0)
            {
                LogClosed("read returned 0 bytes");
                Disconnected?.Invoke();
                return;
            }

            _bytesReceived += bytesRead;

            if (_usingZlib)
                Inflate(_recvBuffer, 0, bytesRead);
            else
                _buffer.AddRange(_recvBuffer.Take(bytesRead));

            ProcessBuffer();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Receive error: {e.Message}");
            LogClosed($"receive threw {e.GetType().Name}: {e.Message}");
            Disconnected?.Invoke();
            return;
        }

        BeginReceiving();
    }

    /// <summary>
    /// Inflates one chunk of the compressed stream into _buffer, running the inflater until it has
    /// consumed all the input and has nothing left to hand out, so every received byte is delivered now.
    /// </summary>
    void Inflate(byte[] input, int offset, int count)
    {
        _inflater.InputBuffer = input;
        _inflater.NextIn = offset;
        _inflater.AvailableBytesIn = count;

        while (true)
        {
            _inflater.OutputBuffer = _inflateBuffer;
            _inflater.NextOut = 0;
            _inflater.AvailableBytesOut = _inflateBuffer.Length;

            int inBefore = _inflater.AvailableBytesIn;
            int rc = _inflater.Inflate(FlushType.Sync);
            int produced = _inflateBuffer.Length - _inflater.AvailableBytesOut;

            if (produced > 0)
                _buffer.AddRange(new ArraySegment<byte>(_inflateBuffer, 0, produced));

            if (rc == ZlibConstants.Z_STREAM_END)
            {
                if (_inflater.AvailableBytesIn > 0)
                    Debug.LogWarning($"[Network] zlib stream ended with {_inflater.AvailableBytesIn} byte(s) left over; ignoring them.");
                return;
            }

            // Z_BUF_ERROR just means no progress was possible: everything available has been delivered.
            if (rc != ZlibConstants.Z_OK && rc != ZlibConstants.Z_BUF_ERROR)
                throw new ZlibException($"inflating: rc={rc} msg={_inflater.Message}");

            bool progressed = produced > 0 || _inflater.AvailableBytesIn < inBefore;
            if (!progressed || (_inflater.AvailableBytesIn == 0 && _inflater.AvailableBytesOut > 0))
                return;
        }
    }

    void ProcessBuffer()
    {
        while (_buffer.Count >= HeaderSize)
        {
            Header header = DeserializeHeader(_buffer.Take(HeaderSize).ToArray());
            // Size goes over the wire as a u16; AOtomation reads it as Int16.
            int size = (ushort)header.Size;

            if (_buffer.Count < size)
                break;

            PacketRecv?.Invoke(_buffer.Take(size).ToArray());
            _packetsReceived++;

            if (!_usingZlib && header.PacketType == PacketType.InitiateCompressionMessage)
            {
                int initiatePadding = size % 4 == 0 ? 0 : 4 - size % 4;
                _buffer.RemoveRange(0, Math.Min(_buffer.Count, size + initiatePadding));

                // Everything after this packet is the compressed stream, including any bytes that
                // arrived in the same read as it.
                _usingZlib = true;
                _inflater = new ZlibCodec();
                _inflater.InitializeInflate(true);
                _inflateBuffer = new byte[InflateBufferSize];

                byte[] compressed = _buffer.ToArray();
                _buffer.Clear();
                if (compressed.Length > 0)
                    Inflate(compressed, 0, compressed.Length);
                continue;
            }

            int padding = !_usingZlib && size % 4 != 0 ? 4 - size % 4 : 0;
            _buffer.RemoveRange(0, size + padding);
        }
    }

    /// <summary>
    /// Why the receive loop stopped, for the zone-in trace. Runs on the socket thread, so it logs
    /// directly rather than through ZoneInTrace (which reads Unity's clock).
    /// </summary>
    void LogClosed(string reason)
    {
        string peer;
        try
        {
            // Readable with nothing to read means the remote end sent FIN.
            peer = Client == null || (Client.Poll(0, SelectMode.SelectRead) && Client.Available == 0)
                ? "remote end closed the connection"
                : $"socket still open (available={Client.Available}), so the stream ended on our side";
        }
        catch (Exception e)
        {
            peer = $"socket state unknown ({e.GetType().Name})";
        }

        Debug.LogWarning(
            $"[ZoneIn] {DateTime.Now:HH:mm:ss.fff} socket closed: {reason}; {peer}; " +
            $"zlib={_usingZlib} bytesReceived={_bytesReceived} packetsReceived={_packetsReceived} buffered={_buffer.Count}");
    }

    Header DeserializeHeader(byte[] header)
    {
        using (MemoryStream memStream = new MemoryStream(header))
        using (StreamReader reader = new StreamReader(memStream))
            return (Header)_headerSerializer.Deserialize(reader, null);
    }
}
