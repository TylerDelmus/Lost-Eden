using System;

/// <summary>
/// The input and network vocabulary for movement.
///
/// <para>
/// <b>This is not how stock's mover stores movement.</b> A <c>CharVehicle_t</c> holds four float
/// axes (<c>Docs/Movement.md</c> §4); these flags are the shape our *input* layer produces and the
/// shape the *network* protocol sends, so they survive as a translation vocabulary at the edges.
/// <see cref="N3CharVehicle.SetInputs"/> converts them into the axes.
/// </para>
///
/// <para>
/// Extracted from the deleted <c>CharacterMotor</c> so the enum outlives it.
/// </para>
/// </summary>
[Flags]
public enum MovementFlags
{
    None = 0,
    Forward = 1 << 0,
    Backward = 1 << 1,
    TurnLeft = 1 << 2,
    TurnRight = 1 << 3,
    StrafeLeft = 1 << 4,
    StrafeRight = 1 << 5,
    Jump = 1 << 6,
    MouseTurn = 1 << 7,
}

/// <summary>
/// Per-direction speed caps. Stock keeps a single <c>MaxVel</c> on the vehicle and picks the curve
/// by movement state and direction (<c>1006f9eb</c>); this struct only survives because the
/// animation layer wants the three numbers at once.
/// </summary>
public readonly struct VelocityLimits
{
    public float Forward { get; }
    public float Backward { get; }
    public float Strafe { get; }

    public VelocityLimits(float forward, float backward, float strafe)
    {
        Forward = forward;
        Backward = backward;
        Strafe = strafe;
    }
}
