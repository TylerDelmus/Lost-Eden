using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

public class Dynel : MonoBehaviour
{
    public Identity Identity { get; private set; }
    public string Name { get; private set; }
    public bool IsNpc { get; private set; }
    public StatCollection Stats { get; } = new();

    public void Initialize(SimpleCharFullUpdateMessage msg)
    {
        Identity = msg.Identity;
        Apply(msg);
    }

    public virtual void Apply(SimpleCharFullUpdateMessage msg)
    {
        Name = msg.Name;
        IsNpc = msg.Flags.HasFlag(SimpleCharFullUpdateFlags.IsNpc);
        Stats.Apply(msg);
        transform.position = new UnityEngine.Vector3(msg.Position.X, msg.Position.Y, msg.Position.Z);
        transform.rotation = new UnityEngine.Quaternion(msg.Heading.X, msg.Heading.Y, msg.Heading.Z, msg.Heading.W);
        gameObject.name = string.IsNullOrEmpty(msg.Name)
            ? $"Dynel_{msg.Identity.Type}_{msg.Identity.Instance}"
            : msg.Name;
    }

    public void Apply(StatMessage msg) => Stats.Apply(msg);
}
