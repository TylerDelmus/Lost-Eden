using System;

/// <summary>
/// Stock <c>_GfxControlSkyFlash_t</c> (typeCode 3006, 0xbbe; vftable <c>Gamecode 1016da74</c>, loader
/// <c>100eefd2</c>, init <c>100ef58b</c>, Process <c>100ef141</c>): a stack of <c>GfxVisualCone</c>s, one
/// inside the other, standing on the locator and reaching up, e.g. 43108 (the hit of 168665 Gift of Life)
/// and 43107 (in every slot of 305432 Purification by Fire).
///
/// Fields: 0 flags, 9 material, 10 cycles, 11 / 12 the two phases of a cycle (s), 13 cone count,
/// 14 segments, 15 / 16 u / v scale, 17 height, 18 / 19 bottom / top radius, 20 / 21 how much each
/// further cone adds to them, 22 / 23 how much they grow over a cycle, 24-29 colours c0-c5 (D3DCOLORs).
/// Flags: bits 0-2 the locator's; 0x100 the cone's uv swap; 0x200 additive (every record); 0x400 the
/// column comes down from the top in phase 1; 0x800 / 0x1000 follow the locator in phase 1 / 2;
/// 0x2000 cone i turned about y by i · 6.28 / count; 0x4000 the base on the ground; 0x8000 the height
/// is the locator's distance above the ground.
///
/// Per call: ready once cycles · (f11 + f12) &lt;= age. Otherwise with c = fmod(age, f11 + f12),
/// u = c / (f11 + f12): bottom radius f22 u + f18, top radius f23 u + f19, height f17. In phase 1
/// (c &lt; f11), k = c / f11, the bottom colour goes c0 → c2 and the top c1 → c3 (randy31 byte-wise colour
/// maths); with 0x400 the bottom radius is k · bottom + (1 - k) · top, the per-cone step likewise, and the
/// height k · f17. In phase 2, k = (c - f11) / f12, bottom c2 → c4, top c3 → c5. Cone i sits at the
/// position + (0, f17 - height, 0) (no lift with 0x8000), its radii the phase's plus i steps. The call
/// ends by clearing the ready flag, so neither a terminate nor the base duration ends it.
///
/// Built on a dynel (<c>100ef80f</c>), a negative field 18 is turned positive after the init and, when the
/// dynel is an <c>n3VisualDynel_t</c> with a CAT mesh, multiplied by its torso sphere's radius
/// (<c>VisualCATMesh_t::GetTorsoSphereRadi</c>, 1 for a mesh without one): 12510 (field 18 = -2).
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class SkyFlashSim
{
    public const int FlagUvSwap = 0x100;
    public const int FlagAdditive = 0x200;
    public const int FlagGrowDown = 0x400;
    public const int FlagFollow1 = 0x800;
    public const int FlagFollow2 = 0x1000;
    public const int FlagTurnCones = 0x2000;
    public const int FlagGround = 0x4000;
    public const int FlagHeightFromGround = 0x8000;

    /// <summary>1016c5b8: cone i is turned by i · this / count.</summary>
    public const double FullTurn = 6.28000020980835;

    readonly float _phase1, _phase2, _height;
    readonly float _top, _stepBottom, _stepTop, _growBottom, _growTop;
    float _bottom;
    readonly uint _cycles;
    readonly uint[] _colours = new uint[6];

    public int Flags { get; }
    public int Material { get; }
    public int ConeCount { get; }
    public int Segments { get; }
    public float UScale { get; }
    public float VScale { get; }
    public float FullHeight => _height;

    // This call's cone parameters.
    public bool Follow { get; private set; }
    public float Height { get; private set; }
    public float BottomRadius { get; private set; }
    public float TopRadius { get; private set; }
    public float StepBottom { get; private set; }
    public float StepTop { get; private set; }
    public uint BottomColour { get; private set; }
    public uint TopColour { get; private set; }

    /// <summary>One per field 13, each a <c>GfxVisualCone</c> (<c>100ef58b</c>).</summary>
    public ShockWaveSim.Cone[] Cones { get; }

    public SkyFlashSim(float[] fields)
    {
        Flags = Int(fields, 0);
        Material = Int(fields, 9);
        _cycles = (uint)Int(fields, 10);
        _phase1 = F(fields, 11);
        _phase2 = F(fields, 12);
        ConeCount = Math.Max(0, Int(fields, 13));
        Segments = Math.Max(0, Int(fields, 14));
        UScale = F(fields, 15);
        VScale = F(fields, 16);
        _height = F(fields, 17);
        _bottom = F(fields, 18);
        _top = F(fields, 19);
        _stepBottom = F(fields, 20);
        _stepTop = F(fields, 21);
        _growBottom = F(fields, 22);
        _growTop = F(fields, 23);
        for (int i = 0; i < 6; i++)
            _colours[i] = (uint)Int(fields, 24 + i);

        Cones = new ShockWaveSim.Cone[ConeCount];
        for (int i = 0; i < ConeCount; i++)
            Cones[i] = new ShockWaveSim.Cone();
    }

    public float BottomRadiusField => _bottom;

    /// <summary>
    /// <c>100ef894</c>, the dynel ctor after the init: a negative field 18 becomes -field 18 · the torso radius.
    /// </summary>
    public void ScaleBottomRadius(float torsoRadius)
    {
        if (!(_bottom < 0f))
            return;
        _bottom = -_bottom;
        _bottom = (float)((double)torsoRadius * _bottom);
    }

    /// <summary>Cone <paramref name="i"/>'s turn about y with 0x2000 (<c>100ef61c</c>), in radians.</summary>
    public float ConeTurn(int i) => ConeCount > 0 ? (float)((double)(uint)i * FullTurn / (uint)ConeCount) : 0f;

    /// <summary>One Process call (<c>100ef151</c>..<c>100ef2d7</c>): false once every cycle has run.</summary>
    public bool Step(float age)
    {
        float cycle = (float)((double)_phase2 + _phase1);
        if ((double)_cycles * cycle <= age)
            return false;

        float c = (float)((double)age % cycle);
        float u = (float)(c / (double)cycle);
        float bottom = (float)((double)_growBottom * u + _bottom);
        float top = (float)((double)_growTop * u + _top);
        float stepBottom = _stepBottom;
        float stepTop = _stepTop;
        float height = _height;

        if (c < _phase1)
        {
            float k = (float)(c / (double)_phase1);
            double w = 1.0 - k;
            Follow = (Flags & FlagFollow1) != 0;
            BottomColour = Mix(_colours[0], _colours[2], k, (float)w);
            TopColour = Mix(_colours[1], _colours[3], k, (float)w);
            if ((Flags & FlagGrowDown) != 0)
            {
                bottom = (float)((double)k * bottom + top * w);
                stepBottom = (float)((double)k * stepBottom + stepTop * w);
                height = (float)((double)k * height);
            }
        }
        else
        {
            float k = (float)((c - (double)_phase1) / _phase2);
            float w = (float)(1.0 - k);
            Follow = (Flags & FlagFollow2) != 0;
            BottomColour = Mix(_colours[2], _colours[4], k, w);
            TopColour = Mix(_colours[3], _colours[5], k, w);
        }

        Height = height;
        BottomRadius = bottom;
        TopRadius = top;
        StepBottom = stepBottom;
        StepTop = stepTop;
        return true;
    }

    /// <summary>
    /// 100ef3e7 (flag 0x8000): the height is the locator's height over the ground, kept only while it lies in
    /// 0..field 17 (else 0).
    /// </summary>
    public float HeightOverGround(float locatorY, float groundY)
    {
        float d = (float)((double)locatorY - groundY);
        return 0f <= d && !(_height < d) ? d : 0f;
    }

    /// <summary>The base's lift above the position: field 17 - height, none with 0x8000.</summary>
    public float Lift => (Flags & FlagHeightFromGround) != 0 ? 0f : (float)((double)_height - Height);

    /// <summary>randy31 <c>a · w + b · k</c>, each product and the sum byte-wise (<see cref="ShockWaveSim.Scale"/>).</summary>
    static uint Mix(uint a, uint b, float k, float w) => ShockWaveSim.Add(ShockWaveSim.Scale(a, w), ShockWaveSim.Scale(b, k));

    /// <summary>100ef3fa: with 0x4000 and 0x8000, a following call's height is <see cref="HeightOverGround"/>.</summary>
    public void SetHeight(float height) => Height = height;

    /// <summary>
    /// <c>100ef466</c>..<c>100ef4cc</c>: every cone at (x, y, z) (<see cref="Lift"/> already added), with this
    /// call's height and colours, its radii the phase's plus i steps; each rebuilt (its dirty flag +0x1e8).
    /// </summary>
    public void Place(float x, float y, float z)
    {
        float bottom = BottomRadius, top = TopRadius;
        bool swap = (Flags & FlagUvSwap) != 0;
        for (int i = 0; i < Cones.Length; i++)
        {
            ShockWaveSim.Cone cone = Cones[i];
            cone.X = x;
            cone.Y = y;
            cone.Z = z;
            cone.Height = Height;
            cone.BottomRadius = bottom;
            cone.TopRadius = top;
            cone.Bottom = BottomColour;
            cone.Top = TopColour;
            ShockWaveSim.BuildCone(cone, Segments, UScale, VScale, swap);
            bottom = (float)((double)StepBottom + bottom);
            top = (float)((double)StepTop + top);
        }
    }

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
