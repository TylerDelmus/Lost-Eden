using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1003 (0x3eb), stock <c>_GfxControlCord_t</c> (vftable <c>Gamecode 1016c564</c>, loader
/// <c>100d60d5</c>, visual build <c>100d627a</c>, Process <c>100d5f5a</c>, slot 4 <c>100d69d8</c>): a
/// ribbon through the points it is given, drawn by DisplaySystem's <c>GfxVisualCord4</c>
/// (material from field 9, additive, v fixed at 0.25) from a 32-link <c>Cord4CircularLinkList</c>.
///
/// Fields: 0 flags, 8 duration, 9 material, 12 link width, 16-19 start colour (A,R,G,B), 20-23 stop
/// colour (stored by slot 12, never read), 35 link life, 36 per-body offset table
/// (<see cref="EffectBodyTable"/>). The rest (rate, cone, magnitudes) go unread.
///
/// Only slot 4 makes links, and only when the locator is in local mode (field 0 bit 1, locator +8):
/// while the duration is negative or age &lt; duration - life, <c>UpdatePosition(p)</c> appends a link
/// whose point and velocity are both p, with the width, the start colour packed as ARGB and the life.
/// Without bit 1 slot 4 is the locator's own <c>101064f7</c> and the Cord never draws (the hand Cords
/// 8000/8001 of cast 46116). BuffPlaceHolder feeds its Cord a point on a circle every 0.45 rad step:
/// nano 25988's halo.
///
/// Process, every call including the arming one: the visual takes the locator's position and turn
/// (local mode), so link points are in the locator's frame. Each link, newest first, moves by its
/// velocity times the frame delta clamped to [0.01, 0.025], loses the delta from its life, and gets
/// alpha <c>_ftol(life / field 35 * 254)</c> in its top byte. The render purges links whose life is 0
/// or less from the oldest end after drawing (<c>1000e9f9</c>).
///
/// Slot 6 sets the duration to age + life, so the ribbon runs out.
///
/// Stock calls Process once per frame, so its clamp made the links drift faster at high frame rates
/// (1.44× at 144 fps, 0.75× at 30). The port steps the links on the fixed clock
/// (<see cref="EffectFrameRate.StockProcessHz"/>, Docs §3.7): each step is one stock call of 1/30 s, whose
/// clamped delta is 0.025. The visual still follows the locator every frame.
/// </summary>
public sealed class GfxControlCord : GfxControl
{
    /// <summary>GfxVisualCord4 ctor <c>1000ec62</c>: <c>Cord4CircularLinkList(0x20)</c>.</summary>
    public const int LinkCapacity = 32;

    // 100d600b / 100d6025: the per-call delta that moves the links.
    const float MinStep = 0.009999999776482582f;
    const double MaxStepCompare = 0.02500000037252903;
    const float MaxStep = 0.02500000037252903f;

    // 100d6084.
    const double AlphaScale = 254.0;

    /// <summary>Replayed calls allowed in one frame; a hitch loses time rather than fast-forwarding.</summary>
    const int MaxStepsPerFrame = 8;

    float _carry;

    struct Link
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        public float Size;
        public uint Argb;
        public float Life;
    }

    readonly Texture2D _texture;
    readonly int _flags;
    readonly float _width;
    readonly float _life;
    readonly float[] _start = new float[4];
    readonly float[] _stop = new float[4];

    // Cord4CircularLinkList: +8 newest index (-1 empty), +0xc oldest index.
    readonly Link[] _links = new Link[LinkCapacity];
    int _newest = -1;
    int _oldest;

    Matrix4x4 _visual = Matrix4x4.identity;

    // Port-only drawing buffers.
    readonly float[] _local = new float[LinkCapacity * 3];
    readonly float[] _camera = new float[LinkCapacity * 3];
    readonly float[] _sizes = new float[LinkCapacity];
    readonly float[] _lives = new float[LinkCapacity];
    readonly uint[] _argbs = new uint[LinkCapacity];
    readonly float[] _positions = new float[2 * (LinkCapacity - 1) * 3];
    readonly float[] _uvs = new float[2 * (LinkCapacity - 1) * 2];
    readonly EffectBillboardBatch.Strip _strip = new EffectBillboardBatch.Strip
    {
        Positions = new Vector3[2 * (LinkCapacity - 1)],
        Uvs = new Vector2[2 * (LinkCapacity - 1)],
        Colors = new Color32[2 * (LinkCapacity - 1)],
        Color = Color.white,
        Additive = true,
    };

    public GfxControlCord(GfxTweakRecord record, EffectLocator locator, Texture2D texture)
        : base(record, locator)
    {
        _texture = texture;
        _flags = record != null ? record.FieldInt(0, 0) : 0;
        _width = record != null ? record.Field(12, 0f) : 0f;
        _life = record != null ? record.Field(35, 0f) : 0f;
        for (int c = 0; c < 4; c++)
        {
            _start[c] = record != null ? record.Field(16 + c, 0f) : 0f;
            _stop[c] = record != null ? record.Field(20 + c, 0f) : 0f;
        }
        base.SetDuration(record != null ? record.Field(8, InfiniteDuration) : InfiniteDuration);
    }

    /// <summary>Field 0 bit 1: the locator is in local mode.</summary>
    public bool LocalMode => (_flags & 2) != 0;

    public int LinkCount
    {
        get
        {
            if (_newest < 0)
                return 0;
            return (_newest - _oldest + LinkCapacity) % LinkCapacity + 1;
        }
    }

    /// <summary>Link <paramref name="k"/>, newest first, in world space. For probes.</summary>
    public bool TryGetLink(int k, out Vector3 world, out float life, out uint argb)
    {
        world = default;
        life = 0f;
        argb = 0u;
        if (k < 0 || k >= LinkCount)
            return false;
        Link link = _links[(_newest - k + LinkCapacity) % LinkCapacity];
        world = _visual.MultiplyPoint3x4(new Vector3(link.X, link.Y, link.Z));
        life = link.Life;
        argb = link.Argb;
        return true;
    }

    /// <summary>Stock slot 11 (<c>100d63e2</c>): the colour later links take.</summary>
    public override void SetStartColor(float a, float r, float g, float b)
    {
        _start[0] = a;
        _start[1] = r;
        _start[2] = g;
        _start[3] = b;
    }

    /// <summary>Stock slot 12 (<c>100d6401</c>): stored, not read by the Cord.</summary>
    public override void SetStopColor(float a, float r, float g, float b)
    {
        _stop[0] = a;
        _stop[1] = r;
        _stop[2] = g;
        _stop[3] = b;
    }

    /// <summary>Stock slot 4 (<c>100d69d8</c>).</summary>
    public override void UpdatePosition(Vector3 position)
    {
        if (!LocalMode)
        {
            base.UpdatePosition(position);
            return;
        }

        if (Duration < 0f || !(Duration - _life <= Age))
            AddLink(position);
    }

    /// <summary><c>100d6323</c> with the same point for both arguments.</summary>
    void AddLink(Vector3 p)
    {
        // Cord4CircularLinkList::GetNew (1000e9b7): the next slot, pushing the oldest out when full.
        if (_newest == -1)
        {
            _newest = 0;
            _oldest = 0;
        }
        else
        {
            _newest = _newest == LinkCapacity - 1 ? 0 : _newest + 1;
            if (_newest == _oldest)
                _oldest = _oldest == LinkCapacity - 1 ? 0 : _oldest + 1;
        }

        _links[_newest] = new Link
        {
            X = p.x, Y = p.y, Z = p.z,
            VX = p.x, VY = p.y, VZ = p.z,
            Size = _width,
            Argb = PackArgb(_start),
            Life = _life,
        };
    }

    /// <summary>100d6356..100d63cc: each channel is <c>fistp(c * 255 - 0.49999)</c>, shifted in unmasked.</summary>
    public static uint PackArgb(float[] argb)
    {
        int a = Channel(argb[0]), r = Channel(argb[1]), g = Channel(argb[2]), b = Channel(argb[3]);
        return (uint)((((a << 8) | r) << 8 | g) << 8 | b);
    }

    static int Channel(float value)
    {
        float scaled = (float)(value * 255.0);
        return (int)Math.Round(scaled - 0.49999, MidpointRounding.ToEven);
    }

    /// <summary>Stock slot 6 (<c>100d60c8</c>).</summary>
    protected override void OnTerminateGracefully() => SetDuration(Age + _life);

    protected override void OnArmed()
    {
        if (Locator != null && Locator.TryResolve(out Matrix4x4 m))
            _visual = Orthonormal(m);
        StepLinks(0f);
    }

    protected override void OnProcess(float dt)
    {
        _visual = Orthonormal(WorldMatrix);
        float step = EffectFrameRate.StockProcessSeconds;
        int steps = EffectFrameRate.TakeFixedSteps(ref _carry, dt, step, MaxStepsPerFrame);
        for (int s = 0; s < steps; s++)
            StepLinks(step);
    }

    void StepLinks(float dt)
    {
        if (_newest < 0)
            return;

        float step = dt;
        if (!(MinStep <= step))
            step = MinStep;
        if (!(step <= MaxStepCompare))
            step = MaxStep;

        for (int i = _newest; ; )
        {
            ref Link link = ref _links[i];
            link.X += link.VX * step;
            link.Y += link.VY * step;
            link.Z += link.VZ * step;
            link.Life -= dt;
            float fraction = link.Life / _life;
            int alpha = (int)(fraction * AlphaScale);
            link.Argb = (link.Argb & 0xffffffu) | ((uint)alpha << 24);

            if (i == _oldest)
                break;
            i = i == 0 ? LinkCapacity - 1 : i - 1;
        }
    }

    /// <summary>1000e9f9, after the render: links out of life leave from the oldest end.</summary>
    void PurgeExpired()
    {
        if (_newest < 0)
            return;
        while (!(0f < _links[_oldest].Life))
        {
            if (_oldest == _newest)
            {
                _newest = -1;
                return;
            }
            _oldest = _oldest == LinkCapacity - 1 ? 0 : _oldest + 1;
        }
    }

    /// <summary>The visual keeps the locator's turn and position only (quaternion + position).</summary>
    static Matrix4x4 Orthonormal(Matrix4x4 m)
    {
        Vector3 x = ((Vector3)m.GetColumn(0)).normalized;
        Vector3 y = ((Vector3)m.GetColumn(1)).normalized;
        Vector3 z = ((Vector3)m.GetColumn(2)).normalized;
        var o = Matrix4x4.identity;
        o.SetColumn(0, x);
        o.SetColumn(1, y);
        o.SetColumn(2, z);
        o.SetColumn(3, m.GetColumn(3));
        return o;
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _texture == null)
            return;

        int count = LinkCount;
        if (count >= 3)
            BuildSegments(dest, camera, count);

        PurgeExpired();
    }

    void BuildSegments(List<EffectBillboardBatch.Strip> dest, Camera camera, int count)
    {
        Matrix4x4 toCamera = camera.worldToCameraMatrix * _visual;
        Matrix4x4 fromWorld = _visual.inverse;
        Vector3 right = fromWorld.MultiplyVector(camera.transform.right);
        Vector3 up = fromWorld.MultiplyVector(camera.transform.up);

        // Newest first, as Cord4CircularLinkList::GetFirst / GetNext walk it.
        for (int k = 0, i = _newest; k < count; k++)
        {
            Link link = _links[i];
            _local[k * 3] = link.X;
            _local[k * 3 + 1] = link.Y;
            _local[k * 3 + 2] = link.Z;
            // Unity camera space looks down -z; stock's D3D view space looks down +z.
            Vector3 c = toCamera.MultiplyPoint3x4(new Vector3(link.X, link.Y, link.Z));
            _camera[k * 3] = c.x;
            _camera[k * 3 + 1] = c.y;
            _camera[k * 3 + 2] = -c.z;
            _sizes[k] = link.Size;
            _lives[k] = link.Life;
            _argbs[k] = link.Argb;
            i = i == 0 ? LinkCapacity - 1 : i - 1;
        }

        int vertices = Cord4Strip.Build(
            count, _local, _camera,
            right.x, right.y, right.z, up.x, up.y, up.z,
            _sizes, _lives, 1f, lifeV: false,
            _positions, _uvs);

        // Stock colours each vertex with its link's ARGB (vertices 2k and 2k + 1 are link k's).
        EffectBillboardBatch.Strip strip = _strip;
        for (int v = 0; v < vertices; v++)
        {
            strip.Positions[v] = _visual.MultiplyPoint3x4(
                new Vector3(_positions[v * 3], _positions[v * 3 + 1], _positions[v * 3 + 2]));
            // D3D tv runs down the image; Unity's v runs up.
            strip.Uvs[v] = new Vector2(_uvs[v * 2], 1f - _uvs[v * 2 + 1]);
            strip.Colors[v] = EffectBillboardBatch.ToColor32(_argbs[v / 2]);
        }
        strip.Count = vertices;
        strip.Texture = _texture;
        dest.Add(strip);
    }
}
