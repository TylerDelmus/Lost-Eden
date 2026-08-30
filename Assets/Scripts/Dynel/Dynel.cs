using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using UnityEngine;

public class Dynel : MonoBehaviour
{
    /// <summary>AO-style eye height used for line-of-sight checks.</summary>
    public const float LineOfSightEyeHeight = 1.6f;

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

    /// <summary>
    /// True when world geometry does not occlude the ray from this dynel's
    /// eye height (<see cref="LineOfSightEyeHeight"/>) to <paramref name="target"/>'s.
    /// </summary>
    public bool LineOfSight(Dynel target)
    {
        if (target == null)
            return false;

        if (target == this)
            return true;

        UnityEngine.Vector3 from = Position + UnityEngine.Vector3.up * LineOfSightEyeHeight;
        UnityEngine.Vector3 to = target.Position + UnityEngine.Vector3.up * LineOfSightEyeHeight;
        return HasLineOfSight(from, to);
    }

    /// <summary>
    /// True when world geometry does not occlude the ray from <paramref name="from"/>
    /// to this dynel's eye height.
    /// </summary>
    public bool LineOfSightFrom(UnityEngine.Vector3 from)
    {
        UnityEngine.Vector3 to = Position + UnityEngine.Vector3.up * LineOfSightEyeHeight;
        return HasLineOfSight(from, to);
    }

    public static bool HasLineOfSight(UnityEngine.Vector3 from, UnityEngine.Vector3 to)
    {
        if ((to - from).sqrMagnitude < 1e-8f)
            return true;

        return !Physics.Linecast(from, to, GameLayers.GroundMask, QueryTriggerInteraction.Ignore);
    }
}
