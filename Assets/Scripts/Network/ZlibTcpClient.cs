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

    readonly List<byte> _buffer = new List<byte>();
    readonly HeaderSerializer _headerSerializer = new HeaderSerializer();

    byte[] _recvBuffer;
    bool _usingZlib;
    ZlibStream _zlibStream;

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

    public void BeginReceiving()
    {
        if (!Connected)
            return;

        try
        {
            _recvBuffer = new byte[RecvBufferSize];
            Stream stream = _usingZlib ? (Stream)_zlibStream : GetStream();
            stream.BeginRead(_recvBuffer, 0, RecvBufferSize, ReceiveCallback, null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] BeginRecv error: {e}");
            Disconnected?.Invoke();
        }
    }

    void ReceiveCallback(IAsyncResult result)
    {
        if (!Connected)
            return;

        try
        {
            Stream stream = _usingZlib ? (Stream)_zlibStream : GetStream();
            int bytesRead = stream.EndRead(result);

            if (bytesRead == 0)
            {
                Disconnected?.Invoke();
                return;
            }

            _buffer.AddRange(_recvBuffer.Take(bytesRead));
            ProcessBuffer();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Error on EndRead:\n{e}");
            Disconnected?.Invoke();
            return;
        }

        BeginReceiving();
    }

    void ProcessBuffer()
    {
        while (_buffer.Count >= HeaderSize)
        {
            Header header = DeserializeHeader(_buffer.Take(HeaderSize).ToArray());

            if (header.PacketType == PacketType.InitiateCompressionMessage)
            {
                _usingZlib = true;
                _zlibStream = new ZlibStream(GetStream(), CompressionMode.Decompress);
                _zlibStream.FlushMode = FlushType.Sync;
            }

            if (_buffer.Count < header.Size)
                break;

            PacketRecv?.Invoke(_buffer.Take(header.Size).ToArray());

            int padding = header.Size % 4 == 0 ? 0 : 4 - header.Size % 4;
            _buffer.RemoveRange(0, header.Size + (!_usingZlib ? padding : 0));
        }
    }

    Header DeserializeHeader(byte[] header)
    {
        using (MemoryStream memStream = new MemoryStream(header))
        using (StreamReader reader = new StreamReader(memStream))
            return (Header)_headerSerializer.Deserialize(reader, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _zlibStream?.Dispose();

        base.Dispose(disposing);
    }
}
