using System.Collections.Generic;
using UnityEngine;
using MovementFlags = N3Lite.MovementFlags;

/// <summary>
/// typeCode 3036 (0xbdc), stock <c>GfxControlToggle_t</c>: one child effect run while its conditions hold. The
/// rules are <see cref="ToggleSim"/>; this makes, runs and draws the child.
///
/// Stock makes the child through the handler's factories without registering it (<c>100d0656</c> and kin, not
/// CreateEffect2), so the Toggle alone processes it; the port does the same with
/// <see cref="IEffectSpawnFactory.CreateOwnedControl"/>. Process (<c>10112952</c>):
/// <list type="bullet">
/// <item>With a child that isn't terminating, or while the Toggle itself is terminating: with 0x8000 a dynel
/// that stops terminates the child; the child is processed; once it is ready it is deleted, and the Toggle
/// stays with 0x400 (not terminating) or is ready. With no child here it is ready.</item>
/// <item>Otherwise: a playfield that fails the test makes it ready; on a dynel (the dynel ctor, mode 2) the child
/// is made only as the dynel starts moving with 0x2000, else at once; a locator the Toggle doesn't have a dynel
/// under ends it.</item>
/// </list>
/// A child terminated by 0x8000 is therefore not processed again: a Meta child (every record's) stops, while the
/// children it registered drain in the handler. The next start replaces it.
///
/// Port: 0x1000 (no locator, <c>100ce912</c>) makes the child on the Toggle's own locator; no nano's record
/// sets it. The dynel's mover is the character's <c>N3CharVehicle</c> — speed its current speed, direction -1
/// while backing and +1 once going forward again (+1 at first). A visual-only dynel (GfxTest) stands. The
/// playfield id is <see cref="PlayfieldId"/>; the resource's flags aren't read, and no record sets the mask.
/// </summary>
public sealed class GfxControlToggle : GfxControl
{
    /// <summary>Port-only: the playfield the Toggle tests against; 0 until the game sets it.</summary>
    public static int PlayfieldId { get; set; }

    readonly ToggleSim _sim;
    readonly IEffectSpawnFactory _factory;
    GfxControl _child;
    int _direction = 1;

    public ToggleSim Sim => _sim;
    public GfxControl Child => _child;

    public GfxControlToggle(GfxTweakRecord record, EffectLocator locator, IEffectSpawnFactory factory)
        : base(record, locator)
    {
        _factory = factory;
        _sim = new ToggleSim(record?.Fields);
        base.SetDuration(_sim.Duration > 0f ? _sim.Duration : InfiniteDuration);
    }

    /// <summary>Stock slot 8 is the empty <c>10079931</c>.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary><c>101124c3</c>: clear 0x400, terminate the child, and wait for it.</summary>
    protected override void OnTerminateGracefully()
    {
        _sim.Terminate();
        _child?.TerminateGracefully();
    }

    protected override void OnReleased(bool immediate)
    {
        _child?.Release(true);
        _child = null;
    }

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body(0f);

    protected override void OnProcess(float dt) => Body(dt);

    void Body(float dt)
    {
        if ((_child != null && !_child.IsTerminating) || IsTerminating)
        {
            RunChild(dt);
            return;
        }

        if (!_sim.PlayfieldAllows(PlayfieldId, 0))
        {
            ReadyFlag = true;
            return;
        }

        // Mode 2 (the dynel ctor, 101128c7); 0x1000 is mode 4, no locator.
        bool onDynel = Locator != null && (_sim.Flags & ToggleSim.FlagNoLocator) == 0
            && (Locator.TryGetSourceDynel(out _) || Locator.TryGetVisual(out _));
        if (onDynel)
        {
            if (!TryMover(out int direction, out float speed))
            {
                ReadyFlag = true;
                return;
            }
            if (!_sim.StartOnDynel(direction, speed))
                return;
        }

        _child = _factory?.CreateOwnedControl(_sim.ChildId, Locator);
        if (_child != null && IgnoreWatchdog)
            Persist(_child);
    }

    void RunChild(float dt)
    {
        if ((_sim.Flags & ToggleSim.FlagStopWithDynel) != 0)
        {
            // 101124e2 leaves the Toggle ready when the dynel is gone.
            if (!TryMover(out int direction, out float speed))
            {
                ReadyFlag = true;
                return;
            }
            if (_sim.StopWithDynel(direction, speed))
                _child?.TerminateGracefully();
        }

        if (_child == null)
        {
            ReadyFlag = true;
            return;
        }

        if (_child.Process(dt))
            return;

        _child.Release(true);
        _child = null;
        if (!_sim.Rearm || IsTerminating)
            ReadyFlag = true;
    }

    /// <summary>The dynel's mover (<c>101124e2</c>, <c>n3Dynel_t</c> +0x50); false when the locator has no dynel.</summary>
    bool TryMover(out int direction, out float speed)
    {
        direction = _direction;
        speed = 0f;
        if (Locator == null)
            return false;
        if (Locator.TryGetSourceDynel(out Dynel dynel) && dynel != null)
        {
            if (dynel is Character character && character.Motor != null)
            {
                MovementFlags flags = character.Motor.MovementFlags;
                if ((flags & MovementFlags.Backward) != 0)
                    _direction = -1;
                else if ((flags & MovementFlags.Forward) != 0)
                    _direction = 1;
                speed = character.Motor.CurrentSpeed;
            }
            direction = _direction;
            return true;
        }
        return Locator.TryGetVisual(out VisualDynel visual) && visual != null;
    }

    static void Persist(GfxControl control)
    {
        control.IgnoreWatchdog = true;
        if (control is GfxControlMeta meta)
        {
            foreach (EffectHandle child in meta.Children)
            {
                if (child?.Control != null)
                    Persist(child.Control);
            }
        }
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectBillboards(dest, camera);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectStrips(dest, camera);
    }

    public override void CollectMeshes(List<EffectBillboardBatch.MeshDraw> dest, Camera camera)
    {
        if (IsAlive)
            _child?.CollectMeshes(dest, camera);
    }
}
