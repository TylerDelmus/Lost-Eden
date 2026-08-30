using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using SmokeLounge.AOtomation.Messaging.Serialization;
using UnityEngine;

class SessionCookie
{
    public uint Cookie1;
    public uint Cookie2;
}

class NetworkSession
{
    const float ZoneConnectDelaySeconds = 0f;

    readonly NetworkClient _client;
    readonly MessageSerializer _serializer = new MessageSerializer();
    readonly ConcurrentQueue<byte[]> _inboundPacketQueue = new ConcurrentQueue<byte[]>();

    ZlibTcpClient _tcpClient;
    SessionCookie _sessionCookie;
    ushort _messageId = 1;
    float _zoneConnectAt = -1f;
    IPEndPoint _pendingZoneEndpoint;

    public bool Connected => _tcpClient != null && _tcpClient.Connected;

    public NetworkSession(NetworkClient client)
    {
        _client = client;
    }

    public void Update()
    {
        while (_inboundPacketQueue.TryDequeue(out byte[] packet))
            ProcessPacket(packet);

        if (_zoneConnectAt > 0f && Time.realtimeSinceStartup >= _zoneConnectAt)
        {
            _zoneConnectAt = -1f;
            IPEndPoint endpoint = _pendingZoneEndpoint;
            _pendingZoneEndpoint = null;
            Connect(endpoint);
        }
    }

    public void ConnectToLoginServer()
    {
        try
        {
            DimensionInfo dimension = _client.Dimension;
            IPAddress address = ResolveHost(dimension.Host);
            Connect(new IPEndPoint(address, dimension.Port));
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Failed to connect to login server: {e}");
            _client.HandleTransportDrop(unexpected: true);
        }
    }

