using System;

/// <summary>
/// Stock <c>_GfxControlGroundImpact_c</c> (type 5000, 0x1388; vftable <c>Gamecode 1016d03c</c>, object
/// 0x38, ctor <c>100e1593</c>, loader <c>100e1490</c>, build <c>100e1536</c>, Process <c>100e1415</c>)
/// and the <c>GfxVisualForceSword_t</c> it drives. The visual's vftable is in Gamecode
/// (<c>1016cfb4</c>) but its bodies are DisplaySystem exports: <c>InitMesh 10015730</c>,
/// <c>Render 10015f21</c>, <c>Process 100156e9</c>, <c>SetSize 100156c9</c>, <c>SetColor 1001564b</c>.
///
/// It is reached from a **second** factory: <c>100d0656</c> returns NULL for 5000, but
/// <c>100d05c1</c> matches 0x1388. The control is a shell — its loader reads fields 0-3 and
/// <c>fstp st(0)</c>s every one, so **the record is dead data** — and it only hands the locator over
/// as the visual's connector and then attaches and detaches it.
///
/// The visual is a blue blade that jitters: a lattice of <b>9 rows x N columns</b> where N is
/// <c>rand() % 10 + 5</c>, chosen once. Rows 0-3 are the four corners of a square cross-section swept
/// from the connector down its own -z for <c>size</c> metres; rows 4-8 are a five-link trail of that
/// cross-section's centre, each link lagging one frame behind the one before it.
///
/// Two different index layouts share the one buffer, which is easy to misread: the blade's corners are
/// <c>col * 4 + corner</c>, and the trail's rows are <c>row * N + col</c>.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class GroundImpactSim
{
    /// <summary>The blade's cross-section is a square of this half-extent (10015ff5).</summary>
    public const float Corner = 0.05f;

    /// <summary>Each column is nudged by up to this much on x and y (10016016).</summary>
    public const float Jitter = 0.005f;

    /// <summary>+0x2e8: rows 4..8, five links of lag (100157ad).</summary>
    public const int TrailRows = 5;

    /// <summary>Rows 0..3 are the cross-section's corners; 9 rows in all.</summary>
    public const int BladeRows = 4;
    public const int Rows = BladeRows + TrailRows;

    /// <summary>The blade's colour, the same on every vertex (10015922: B 0xff, G 0x9b, R 0x32).</summary>
    public const uint Rgb = 0x3299ffu;

    /// <summary>The alpha flicker is <c>rand() % 200 + 0x37</c> (100161cc).</summary>
    public const int FlickerBase = 0x37;
    public const int FlickerRange = 200;

    public readonly int Columns;
    public readonly int VertexCount;

    /// <summary>The visual's <c>+0x2f4</c>; the reset at 100e13ee leaves it at 1.</summary>
    public float Size { get; set; } = 1f;

    /// <summary>x, y, z per vertex, in the connector's own frame.</summary>
    public readonly float[] Positions;

    /// <summary>u, v per vertex — set once by InitMesh and never touched again.</summary>
    public readonly float[] Uvs;

    public readonly uint[] Colours;

    /// <summary>An indexed triangle list: <c>36 * (N - 1)</c> indices (10015a6d).</summary>
    public readonly int[] Indices;

    readonly Func<double> _random;

    /// <summary>Vertex index of the blade's corner <paramref name="corner"/> in column <paramref name="col"/>.</summary>
    public int Blade(int col, int corner) => col * BladeRows + corner;

    /// <summary>Vertex index of trail row <paramref name="row"/> (4..8) in column <paramref name="col"/>.</summary>
    public int Trail(int row, int col) => row * Columns + col;

    public GroundImpactSim(Func<double> random)
    {
        _random = random ?? (() => 0.5);

        // 1001579d: the blade's length in columns is fixed once, at construction.
        Columns = (int)(_random() * 32768.0) % 10 + 5;
        VertexCount = Columns * Rows;
        Positions = new float[VertexCount * 3];
        Uvs = new float[VertexCount * 2];
        Colours = new uint[VertexCount];
        Indices = new int[36 * Math.Max(0, Columns - 1)];

        InitMesh();
    }

    /// <summary>10015730's tail: the UVs, the colours and the index list, all fixed for the life of it.</summary>
    void InitMesh()
    {
        // 1001582e: every column's corners are u = 0, 1, 0, 1 and v = 0.65 — there is no run along
        // the blade at all.
        for (int col = 0; col < Columns; col++)
        {
            for (int c = 0; c < BladeRows; c++)
            {
                int v = Blade(col, c);
                Uvs[v * 2] = (c & 1) == 0 ? 0f : 1f;
                Uvs[v * 2 + 1] = 0.65f;
            }
        }
        // 100158b4: the trail rows sit on one texel.
        for (int row = BladeRows; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int v = Trail(row, col);
                Uvs[v * 2] = 0.5f;
                Uvs[v * 2 + 1] = 0.5f;
            }
        }

        // 10015984 / 100159c7: one colour throughout; only the tip column starts transparent.
        for (int v = 0; v < VertexCount; v++)
            Colours[v] = 0xff000000u | Rgb;
        for (int c = 0; c < BladeRows; c++)
            Colours[Blade(Columns - 1, c)] = Rgb;

        BuildIndices();
    }

    /// <summary>
    /// 10015a9f and 10015b5b: the blade's two facing quads per column pair, then the trail ribbon's
    /// one quad per link per column pair — 12 + 24 = 36 indices a pair.
    /// </summary>
    void BuildIndices()
    {
        int n = 0;
        for (int col = 0; col + 1 < Columns; col++)
        {
            int c = col * BladeRows;
            // Corners 0-1 make one blade and 2-3 the other, so the sword reads as a cross.
            Indices[n++] = c; Indices[n++] = c + 1; Indices[n++] = c + 4;
            Indices[n++] = c + 1; Indices[n++] = c + 5; Indices[n++] = c + 4;
            Indices[n++] = c + 2; Indices[n++] = c + 3; Indices[n++] = c + 6;
            Indices[n++] = c + 3; Indices[n++] = c + 7; Indices[n++] = c + 6;
        }
        for (int t = 0; t + 1 < TrailRows; t++)
        {
            for (int col = 0; col + 1 < Columns; col++)
            {
                int a = Trail(BladeRows + t, col);
                int b = Trail(BladeRows + t + 1, col);
                Indices[n++] = a; Indices[n++] = b; Indices[n++] = a + 1;
                Indices[n++] = b; Indices[n++] = a + 1; Indices[n++] = b + 1;
            }
        }
    }

    /// <summary>
    /// One Render (<c>10015f21</c>). Everything below the blade is rebuilt from scratch each call, so
    /// the jitter and the flicker are new every frame; only the trail carries state.
    /// </summary>
    public void Step()
    {
        if (Columns <= 0)
            return;

        // 10015f86: the blade runs from the connector down its own -z, in Columns - 1 even steps.
        float stepZ = Columns > 1 ? -Size / (Columns - 1) : 0f;
        // 10015fca: the taper runs 0 -> 2 along the blade while its partner runs 2 -> 0.
        float taperStep = 2f / Columns;
        float nearTaper = 0f, farTaper = 2f;
        float baseZ = 0f;

        for (int col = 0; col < Columns; col++)
        {
            // 10016016: a fresh nudge on x and y, scaled by both tapers so the middle wanders most.
            float jx = (float)((_random() * 2000.0 % 2000.0) / 1000.0 - 1.0) * Jitter;
            float jy = (float)((_random() * 2000.0 % 2000.0) / 1000.0 - 1.0) * Jitter;
            float scale = nearTaper * farTaper;
            float ox = jx * scale, oy = jy * scale;

            for (int c = 0; c < BladeRows; c++)
            {
                // 10015ff5: corner 0 is (+,+), 1 is (-,-), 2 is (+,-) and 3 is (-,+), which is what
                // makes the two quads cross.
                float cx = c == 0 || c == 2 ? Corner : -Corner;
                float cy = c == 0 || c == 3 ? Corner : -Corner;
                int v = Blade(col, c) * 3;
                Positions[v] = cx + ox;
                Positions[v + 1] = cy + oy;
                Positions[v + 2] = baseZ;
            }

            // 100161a0: the ends keep the alpha InitMesh gave them; only the middle flickers.
            if (col != 0 && col != Columns - 1)
            {
                int max = 0;
                for (int c = 0; c < BladeRows; c++)
                {
                    int a = (int)(_random() * 32768.0) % FlickerRange + FlickerBase;
                    Colours[Blade(col, c)] = (uint)a << 24 | Rgb;
                    if (a > max)
                        max = a;
                }
                // 1001626f: each trail link takes the brightest corner, halved again per link.
                for (int t = 0; t < TrailRows; t++)
                    Colours[Trail(BladeRows + t, col)] = (uint)(max >> (t + 1)) << 24 | Rgb;
            }

            ShiftTrail(col);

            baseZ += stepZ;
            nearTaper += taperStep;
            farTaper -= taperStep;
        }
    }

    /// <summary>
    /// 100162ab: the trail is walked from the far link back, so each one inherits what its neighbour
    /// held last frame. Link 0 takes the cross-section's centre; a link whose source has never been
    /// written (still exactly zero, <c>10007cc2</c>) falls back to that centre too.
    /// </summary>
    void ShiftTrail(int col)
    {
        float cx = 0f, cy = 0f, cz = 0f;
        for (int c = 0; c < BladeRows; c++)
        {
            int v = Blade(col, c) * 3;
            cx += Positions[v];
            cy += Positions[v + 1];
            cz += Positions[v + 2];
        }
        cx *= 0.25f; cy *= 0.25f; cz *= 0.25f;

        for (int t = TrailRows - 1; t >= 0; t--)
        {
            int dst = Trail(BladeRows + t, col) * 3;
            if (t == 0)
            {
                Positions[dst] = cx;
                Positions[dst + 1] = cy;
                Positions[dst + 2] = cz;
                continue;
            }

            int src = Trail(BladeRows + t - 1, col) * 3;
            bool empty = Positions[src] == 0f && Positions[src + 1] == 0f && Positions[src + 2] == 0f;
            if (empty)
            {
                Positions[dst] = cx;
                Positions[dst + 1] = cy;
                Positions[dst + 2] = cz;
            }
            else
            {
                Positions[dst] = Positions[src];
                Positions[dst + 1] = Positions[src + 1];
                Positions[dst + 2] = Positions[src + 2];
            }
        }
    }

    /// <summary>1001564b: SetColor rewrites the rgb of every vertex and leaves the alphas alone.</summary>
    public void SetColour(float r, float g, float b)
    {
        uint rgb = (uint)Clamp(r) << 16 | (uint)Clamp(g) << 8 | (uint)Clamp(b);
        for (int v = 0; v < VertexCount; v++)
            Colours[v] = Colours[v] & 0xff000000u | rgb;
    }

    static int Clamp(float channel)
    {
        int b = (int)(channel * 255f);
        return b < 0 ? 0 : b > 255 ? 255 : b;
    }
}
