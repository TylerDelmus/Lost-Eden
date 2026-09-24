using System;
using System.Collections.Generic;

/// <summary>
/// Stock <c>_GfxControlVulcanRocks_t</c> (typeCode 1029, 0x405; vftable <c>Gamecode 1016e274</c>, loader
/// <c>10103448</c>, init <c>10103595</c>, dynel ctor <c>10103a18</c>, Process <c>10103bc1</c>) with its
/// DisplaySystem <c>GfxVisualRockList</c> (ctor <c>1001c5d0</c>, GetNew <c>1001c4f9</c>, ProcessRocks
/// <c>1001c435</c>): rocks thrown up from the locator that fall, bounce on the ground and settle, e.g.
/// 45060, the hit of 157988 Fiery Breath.
///
/// Fields: 0 flags, 8 duration (the base's), 10 speed, 11/12 elevation low/high (radians), 13-16 colour
/// A,R,G,B (packed, set by slot 13, never drawn), 20 the list's size, 21 throws per second, 22 model count
/// n, 23.. the n models (slots of <c>VisualEnvFX_t</c>'s model table), then one more: above 0, settled
/// rocks don't count and the rocks are deleted with the list. Fields 9 and 17-19 are loaded and unused.
/// Flag 0x400 keeps it alive when the locator is lost.
///
/// Per call (age, dt):
/// <list type="bullet">
/// <item>Throws: while the throw counter &lt;= field 21 · age it goes up by 1 and one of the n models is
/// picked (<c>_ftol(r (n - 0.0001))</c>). The list gives a rock when it has room (min(field 20, 128)) and
/// the model has a mesh loaded (<c>GfxVisualRockHandler</c>, 256 rocks in all); else nothing more is drawn
/// for that throw. A rock starts at the locator, at speed field 10 along
/// X cos(t) cos(p) + Y sin(p) + Z sin(t) cos(p) of the locator's axes (t = r 2pi, p between fields 11 and
/// 12), spinning about a random unit axis from angle r 2pi - pi at (r 2pi - pi) 8 rad/s.</item>
/// <item>Each rock: v.y -= 9.8 dt, p += v dt. Under the ground it is put back on it and v is mirrored
/// in the ground's normal and halved. Up to four such bounces each give a new random spin while the speed
/// stays at 0.1 or more; after that, or slower, the rock stops (v = 0, spin 0) and, without the last field,
/// the settle counter goes up. A stopped rock sinks again under gravity the next call, so it adds one
/// every call it rests. Then angle += spin dt.</item>
/// <item>Ready once the settle counter reaches <c>_ftol(field 20)</c>.</item>
/// </list>
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class VulcanRocksSim
{
    public const int FlagKeepWithoutLocator = 0x400;

    /// <summary>1001c4a6: the list never holds more than this.</summary>
    public const int ListLimit = 128;

    // 1016d1f8 / 1015f810 / 101585c8 / 10169110 / 1015ef38.
    const double Gravity = 9.800000190734863;
    const double TwoPi = 6.2831854820251465;
    const double Pi = 3.1415927410125732;
    const double PickMargin = 0.0001;
    const double StopSpeed = 0.10000000149011612;

    public sealed class Rock
    {
        public float X, Y, Z;
        public float VX, VY, VZ;
        // 1001c3ee: a new rock turns about y.
        public float AxisX, AxisY = 1f, AxisZ;
        public float Angle, Spin;
        public int Bounces;
        public int Model;
    }

    /// <summary>The locator this call (<c>1010640a</c> and the axis accessors <c>10106439</c> / <c>1010646a</c> / <c>1010649b</c>).</summary>
    public struct Frame
    {
        public float X, Y, Z;
        public float XX, XY, XZ;
        public float YX, YY, YZ;
        public float ZX, ZY, ZZ;
    }

    /// <summary>
    /// The playfield's ground under a point (<c>100ada15</c>): its height and normal, false when there is none.
    /// </summary>
    public delegate bool GroundProbe(float x, float y, float z, out float height, out float nx, out float ny, out float nz);

    readonly Func<float> _rand;
    readonly Func<int, bool> _claim;
    readonly GroundProbe _ground;
    readonly float _speed, _low, _high, _count, _rate;
    readonly int[] _models;
    readonly List<Rock> _rocks = new List<Rock>();
    float _thrown;

    public int Flags { get; }
    public float Duration { get; }
    public int Capacity { get; }
    public int[] Models => _models;
    /// <summary>The field after the models (+0x90): settled rocks don't count, and the list deletes its rocks.</summary>
    public bool DeleteRocks { get; }
    public uint Argb { get; set; }
    public IReadOnlyList<Rock> Rocks => _rocks;
    /// <summary>+0x84: settle events, one per rock per call it rests.</summary>
    public int Settled { get; private set; }
    public float Thrown => _thrown;

    /// <param name="rand">Stock's shared uniform source in [0, 1) (<c>1013dec9</c>).</param>
    /// <param name="claim">
    /// Whether a rock of this model can be had: its slot in the model table holds a mesh and the
    /// handler has room. Takes the rock when it answers true.
    /// </param>
    public VulcanRocksSim(float[] fields, Func<float> rand, Func<int, bool> claim, GroundProbe ground)
    {
        _rand = rand;
        _claim = claim;
        _ground = ground;
        Flags = Int(fields, 0);
        Duration = F(fields, 8);
        _speed = F(fields, 10);
        _low = F(fields, 11);
        _high = F(fields, 12);
        _count = F(fields, 20);
        _rate = F(fields, 21);
        int n = Math.Max(0, Int(fields, 22));
        _models = new int[n];
        for (int i = 0; i < n; i++)
            _models[i] = Int(fields, 23 + i);
        DeleteRocks = Int(fields, 23 + n) > 0;
        Capacity = Math.Min(Ftol(_count), ListLimit);

        // 10103595: fields 13-16 packed as Tracer3 packs them, shifted in unmasked.
        Argb = (uint)((((Channel(F(fields, 13)) << 8) | Channel(F(fields, 14))) << 8 | Channel(F(fields, 15))) << 8
            | Channel(F(fields, 16)));
    }

    /// <summary>One Process call (<c>10103c10</c>..<c>101040e2</c>). False once the rocks have settled.</summary>
    public bool Step(float age, float dt, in Frame frame)
    {
        float target = (float)((double)_rate * age);
        while (_thrown <= target)
        {
            _thrown = (float)(_thrown + 1.0);
            if (_models.Length == 0)
                continue;
            int pick = Ftol(_rand() * (_models.Length - PickMargin));
            if (pick < 0 || pick >= _models.Length)
                continue;
            if (_rocks.Count >= Capacity || !_claim(_models[pick]))
                continue;
            _rocks.Add(Throw(_models[pick], frame));
        }

        for (int i = 0; i < _rocks.Count; i++)
            Move(_rocks[i], dt);

        return Settled < Ftol(_count);
    }

    Rock Throw(int model, in Frame f)
    {
        var rock = new Rock { Model = model, X = f.X, Y = f.Y, Z = f.Z };

        float t = (float)(_rand() * TwoPi + 0.0);
        float p = (float)(_rand() * ((double)_high - _low) + _low);
        float cp = (float)Math.Cos(p);
        float a = (float)((float)Math.Sin(t) * (double)cp);
        float b = (float)Math.Sin(p);
        float c = (float)((float)Math.Cos(t) * (double)cp);

        // (X c + Y b) + Z a, each step a float vector, then times the speed.
        float sx = (float)(f.XX * c) + (float)(f.YX * b);
        float sy = (float)(f.XY * c) + (float)(f.YY * b);
        float sz = (float)(f.XZ * c) + (float)(f.YZ * b);
        sx = (float)(sx + (float)(f.ZX * a));
        sy = (float)(sy + (float)(f.ZY * a));
        sz = (float)(sz + (float)(f.ZZ * a));
        rock.VX = (float)(sx * (double)_speed);
        rock.VY = (float)(sy * (double)_speed);
        rock.VZ = (float)(sz * (double)_speed);

        NewSpin(rock);
        rock.Bounces = 0;
        return rock;
    }

    /// <summary>A random unit axis (up when all three draws are 0), angle r 2pi - pi, spin (r 2pi - pi) 8.</summary>
    void NewSpin(Rock rock)
    {
        float x = (float)(_rand() * 2.0 - 1.0);
        float y = (float)(_rand() * 2.0 - 1.0);
        float z = (float)(_rand() * 2.0 - 1.0);
        if (x == 0f && y == 0f && z == 0f)
        {
            rock.AxisX = 0f;
            rock.AxisY = 1f;
            rock.AxisZ = 0f;
        }
        else
        {
            // 100439aa(1): times 1 / |v| (1003e0fd).
            float length = (float)Math.Sqrt((float)((double)y * y + (double)x * x + (double)z * z));
            float s = (float)(1.0 / length);
            rock.AxisX = (float)(x * (double)s);
            rock.AxisY = (float)(y * (double)s);
            rock.AxisZ = (float)(z * (double)s);
        }

        rock.Angle = (float)(_rand() * TwoPi - Pi);
        rock.Spin = (float)((_rand() * TwoPi - Pi) * 8.0);
    }

    void Move(Rock rock, float dt)
    {
        rock.VY = (float)(rock.VY - dt * Gravity);
        rock.X = (float)(rock.X + (float)(rock.VX * (double)dt));
        rock.Y = (float)((float)(rock.VY * (double)dt) + rock.Y);
        rock.Z = (float)((float)(rock.VZ * (double)dt) + rock.Z);

        float nx = 0f, ny = 0f, nz = 0f;
        float y = rock.Y;
        if (_ground != null && _ground(rock.X, rock.Y, rock.Z, out float height, out nx, out ny, out nz)
            && !float.IsNaN(height) && y < height)
            y = height;

        // 10103ef8: only a rock that was under the ground is moved and bounced.
        if (y > rock.Y)
        {
            rock.Y = y;
            float d = (float)((double)rock.VY * ny + (double)rock.VX * nx + (double)rock.VZ * nz);
            float twice = (float)(d + (double)d);
            rock.VX = (float)(rock.VX - (float)(nx * (double)twice));
            rock.VY = (float)(rock.VY - (float)(ny * (double)twice));
            rock.VZ = (float)(rock.VZ - (float)(nz * (double)twice));
            rock.VX = (float)(rock.VX * 0.5);
            rock.VY = (float)(rock.VY * 0.5);
            rock.VZ = (float)(rock.VZ * 0.5);

            bool stop = rock.Bounces > 3;
            if (!stop)
            {
                float squares = (float)((double)rock.VY * rock.VY + (double)rock.VX * rock.VX + (double)rock.VZ * rock.VZ);
                stop = (float)Math.Sqrt(squares) < StopSpeed;
            }

            if (stop)
            {
                rock.VX = rock.VY = rock.VZ = 0f;
                rock.Spin = 0f;
                if (!DeleteRocks)
                    Settled++;
            }
            else
            {
                NewSpin(rock);
                rock.Bounces++;
            }
        }

        rock.Angle = (float)(rock.Spin * (double)dt + rock.Angle);
    }

    /// <summary>fistp(c 255 - 0.49999), round to even.</summary>
    static int Channel(float c) => (int)Math.Round((float)(c * 255.0) - 0.49999, MidpointRounding.ToEven);

    static int Ftol(double v) => double.IsNaN(v) ? int.MinValue : (int)v;

    static float F(float[] f, int i) => f != null && i >= 0 && i < f.Length ? f[i] : 0f;

    static int Int(float[] f, int i) => f != null && i >= 0 && i < f.Length ? GfxBits.Of(f, i) : 0;
}
