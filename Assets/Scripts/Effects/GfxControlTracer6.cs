using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// typeCode 1026 (0x402), stock <c>_GfxControlTracer6_t</c> — the last tracer the port drew with a
/// stand-in. The mechanics are <see cref="Tracer6Sim"/>; this binds them to the flight line and draws
/// the two halves stock draws.
///
/// The three <c>GfxVisualCord4</c> ribbons (<c>10100de0</c>) are built alike and given the same link
/// positions every call (<c>101012d4</c> walks them with one body), so they land on top of each other
/// and, being additive, simply come out three times as bright. The port emits the three strips rather
/// than one, so the brightness matches.
///
/// The sprite trail is one <c>GfxVisualSprite3Type0</c> (<c>10100f6c</c>), whose quads sit in the
/// visual's own frame rather than facing the camera: each is its position ± width/2 along the frame's x
/// and ± height/2 along its z (<c>10028cd0</c>), which puts them across the line of flight, since the
/// locator's y is the direction. Its atlas is forced to 8 × 8 by the build (<c>10100fae</c>) whatever
/// the material says, and the cell is the sprite's own frame counter.
/// </summary>
public sealed class GfxControlTracer6 : GfxControl
{
    readonly Tracer6Sim _sim;
    readonly Texture2D _ribbonTexture;
    readonly Texture2D _spriteTexture;
    readonly EffectBillboardBatch.Strip[] _ribbons = new EffectBillboardBatch.Strip[Tracer6Sim.RibbonCount];
    readonly EffectBillboardBatch.Strip _sprites;
    readonly float[] _camera = new float[Tracer6Sim.LinksPerRibbon * 3];
    readonly float[] _sizes = new float[Tracer6Sim.LinksPerRibbon];
    readonly float[] _positions = new float[2 * (Tracer6Sim.LinksPerRibbon - 1) * 3];
    readonly float[] _uvs = new float[2 * (Tracer6Sim.LinksPerRibbon - 1) * 2];
    Matrix4x4 _visual = Matrix4x4.identity;

    public Tracer6Sim Sim => _sim;

    public GfxControlTracer6(
        GfxTweakRecord record,
        EffectLocator locator,
        Vector3 start,
        Vector3 end,
        Texture2D ribbonTexture,
        Texture2D spriteTexture)
        : base(record, locator)
    {
        _ribbonTexture = ribbonTexture;
        _spriteTexture = spriteTexture;
        _sim = new Tracer6Sim(record?.Fields, start.x, start.y, start.z, end.x, end.y, end.z);

        // +0x10 is the record's duration (-1 on every record); the sim moves it when the flight lands.
        base.SetDuration(_sim.Duration);

        for (int i = 0; i < _sizes.Length; i++)
            _sizes[i] = _sim.RibbonWidth;
        for (int i = 0; i < _ribbons.Length; i++)
        {
            _ribbons[i] = new EffectBillboardBatch.Strip
            {
                Positions = new Vector3[2 * (Tracer6Sim.LinksPerRibbon - 1)],
                Uvs = new Vector2[2 * (Tracer6Sim.LinksPerRibbon - 1)],
                Texture = ribbonTexture,
                Additive = true,
            };
        }

        int quadVerts = 4 * Tracer6Sim.MaxSprites;
        _sprites = new EffectBillboardBatch.Strip
        {
            Positions = new Vector3[quadVerts],
            Uvs = new Vector2[quadVerts],
            Colors = new Color32[quadVerts],
            Color = Color.white,
            Texture = spriteTexture,
            Additive = true,
            Quads = true,
        };

        if (_sim.Degenerate)
        {
            ReadyFlag = true;
            return;
        }

        // The visual turns and sits with the locator in local mode (101062d5 / 10106306).
        if (_sim.Local)
        {
            float[] b = _sim.Basis;
            _visual.SetColumn(0, new Vector4(b[0], b[1], b[2], 0f));
            _visual.SetColumn(1, new Vector4(b[3], b[4], b[5], 0f));
            _visual.SetColumn(2, new Vector4(b[6], b[7], b[8], 0f));
            _visual.SetColumn(3, new Vector4(_sim.OriginX, _sim.OriginY, _sim.OriginZ, 1f));
        }
    }

    /// <summary>Stock slot 8 (<c>101014d8</c>) writes field 10's speed, not the duration.</summary>
    public override void SetDuration(float seconds) { }

    /// <summary>Stock slot 6 (<c>10100c8e</c>) readies the control at once.</summary>
    protected override void OnTerminateGracefully() => ReadyFlag = true;

    protected override void OnArmed() => _sim.Step(0f, 0f);

