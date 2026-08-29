using System;
using System.Text;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using UnityEngine;

static class NetworkDebug
{
    public const bool LogRawMessages = false;
    const int MaxHexBytes = 256;

    public static void LogPacket(string direction, byte[] raw, Message message)
    {
        if (!LogRawMessages)
            return;

        Header header = message.Header;
        Debug.Log(
            $"[Network:{direction}] {header.PacketType} id={header.MessageId} " +
            $"from={header.Sender} to={header.Receiver} size={raw.Length} " +
            $"body={DescribeBody(message.Body)}\n{ToHex(raw)}");
    }

    static string DescribeBody(MessageBody body)
    {
        switch (body)
        {
            case SystemMessage sys:
                return $"System:{sys.SystemMessageType}";
            case N3Message n3:
                return $"N3:{n3.N3MessageType}";
            case PingMessage ping:
                return $"Ping:{ping.PingMessageType}";
            default:
                return body?.GetType().Name ?? "null";
        }
    }

    static string ToHex(byte[] bytes)
    {
        int length = Math.Min(bytes.Length, MaxHexBytes);
        var builder = new StringBuilder(length * 3);

        for (int i = 0; i < length; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append(bytes[i].ToString("X2"));
        }

        if (bytes.Length > MaxHexBytes)
            builder.Append($" … (+{bytes.Length - MaxHexBytes} bytes)");

        return builder.ToString();
    }
}
