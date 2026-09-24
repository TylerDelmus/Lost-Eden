using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1012 (0x3f4), stock <c>_GfxControlSprite_t</c>: one textured quad on the locator. The cycle is
/// <see cref="SpriteSim"/>; this owns the place, the turn and the drawing.
///
/// <list type="bullet">
/// <item><c>GfxVisualSprite2</c>: a camera-facing quad of width × height (the view's inverse rotation on
/// ±w/2, ±h/2), texture × vertex colour, SrcAlpha/One, or SrcAlpha/InvSrcAlpha with sprite flag 4.</item>
/// <item><c>GfxVisualSprite3</c>: the same quad in its own frame's xy plane, turned by the locator (flag 8)
/// or about y towards the camera (flag 0x2000, <c>100f6e98</c>): x = normalise(-dz, 0, dx), d the
/// camera-to-sprite direction. Its colour is texture × vertex colour but its alpha is the texture's only.</item>
/// </list>
/// The atlas cell is column frame % columns, row frame / rows (<see cref="Sprite2Type0Visual.Cell"/>), as in
/// the other sprite visuals. The place is the locator's local-mode position at creation, and every call
/// with flag 0x10 (a lost locator then ends it).
/// </summary>
public sealed class GfxControlSprite : GfxControl
{
    readonly SpriteSim _sim;
    readonly Texture2D _atlas;
    readonly EffectAtlasFrames _frames;
    readonly int _cols, _rows;
    readonly bool _localMode;
    Vector3 _position;
    Quaternion _turn = Quaternion.identity;

    public SpriteSim Sim => _sim;
    public Vector3 Position => _position;

    public GfxControlSprite(GfxTweakRecord record, EffectLocator locator, Texture2D atlas, EffectAtlasFrames frames,
        int cols, int rows, int firstFrame, int lastFrame)
        : base(record, locator)
    {
        _atlas = atlas;
        _frames = frames;
        _cols = cols;
        _rows = rows;
        _sim = new SpriteSim(record?.Fields, firstFrame, lastFrame);
        _localMode = (_sim.LocatorFlags & 2) != 0;
        base.SetDuration(_sim.Duration < 0f ? InfiniteDuration : _sim.Duration);

        // 100f6483: a visual kind other than 0 or 3 makes nothing and is ready at once.
        if (_sim.Visual == SpriteSim.Kind.None)
            ReadyFlag = true;
        Frame(out _position, out _turn);
    }

    public override void SetStartColor(float a, float r, float g, float b) => _sim.SetStart(a, r, g, b);

    public override void SetStopColor(float a, float r, float g, float b) => _sim.SetStop(a, r, g, b);

    /// <summary>Slot 13 (<c>100f6167</c>): start = the colour, stop = the colour at alpha 0.</summary>
    public override void SetColor(uint argb) => SetColorAsStartAndFadeOut(argb);

    // Stock's first Process arms the control and still runs the body at age 0.
    protected override void OnArmed() => Body();

    protected override void OnProcess(float dt) => Body();

    void Body()
    {
        if ((_sim.Flags & SpriteSim.FlagFollow) != 0)
        {
            if (!Frame(out Vector3 position, out Quaternion turn))
            {
                ReadyFlag = true;
                return;
            }
            _position = position;
            _turn = turn;
        }

        if (!_sim.Step(Age))
            ReadyFlag = true;
    }

    /// <summary>The locator's local-mode position and turn (<c>10106306</c> / <c>101062d5</c>).</summary>
    bool Frame(out Vector3 position, out Quaternion turn)
    {
        Matrix4x4 world = Matrix4x4.identity;
        bool resolved = Locator != null && Locator.TryResolve(out world);
        position = Vector3.zero;
        turn = Quaternion.identity;
        if (_localMode && resolved)
        {
            position = world.GetColumn(3);
            Vector3 x = ((Vector3)world.GetColumn(0)).normalized;
            Vector3 y = ((Vector3)world.GetColumn(1)).normalized;
            Vector3 z = ((Vector3)world.GetColumn(2)).normalized;
            if (x != Vector3.zero && y != Vector3.zero && z != Vector3.zero)
                turn = new Matrix4x4(x, y, z, new Vector4(0f, 0f, 0f, 1f)).rotation;
        }
        return resolved;
    }

    public override void CollectBillboards(List<EffectBillboardBatch.Quad> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null || _atlas == null)
            return;

        Texture2D texture = _frames != null && (_cols > 1 || _rows > 1)
            ? _frames.GetFrame(_atlas, _cols, _rows, Sprite2Type0Visual.Cell(_sim.Frame, _cols, _rows))
            : _atlas;
        if (texture == null)
            return;

        Vector3 axisX, axisY;
        bool sprite3 = _sim.Visual == SpriteSim.Kind.Sprite3;
        if (sprite3)
        {
            Quaternion rotation = Quaternion.identity;
            if ((_sim.Flags & SpriteSim.FlagTurn) != 0)
                rotation = _turn;
            if ((_sim.Flags & SpriteSim.FlagFaceCamera) != 0)
                rotation = FaceCamera(_position - camera.transform.position);
            axisX = rotation * new Vector3(_sim.Width, 0f, 0f);
            axisY = rotation * new Vector3(0f, _sim.Height, 0f);
        }
        else
        {
            axisX = camera.transform.right * _sim.Width;
            axisY = camera.transform.up * _sim.Height;
        }

        uint argb = _sim.Argb;
        dest.Add(new EffectBillboardBatch.Quad
        {
            Matrix = Matrix4x4.TRS(_position, Quaternion.identity, Vector3.one),
            Color = new Color(
                ((argb >> 16) & 0xff) / 255f,
                ((argb >> 8) & 0xff) / 255f,
                (argb & 0xff) / 255f,
                // GfxVisualSprite3 takes its alpha from the texture only.
                sprite3 ? 1f : (argb >> 24) / 255f),
            Texture = texture,
            Additive = _sim.Additive,
            UseAxes = true,
            AxisX = axisX,
            AxisY = axisY,
        });
    }

    /// <summary>
    /// 100f6eb6: x = normalise(-dz, 0, dx) (1, 0, 0 when the horizontal distance² is 0.001 or less), y up,
    /// z = x × y, d the camera-to-sprite direction.
    /// </summary>
    static Quaternion FaceCamera(Vector3 d)
    {
        var x = new Vector3(-d.z, 0f, d.x);
        if (d.x * d.x + d.z * d.z > 0.001f)
            x.Normalize();
        else
            x = Vector3.right;
        var z = new Vector3(-x.z, 0f, x.x);
        return new Matrix4x4(x, Vector3.up, z, new Vector4(0f, 0f, 0f, 1f)).rotation;
    }
}