    protected override void OnProcess(float dt)
    {
        // _GfxControl_t::Process (100d2a86) runs before the body and sets +0x14, which 10101883 then
        // tests, so once the duration the sim gave itself has passed the body never runs again. Without
        // this the update would keep meeting "age > duration" and buy itself another second forever.
        if (Duration >= 0f && Duration < Age)
        {
            ReadyFlag = true;
            return;
        }

        _sim.Step(Age, dt);
        // The sim owns +0x10, so the base timer has to follow it to end the control at the right time.
        base.SetDuration(_sim.Duration);
    }

    public override void CollectStrips(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (!IsAlive || dest == null || camera == null)
            return;

        CollectRibbons(dest, camera);
        CollectSprites(dest);
    }

    void CollectRibbons(List<EffectBillboardBatch.Strip> dest, Camera camera)
    {
        if (_ribbonTexture == null || !_sim.RibbonsVisible)
            return;

        Matrix4x4 toCamera = camera.worldToCameraMatrix * _visual;
        Matrix4x4 fromWorld = _visual.inverse;
        Vector3 right = fromWorld.MultiplyVector(camera.transform.right);
        Vector3 up = fromWorld.MultiplyVector(camera.transform.up);

        float[] links = _sim.LinkPositions;
        for (int i = 0; i < Tracer6Sim.LinksPerRibbon; i++)
        {
            // Unity camera space looks down -z; stock's D3D view space looks down +z.
            Vector3 c = toCamera.MultiplyPoint3x4(new Vector3(links[i * 3], links[i * 3 + 1], links[i * 3 + 2]));
            _camera[i * 3] = c.x;
            _camera[i * 3 + 1] = c.y;
            _camera[i * 3 + 2] = -c.z;
        }

        int count = Cord4Strip.Build(
            Tracer6Sim.LinksPerRibbon, links, _camera,
            right.x, right.y, right.z, up.x, up.y, up.z,
            _sizes, Tracer6Sim.LinkLives, 1f, lifeV: true,
            _positions, _uvs);
        if (count < 4)
            return;

        uint argb = _sim.RibbonArgb;
        var colour = new Color(
            ((argb >> 16) & 0xff) / 255f,
            ((argb >> 8) & 0xff) / 255f,
            (argb & 0xff) / 255f,
            ((argb >> 24) & 0xff) / 255f);

        for (int r = 0; r < _ribbons.Length; r++)
        {
            EffectBillboardBatch.Strip strip = _ribbons[r];
            for (int v = 0; v < count; v++)
            {
                strip.Positions[v] = _visual.MultiplyPoint3x4(
                    new Vector3(_positions[v * 3], _positions[v * 3 + 1], _positions[v * 3 + 2]));
                // D3D tv runs down the image; Unity's v runs up.
                strip.Uvs[v] = new Vector2(_uvs[v * 2], 1f - _uvs[v * 2 + 1]);
            }
            strip.Count = count;
            strip.Color = colour;
            dest.Add(strip);
        }
    }

    void CollectSprites(List<EffectBillboardBatch.Strip> dest)
    {
        if (_spriteTexture == null)
            return;

        uint argb = _sim.SpriteArgb;
        byte cr = (byte)(argb >> 16), cg = (byte)(argb >> 8), cb = (byte)argb;
        const float cell = 1f / Tracer6Sim.AtlasCols;
        const float row = 1f / Tracer6Sim.AtlasRows;

        Tracer6Sim.Sprite[] pool = _sim.Sprites;
        int v = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            Tracer6Sim.Sprite s = pool[i];
            if (!s.Alive || s.Life <= 0f)
                continue;

            // 10101168: the alpha is the life, the rest of the colour is field 13-16's.
            var c = new Color32(cr, cg, cb, (byte)Mathf.Clamp((int)(s.Life * 255f), 0, 255));
            Tracer6Sim.AtlasCell(s.Frame, out int col, out int r);
            float u0 = col * cell, v0 = r * row;

            float hw = s.Width * 0.5f, hh = s.Height * 0.5f;
            // The quad lies across the flight line: x and z of the visual's own frame.
            Vector3 p = new Vector3(s.X, s.Y, s.Z);
            Set(v++, p + new Vector3(-hw, 0f, -hh), u0, v0 + row, c);
            Set(v++, p + new Vector3(hw, 0f, -hh), u0 + cell, v0 + row, c);
            Set(v++, p + new Vector3(-hw, 0f, hh), u0, v0, c);
            Set(v++, p + new Vector3(hw, 0f, hh), u0 + cell, v0, c);
        }

        if (v == 0)
            return;
        _sprites.Count = v;
        dest.Add(_sprites);
    }

    void Set(int index, Vector3 local, float u, float vCoord, Color32 colour)
    {
        _sprites.Positions[index] = _visual.MultiplyPoint3x4(local);
        // D3D tv runs down the image; Unity's v runs up.
        _sprites.Uvs[index] = new Vector2(u, 1f - vCoord);
        _sprites.Colors[index] = colour;
    }
}
