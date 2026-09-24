using System;
using System.Collections.Generic;
using System.IO;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using UnityEngine;

/// <summary>
/// Diagnostics for the zone-in hang: logs each gate between ZoneInfo and the local player being bound,
/// dumps the state once if the bind hasn't happened after WatchdogSeconds, and describes (and saves) any
/// packet that fails to deserialize. Logging only; nothing here changes behaviour.
/// </summary>
static class ZoneInTrace
{
    public static bool Enabled = true;
    const float WatchdogSeconds = 20f;
    const int RecentPacketCount = 8;

    static bool _active;
    static float _startedAt;
    static bool _watchdogFired;
    static int _failCount;
    static readonly Queue<string> _recentPackets = new Queue<string>();

    /// <summary>Supplies the playfield/player half of the watchdog dump (set by PlayfieldFactory).</summary>
    public static Func<string> StateProvider;

    public static void Begin(string reason)
    {
        if (!Enabled)
            return;

        // A fresh zone-in after the watchdog already fired (e.g. the reconnect) starts a new trace.
        if (!_active || _watchdogFired)
        {
            _active = true;
            _startedAt = Time.realtimeSinceStartup;
            _watchdogFired = false;
        }

        Mark(reason);
    }

    public static void Mark(string gate)
    {
        if (!Enabled)
            return;

        string elapsed = _active ? $"+{Time.realtimeSinceStartup - _startedAt:F2}s" : "idle";
        Debug.Log($"[ZoneIn] {elapsed} {gate}");
    }

    public static void Complete(string gate)
    {
        if (!Enabled)
            return;

        Mark($"{gate} — zone-in complete");
        _active = false;
    }

    public static void Tick(NetworkClient client)
    {
        if (!Enabled || !_active || _watchdogFired)
            return;

        if (Time.realtimeSinceStartup - _startedAt < WatchdogSeconds)
            return;

        _watchdogFired = true;
        string playfield = StateProvider != null ? StateProvider() : "(no playfield state provider)";
        Debug.LogError(
            $"[ZoneIn] STUCK: not bound {WatchdogSeconds:F0}s after zone-in began. " +
            $"phase={client.Phase} socket={(client.Connected ? "connected" : "closed")} " +
            $"localDynel={client.LocalDynelId} parseFailures={_failCount} | {playfield}\n" +
            $"recent RX: {string.Join(" > ", _recentPackets)}");
    }

    public static void RecordReceived(Message message)
    {
        if (!Enabled)
            return;

        string name = message.Body switch
        {
            N3Message n3 => $"N3:{n3.N3MessageType}",
            SystemMessage sys => $"Sys:{sys.SystemMessageType}",
            _ => message.Header.PacketType.ToString()
        };
        Remember(name);
    }

    /// <summary>
    /// Describes a packet the serializer rejected, straight from its raw bytes, and saves it to disk.
    /// AO is big-endian; the header is MessageId, PacketType, Unknown, Size (u16 each), Sender, Receiver.
    /// </summary>
    public static void RecordParseFailure(byte[] raw, Exception e)
    {
        _failCount++;

        string description = DescribeRaw(raw);
        Remember($"FAIL({description})");

        string savedTo = Save(raw, description);
        Debug.LogError(
            $"[ZoneIn] Packet failed to deserialize #{_failCount}: {description} " +
            $"— {e.GetType().Name}: {e.Message}\nsaved: {savedTo}\n" +
            $"recent RX: {string.Join(" > ", _recentPackets)}");
    }

    static string DescribeRaw(byte[] raw)
    {
        if (raw == null || raw.Length < 16)
            return $"short packet ({raw?.Length ?? 0} bytes)";

        ushort messageId = ReadU16(raw, 0);
        var packetType = (PacketType)(short)ReadU16(raw, 2);
        ushort declaredSize = ReadU16(raw, 6);
        int sender = ReadI32(raw, 8);
        int receiver = ReadI32(raw, 12);

        string subtype = "";
        if (raw.Length >= 20)
        {
            int sub = ReadI32(raw, 16);
            if (packetType == PacketType.N3Message)
                subtype = $" N3={(Enum.IsDefined(typeof(N3MessageType), sub) ? ((N3MessageType)sub).ToString() : "?")}(0x{sub:X8})";
            else if (packetType == PacketType.SystemMessage)
                subtype = $" Sys={(Enum.IsDefined(typeof(SystemMessageType), sub) ? ((SystemMessageType)sub).ToString() : "?")}(0x{sub:X8})";
            else
                subtype = $" first=0x{sub:X8}";
        }

        string identity = "";
        if (packetType == PacketType.N3Message && raw.Length >= 28)
            identity = $" identity={ReadI32(raw, 20)}:{ReadI32(raw, 24)}";

        return $"{packetType}{subtype}{identity} id={messageId} size={declaredSize} raw={raw.Length} " +
               $"from={sender} to={receiver}";
    }

    static string Save(byte[] raw, string description)
    {
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "ZoneInTrace");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"parsefail_{DateTime.Now:yyyyMMdd_HHmmss}_{_failCount}.bin");
            File.WriteAllBytes(path, raw ?? Array.Empty<byte>());
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), description);
            return path;
        }
        catch (Exception ex)
        {
            return $"(save failed: {ex.Message})";
        }
    }

    static void Remember(string entry)
    {
        _recentPackets.Enqueue(entry);
        while (_recentPackets.Count > RecentPacketCount)
            _recentPackets.Dequeue();
    }

    static ushort ReadU16(byte[] b, int o) => (ushort)((b[o] << 8) | b[o + 1]);

    static int ReadI32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
}
