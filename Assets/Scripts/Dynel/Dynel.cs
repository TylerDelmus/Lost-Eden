using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

public class Dynel : MonoBehaviour
{
    public Identity Identity { get; private set; }
    public string Name { get; private set; }
    public bool IsNpc { get; private set; }
    public StatCollection Stats { get; } = new();
    public UnityEngine.Vector3 Position => transform.position;
    public bool ShowNameplate = true;

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
        transform.position = msg.Position.ToUnity();
        transform.rotation = msg.Heading.ToUnity();
        gameObject.name = string.IsNullOrEmpty(msg.Name)
            ? $"Dynel_{msg.Identity.Type}_{msg.Identity.Instance}"
            : msg.Name;
    }

    public void Apply(StatMessage msg) => Stats.Apply(msg);

    public void Apply(FullCharacterMessage msg) => Stats.Apply(msg);

    /// <summary>
    /// AO-style indicator world position for nameplates / hit floats.
    /// Fallback when no visual: local (0, 2, 0).
    /// </summary>
    public virtual bool TryGetIndicatorPosition(out UnityEngine.Vector3 worldPos)
    {
        worldPos = transform.position + UnityEngine.Vector3.up * 2f;
        return true;
    }
}
