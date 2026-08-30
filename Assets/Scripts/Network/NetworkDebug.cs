using System;
using System.Text;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.SystemMessages;
using UnityEngine;

static class NetworkDebug
{
    public const bool LogRawMessages = false;
    public const bool LogLogin = false;
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

    public static void LogLoginConnect(DimensionInfo dimension, string username)
    {
        if (!LogLogin || dimension == null)
            return;

        Debug.Log(
            $"[Login] Connect dimension={dimension.Id} name=\"{dimension.Name}\" " +
            $"endpoint={dimension.Host}:{dimension.Port} clientVersion=\"{dimension.ClientVersion}\" " +
            $"username=\"{username}\" publicKey={dimension.PublicKey}");
    }

    public static void LogLoginUserLogin(string username, string clientVersion)
    {
        if (!LogLogin)
            return;

        Debug.Log($"[Login] TX UserLogin username=\"{username}\" clientVersion=\"{clientVersion}\"");
    }

    public static void LogLoginServerSalt(byte[] salt)
    {
        if (!LogLogin)
            return;

        if (salt == null)
        {
            Debug.LogWarning("[Login] RX ServerSalt salt=null");
            return;
        }

        bool asciiHex = IsPrintableAsciiHex(salt);
        Debug.Log(
            $"[Login] RX ServerSalt length={salt.Length} kind={(asciiHex ? "ascii-hex" : "binary")} " +
            $"hex={ToHex(salt)}");
    }

    public static void LogLoginCredentials(
        string username,
        byte[] salt,
        string publicKey,
        string challengeResponse,
        int passwordLength)
    {
        if (!LogLogin)
            return;

        int plaintextLength = salt != null
            ? (username?.Length ?? 0) + 1 + salt.Length + 1 + passwordLength
            : -1;
        int responseLength = challengeResponse?.Length ?? 0;
        string responsePreview = string.IsNullOrEmpty(challengeResponse)
            ? "<null>"
            : responseLength <= 96
                ? challengeResponse
                : challengeResponse.Substring(0, 96) + "…";

        Debug.Log(
            $"[Login] TX UserCredentials username=\"{username}\" passwordLength={passwordLength} " +
            $"saltLength={salt?.Length ?? 0} saltHex={(salt != null ? ToHex(salt) : "<null>")} plaintextLength={plaintextLength} " +
            $"publicKey={publicKey} challengeLength={responseLength} challengePreview={responsePreview}");
    }

    static bool IsPrintableAsciiHex(byte[] salt)
    {
        if (salt == null || salt.Length == 0)
            return false;

        for (int i = 0; i < salt.Length; i++)
        {
            byte b = salt[i];
            bool isDigit = b >= (byte)'0' && b <= (byte)'9';
            bool isLower = b >= (byte)'a' && b <= (byte)'f';
            bool isUpper = b >= (byte)'A' && b <= (byte)'F';
            if (!isDigit && !isLower && !isUpper)
                return false;
        }

        return true;
    }

    public static void LogLoginError(LoginError error)
    {
        if (!LogLogin)
            return;

        Debug.LogError($"[Login] RX LoginError {error}");
    }

    public static void LogLoginCharacterList(int characterCount)
    {
        if (!LogLogin)
            return;

        Debug.Log($"[Login] RX CharacterList count={characterCount}");
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