    static IPAddress ResolveHost(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress address))
            return address;

        return Dns.GetHostEntry(host).AddressList[0];
    }

    public void Connect(IPEndPoint endpoint)
    {
        Debug.Log($"[Network] Connecting to {endpoint}");
        _client.SetPhase(SessionPhase.Connecting);

        CloseSocket();

        if (_sessionCookie != null)
            _messageId = 1;

        _tcpClient = new ZlibTcpClient();
        _tcpClient.Disconnected += OnSocketDisconnected;
        _tcpClient.PacketRecv += packet => _inboundPacketQueue.Enqueue(packet);

        try
        {
            _tcpClient.BeginConnect(endpoint.Address, endpoint.Port, ConnectCallback,
                new ConnectState { Endpoint = endpoint, Client = _tcpClient });
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] BeginConnect failed: {e}");
            _client.HandleTransportDrop(unexpected: true);
        }
    }

    class ConnectState
    {
        public IPEndPoint Endpoint;
        public ZlibTcpClient Client;
    }

    void ConnectCallback(IAsyncResult result)
    {
        var state = (ConnectState)result.AsyncState;
        ZlibTcpClient tcp = state.Client;

        try
        {
            tcp.EndConnect(result);

            if (!ReferenceEquals(tcp, _tcpClient))
                return;

            Debug.Log($"[Network] Connected to {state.Endpoint}");
            tcp.BeginReceiving();

            _client.Post(() =>
            {
                if (!ReferenceEquals(tcp, _tcpClient))
                    return;

                if (_sessionCookie == null)
                {
                    _client.SetPhase(SessionPhase.Authenticating);
                    _client.OnLoginSocketReady();
                }
                else
                {
                    _client.SetPhase(SessionPhase.EnteringZone);
                }
            });
        }
        catch (Exception e)
        {
            if (!ReferenceEquals(tcp, _tcpClient))
                return;

            Debug.LogError($"[Network] Failed to connect to {state.Endpoint}: {e}");
            _client.Post(() => _client.HandleTransportDrop(unexpected: true));
        }
    }

    public void CloseSocket()
    {
        _zoneConnectAt = -1f;
        _pendingZoneEndpoint = null;

        if (_tcpClient == null)
            return;

        try
        {
            _tcpClient.Disconnected -= OnSocketDisconnected;
            if (_tcpClient.Connected)
                _tcpClient.Close();
            _tcpClient.Dispose();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Network] Error closing socket: {e.Message}");
        }

        _tcpClient = null;
    }

    public void ResetSession()
    {
        CloseSocket();
        _sessionCookie = null;
        _messageId = 1;
        _zoneConnectAt = -1f;
        _pendingZoneEndpoint = null;

        while (_inboundPacketQueue.TryDequeue(out _))
        {
        }
    }

    void OnSocketDisconnected()
    {
        _client.Post(() => _client.HandleTransportDrop(unexpected: true));
    }

    public void Send(MessageBody messageBody)
    {
        if (messageBody is N3Message n3Message)
            n3Message.Identity = new Identity(IdentityType.SimpleChar, _client.LocalDynelId);

        var message = new Message
        {
            Body = messageBody,
            Header = new Header
            {
                PacketType = messageBody.PacketType,
                Sender = _client.LocalDynelId,
                Receiver = messageBody.PacketType == PacketType.SystemMessage ? 1 : 2
            }
        };

        Send(message);
    }

    public void Send(Message message)
    {
        if (_tcpClient == null || !_tcpClient.Connected)
        {
            Debug.LogWarning("[Network] Send ignored — not connected.");
            return;
        }

        message.Header.MessageId = _messageId;

        using (var stream = new MemoryStream())
        {
            _serializer.Serialize(stream, message);
            byte[] packet = stream.ToArray();
            NetworkDebug.LogPacket("TX", packet, message);
            _tcpClient.Send(packet);
        }

        _messageId++;
        if (_messageId == 0xFFFF)
            _messageId = 1;
    }

    void ProcessPacket(byte[] packet)
    {
        try
        {
            Message message = _serializer.Deserialize(packet);
            if (message == null)
                return;

            NetworkDebug.LogPacket("RX", packet, message);

            if (message.Header.Sender != _client.ServerId)
                _client.ServerId = message.Header.Sender;

            _client.RaiseMessageReceived(message);

            switch (message.Header.PacketType)
            {
                case PacketType.InitiateCompressionMessage:
                    OnInitiateCompression();
                    break;
                case PacketType.PingMessage:
                    Pong(message);
                    break;
                case PacketType.SystemMessage:
                    HandleSystemMessage((SystemMessage)message.Body);
                    break;
                case PacketType.N3Message:
                    HandleN3Message((N3Message)message.Body);
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[Network] Failed to deserialize/process packet: {e}");
        }
    }

    void HandleSystemMessage(SystemMessage sysMsg)
    {
        switch (sysMsg.SystemMessageType)
        {
            case SystemMessageType.ServerSalt:
            case SystemMessageType.CharacterList:
                _client.HandleSystemMessage(sysMsg);
                break;
            case SystemMessageType.ZoneInfo:
                OnZoneInfo((ZoneInfoMessage)sysMsg);
                break;
            case SystemMessageType.ZoneRedirection:
                OnZoneRedirection((ZoneRedirectionMessage)sysMsg);
                break;
            case SystemMessageType.LoginError:
                var loginError = (LoginErrorMessage)sysMsg;
                Debug.LogError($"[Network] Login error: {loginError.Error}");
                _client.RaiseLoginFailed(loginError.Error);
                _client.HandleTransportDrop(unexpected: false);
                break;
        }
    }

    void HandleN3Message(N3Message n3Msg)
    {
        if (n3Msg.N3MessageType == N3MessageType.FullCharacter)
            _client.OnFullCharacter((FullCharacterMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.PlayfieldAnarchyF)
            _client.OnPlayfieldAnarchyF((PlayfieldAnarchyFMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.SimpleCharFullUpdate)
            _client.OnSimpleCharFullUpdate((SimpleCharFullUpdateMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.Stat)
            _client.OnStat((StatMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.CharDCMove)
            _client.OnCharDCMove((CharDCMoveMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.CharacterAction)
            _client.OnCharacterAction((CharacterActionMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.FollowTarget)
            _client.OnFollowTarget((FollowTargetMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.Despawn)
            _client.OnDynelDespawn((DespawnMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.AppearanceUpdate)
            _client.OnAppearanceUpdate((AppearanceUpdateMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.HealthDamage)
            _client.OnHealthDamage((HealthDamageMessage)n3Msg);
        else if (n3Msg.N3MessageType == N3MessageType.AttackInfo)
            _client.OnAttackInfo((AttackInfoMessage)n3Msg);
    }

    void ScheduleZoneConnect(IPEndPoint endpoint)
    {
        _pendingZoneEndpoint = endpoint;
        _zoneConnectAt = Time.realtimeSinceStartup + ZoneConnectDelaySeconds;
    }

    void OnZoneInfo(ZoneInfoMessage zoneInfo)
    {
        _sessionCookie = new SessionCookie
        {
            Cookie1 = zoneInfo.Cookie1,
            Cookie2 = zoneInfo.Cookie2
        };

        ScheduleZoneConnect(new IPEndPoint(zoneInfo.ServerIpAddress, zoneInfo.ServerPort));
    }

    void OnZoneRedirection(ZoneRedirectionMessage zoneRed)
    {
        Debug.Log($"[Network] Zone redirection to {zoneRed.ServerIpAddress}:{zoneRed.ServerPort}");
        ScheduleZoneConnect(new IPEndPoint(zoneRed.ServerIpAddress, zoneRed.ServerPort));
    }

    void OnInitiateCompression()
    {
        SendZoneLogin();
    }

    void SendZoneLogin()
    {
        Send(new ZoneLoginMessage
        {
            CharacterId = _client.LocalDynelId,
            Cookie1 = _sessionCookie.Cookie1,
            Cookie2 = _sessionCookie.Cookie2
        });
    }

    void Pong(Message pingMsg)
    {
        var pingBody = (PingMessage)pingMsg.Body;

        Send(new Message
        {
            Body = new PingMessage
            {
                PingMessageType = PingMessageType.Pong,
                ServerTime = pingBody.ServerTime,
                UpTime1 = pingBody.UpTime1,
                UpTime2 = pingBody.UpTime2,
                Unk2 = pingBody.Unk2
            },
            Header = new Header
            {
                PacketType = PacketType.PingMessage,
                Sender = _client.LocalDynelId,
                Receiver = pingMsg.Header.Sender
            }
        });
    }
}
