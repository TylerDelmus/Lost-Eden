using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 15: sparks thrown off the host's skin along its normals
/// (e.g. 43047, the hit of 28604 Electrifying Containment). One <see cref="Step"/> is one call of the
/// stock Process, <c>FUN_100f8491</c>, whose case 15 runs <c>Gamecode 100f9f5f</c>..<c>100fa20a</c>. It takes no
/// delta: the spawns, the drag and the step are per call.
///
/// Fields (loader <c>FUN_100f72a9</c>): 18-21 / 22-25 colour ramp (+0x1698), 26 duration, 28 size
/// (+0x16d4), 29 speed (+0x16d8), 30 life in ms (int bits; +0x16cc = field 30 / 1000). Field 31 is loaded
/// but case 15 doesn't read it.
///
/// Lazy init (<c>100f811f</c>): the locator's dynel, cast to n3VisualDynel_t, gives its CAT mesh; with
/// no CATRender (+0x90) the init clears +0x16f0 and Process returns before the age moves, to try again
/// next call. Otherwise it keeps the render's +0x78 (+0x16ec, the scale), registers the vertex callback
/// <c>100f740b</c> on the render, sets every timer to -100, fills the 30-entry sample tables with position
/// (0, 0, 0) (+0x48) and normal (0, 1, 0) (+0x1b0), and sets the next entry (+0x16e8) to 0.
///
/// The callback (<see cref="ReadGroup"/>) runs for each CAT group as the mesh is drawn and copies about
/// 30 of its skinned vertices, position and normal, into the tables.
///
/// Per call, the dynel's position P (<c>Vehicle_t::GetGlobalPos</c> less the locator's local-mode
/// position, <c>10106306</c>; every record is world mode, where that is zero) and rotation Q
/// (<c>n3Dynel_t::GetGlobalRot</c>), then for each of the 128 slots:
/// <list type="bullet">
/// <item>Alive (age &lt; timer): <c>v *= 0.96</c>, <c>p += v * 0.1</c>; the record takes p,
/// t = (life + age - timer) / life, width = height = (1.1 - t) * field 28, colour the ramp at t, frame 0.</item>
/// <item>Due, while fewer than 10 have spawned this call and it isn't terminating: entry k = +0x16e8,
/// p = P + Q(table position k * scale), v = Q(table normal k * field 29), k = (k + 1) mod 30,
/// timer = life + age. The record is left as it was. Otherwise the record is hidden.</item>
/// </list>
/// The spawn tables are only as fresh as the last draw: the first call spawns from the lazy init's
/// defaults, so its 10 sparks leave P straight up.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsBodySparks : IStarsStockCase
{
    public const int StarType = 15;
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary>Slots a single Process may open: <c>100f9f74 MOV [EBP-0x1c], 0xa</c>.</summary>
    public const int SpawnsPerStep = 10;

    /// <summary>Sample table length: <c>100f81aa PUSH 0x1e</c>, <c>100fa1c0 CMP [EBX+0x16e8], 0x1e</c>.</summary>
    public const int TableSize = 30;

    /// <summary>The callback's stride is the mesh's vertex count over this: <c>100f7429 PUSH 0x1f</c>.</summary>
    public const int StrideDivisor = 31;

    /// <summary><c>100fa023 FLD [0x1016c33c]</c>.</summary>
    public const float Drag = 0.9599999785423279f;

    /// <summary><c>100fa040 FLD [0x10161850]</c>.</summary>
    public const float Step01 = 0.10000000149011612f;

    /// <summary><c>100fa0ad FSUBR [0x1016de38]</c>, a double holding float 1.1.</summary>
    const double SizeTop = 1.100000023841858;

    const float UnspawnedTimer = -100f;

    /// <summary>A skinned vertex as the callback sees it: position and normal in the mesh's own space.</summary>
    public struct Vertex
    {
        public float X, Y, Z;
        public float NX, NY, NZ;
    }

    /// <summary>
    /// The dynel's frame for a call: position P and the rotation Q as its three axes, so that
    /// Q(v) = AxisX * v.x + AxisY * v.y + AxisZ * v.z.
    /// </summary>
    public struct Frame
    {
        public float Px, Py, Pz;
        public float Xx, Xy, Xz;
        public float Yx, Yy, Yz;
        public float Zx, Zy, Zz;

        public static Frame At(float x, float y, float z) => new Frame
        {
            Px = x, Py = y, Pz = z,
            Xx = 1f, Yy = 1f, Zz = 1f,
        };
    }

    readonly float _life;
    readonly float _size;
    readonly float _speed;
    readonly float _scale;
    readonly float[] _start;
    readonly float[] _end;
    readonly Func<int> _rand;

    // +0x48 sample positions, +0x1b0 sample normals, +0x16e8 next entry.
    readonly float[] _tableP = new float[TableSize * 3];
    readonly float[] _tableN = new float[TableSize * 3];
    int _next;

    // +0xc48 positions, +0x648 velocities, +0x1448 per-slot expiry timers.
    readonly float[] _px = new float[SlotCount], _py = new float[SlotCount], _pz = new float[SlotCount];
    readonly float[] _vx = new float[SlotCount], _vy = new float[SlotCount], _vz = new float[SlotCount];
    readonly float[] _timer = new float[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];

    // Port-only, for drawing between steps (see StarsCase3.Blend).
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];
    readonly int[] _slotSerial = new int[SlotCount];
    int _serial;

    /// <summary>Stock +0x1648.</summary>
    public bool Terminating { get; set; }

    /// <summary>Slots spawned or alive on the last step. Stock's <c>[EBP-0x24]</c>.</summary>
    public int LastCount { get; private set; }

    /// <summary>Terminating and the last step saw nothing (<c>100f90c1</c>).</summary>
    public bool Drained => Terminating && LastCount == 0;

    public float Life => _life;
    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <summary>The next sample entry a spawn takes (+0x16e8).</summary>
    public int NextEntry => _next;

    /// <param name="size">Field 28.</param>
    /// <param name="speed">Field 29.</param>
    /// <param name="scale">The CATRender's +0x78, which multiplies the sample positions.</param>
    /// <param name="startArgb">Fields 18-21, A,R,G,B.</param>
    /// <param name="endArgb">Fields 22-25, A,R,G,B.</param>
    /// <param name="rand">Stock <c>rand()</c>: 0..0x7fff.</param>
    public StarsBodySparks(
        float lifeSeconds,
        float size,
        float speed,
        float scale,
        float[] startArgb,
        float[] endArgb,
        Func<int> rand)
    {
        _life = lifeSeconds;
        _size = size;
        _speed = speed;
        _scale = scale;
        _start = startArgb;
        _end = endArgb;
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));

        // The lazy init's part for this case (100f817c..100f81c9).
        for (int i = 0; i < SlotCount; i++)
        {
            _timer[i] = UnspawnedTimer;
            _sprites[i].Argb = 0xffffffffu;
        }
        for (int k = 0; k < TableSize; k++)
            _tableN[k * 3 + 1] = 1f;
    }

    /// <summary>
    /// The vertex callback <c>100f740b</c> for one CAT group of <paramref name="count"/> vertices that
    /// start at <paramref name="baseIndex"/> in the mesh's <paramref name="totalCount"/>
    /// (<c>CATMesh_t::GetTotalVertexCount</c>). With stride n = total / 31 (at least 1) it walks the group
    /// from vertex rand() % 30 in steps of n, and copies each vertex into entry (base + index) / n while
    /// that is below 30.
    /// </summary>
    public void ReadGroup(int count, int baseIndex, int totalCount, Func<int, Vertex> vertexAt)
    {
        int stride = totalCount / StrideDivisor;
        if (stride == 0)
            stride = 1;

        for (int j = _rand() % TableSize; (uint)j < (uint)count; j += stride)
        {
            uint k = (uint)(baseIndex + j) / (uint)stride;
            if (k >= TableSize)
                continue;

            Vertex v = vertexAt(j);
            int o = (int)k * 3;
            _tableP[o] = v.X;
            _tableP[o + 1] = v.Y;
            _tableP[o + 2] = v.Z;
            _tableN[o] = v.NX;
            _tableN[o + 1] = v.NY;
            _tableN[o + 2] = v.NZ;
        }
    }

    /// <summary>Sample entry <paramref name="k"/>: position then normal.</summary>
    public Vertex Entry(int k) => new Vertex
    {
        X = _tableP[k * 3], Y = _tableP[k * 3 + 1], Z = _tableP[k * 3 + 2],
        NX = _tableN[k * 3], NY = _tableN[k * 3 + 1], NZ = _tableN[k * 3 + 2],
    };

    /// <summary>One stock Process call at <paramref name="age"/>, with the dynel at <paramref name="frame"/>.</summary>
    public void Step(float age, in Frame frame)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        int budget = SpawnsPerStep;
        int count = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            if (age < _timer[i])
            {
                _vx[i] = _vx[i] * Drag;
                _vy[i] = _vy[i] * Drag;
                _vz[i] = _vz[i] * Drag;
                _px[i] = _vx[i] * Step01 + _px[i];
                _py[i] = _vy[i] * Step01 + _py[i];
                _pz[i] = _vz[i] * Step01 + _pz[i];

                ref StarsCase3.Sprite s = ref _sprites[i];
                s.X = _px[i];
                s.Y = _py[i];
                s.Z = _pz[i];
                float t = StarsCase3.LifeFraction(_timer[i], age, _life);
                s.Size = Size(t, _size);
                s.Argb = StockColorRamp.Eval(_start, _end, t);
                s.Frame = 0;
                s.Visible = true;
                s.Serial = _slotSerial[i];
                count++;
                continue;
            }

            if (budget == 0 || Terminating)
            {
                _sprites[i].Visible = false;
                continue;
            }

            int o = _next * 3;
            Rotate(frame, _tableP[o] * _scale, _tableP[o + 1] * _scale, _tableP[o + 2] * _scale,
                out float rx, out float ry, out float rz);
            _px[i] = rx + frame.Px;
            _py[i] = ry + frame.Py;
            _pz[i] = rz + frame.Pz;
            Rotate(frame, _tableN[o] * _speed, _tableN[o + 1] * _speed, _tableN[o + 2] * _speed,
                out _vx[i], out _vy[i], out _vz[i]);

            _next++;
            if (_next >= TableSize)
                _next = 0;

            _timer[i] = _life + age;
            _slotSerial[i] = ++_serial;
            count++;
            budget--;
            // As in case 3, the spawn call leaves the sprite record as it was.
        }

        LastCount = count;
    }

    static void Rotate(in Frame f, float x, float y, float z, out float rx, out float ry, out float rz)
    {
        rx = f.Xx * x + f.Yx * y + f.Zx * z;
        ry = f.Xy * x + f.Yy * y + f.Zy * z;
        rz = f.Xz * x + f.Yz * y + f.Zz * z;
    }

    /// <summary><c>100fa0aa..100fa0b9</c>: (1.1 - t) * field 28.</summary>
    public static float Size(float t, float size) => (float)((SizeTop - t) * size);

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
