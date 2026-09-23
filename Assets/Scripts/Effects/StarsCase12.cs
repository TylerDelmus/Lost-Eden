using System;

/// <summary>
/// Stock <c>_GfxControlStars_t</c> starType 12 (<c>Gamecode 100f9a7e</c>..<c>100f9c36</c>): short
/// streaks of four sparks that fly out from the locator together and shrink as they go, each streak in
/// one of eight flat colours. Its only record is 14100, the hit of 257089 Alien Cocoon and its kin.
///
/// Fields (loader <c>FUN_100f72a9</c>): 11 the size a spark ends on (+0x167c), 26 duration,
/// 28 the spark's life in seconds, 29 how far it reaches (+0x16d8), 31 how much bigger it starts,
/// in hundredths (+0x16e0).
///
/// <b>The life is field 28, not field 30.</b> Every call begins by copying +0x16d4 over +0x16cc
/// (<c>100f9a8a</c>), so this type overrides the shared "life in milliseconds" that the lazy init put
/// there and reads field 28 as whole seconds instead. Its lazy init is the shared one
/// (<c>100f81e0</c> → <c>100f80a6</c>), which fills all 128 death times with -100 so everything starts
/// expired.
///
/// Per call, two streaks may be started (<b>the budget is the literal 2</b> at <c>100f9ac2</c>, not a
/// field), and only on a slot whose index is a multiple of four (<c>100f9b9d</c>):
/// <list type="bullet">
/// <item><b>Spawn</b>: one random unit vector (<c>100d3005</c>) is written to all four slots of the
/// group, and slot i + k is given the death time <c>age + life - k · 0.1 · life</c>, so the four sparks
/// trail each other by a tenth of a life along the same line. The slot that triggers the spawn was
/// already hidden by <c>100f9ba1</c>, so it sits out this call while its three followers — whose death
/// times the loop now reads as being in the future — light up at once.</item>
/// <item><b>Alive</b>: u = (death - age) / life falls 1 to 0, t = 1 - u. The spark sits at
/// <c>locator + dir · field 29 · t</c> and its size is <c>field 11 + u · field 31 / 100</c>, so it
/// starts big and shrinks to field 11 as it reaches the end of its travel. Its colour is a flat entry
/// from the palette at <c>0x102c5f48</c> chosen by <c>(i &gt;&gt; 2) &amp; 7</c> — one colour per
/// streak, not a ramp — and its frame is 0.</item>
/// </list>
/// On expiry it takes the drain path (its <c>0x100fc374</c> entry is 0): 5 seconds more, spawning
/// stops, and the shared tail (<c>100f884e</c>) readies it once nothing is alive.
///
/// No Unity dependency so the recovered maths can be asserted from plain unit tests.
/// </summary>
public sealed class StarsCase12 : IStarsStockCase
{
    public const int SlotCount = StarsCase3.SlotCount;

    /// <summary>Sparks per streak, and the slot stride a spawn writes (<c>100f9c0e</c>).</summary>
    public const int GroupSize = 4;

    /// <summary><c>100f9ac2</c>: streaks started per call, a literal rather than a field.</summary>
    public const int GroupsPerCall = 2;

    /// <summary><c>100f9bf5</c>: how far apart in life the four sparks of a streak are.</summary>
    public const float GroupStagger = 0.10000000149011612f;

    /// <summary><c>100f9ab6</c>: field 31 is in hundredths.</summary>
    public const float GainScale = 0.009999999776482582f;

    /// <summary><c>100f81e0</c>: every death time starts here, so all slots begin expired.</summary>
    public const float DeadTime = -100f;

    /// <summary>The flat colours at <c>0x102c5f48</c>, one per streak.</summary>
    public static readonly uint[] Palette =
    {
        0xffffc0c0, 0xffffff00, 0xffff00ff, 0xff00ffff,
        0xff0000ff, 0xff00ff00, 0xffff0000, 0xffc0ffc0,
    };

    readonly float _sizeEnd;
    readonly float _life;
    readonly float _reach;
    readonly float _gain;
    readonly Func<int> _rand;

    readonly float[] _death = new float[SlotCount];
    readonly float[] _dx = new float[SlotCount];
    readonly float[] _dy = new float[SlotCount];
    readonly float[] _dz = new float[SlotCount];

    readonly int[] _slotSerial = new int[SlotCount];
    readonly StarsCase3.Sprite[] _sprites = new StarsCase3.Sprite[SlotCount];
    readonly StarsCase3.Sprite[] _previous = new StarsCase3.Sprite[SlotCount];

    public bool Initialised { get; private set; }
    public bool Terminating { get; set; }

    /// <summary>The shared tail <c>100f884e</c>: terminating with nothing alive.</summary>
    public bool Drained { get; private set; }

    public StarsCase3.Sprite[] Sprites => _sprites;

    /// <summary>Stock +0x16cc, which this type takes from field 28.</summary>
    public float Life => _life;

    /// <summary>Slots that were alive or spawned on the last step.</summary>
    public int LiveCount { get; private set; }

    /// <param name="sizeEnd">Field 11.</param>
    /// <param name="lifeSeconds">Field 28.</param>
    /// <param name="reach">Field 29.</param>
    /// <param name="gainHundredths">Field 31.</param>
    public StarsCase12(float sizeEnd, float lifeSeconds, float reach, int gainHundredths, Func<int> rand)
    {
        _sizeEnd = sizeEnd;
        _life = lifeSeconds;
        _reach = reach;
        _gain = (float)(gainHundredths * (double)GainScale);
        _rand = rand ?? throw new ArgumentNullException(nameof(rand));
    }

    /// <summary>One stock Process call; <paramref name="duration"/> is unused here, as in stock.</summary>
    public void Step(float age, float duration, float ox, float oy, float oz)
    {
        Array.Copy(_sprites, _previous, SlotCount);

        if (!Initialised)
        {
            Initialised = true;
            for (int i = 0; i < SlotCount; i++)
                _death[i] = DeadTime;
        }

        int budget = GroupsPerCall;
        int live = 0;

        for (int i = 0; i < SlotCount; i++)
        {
            ref StarsCase3.Sprite s = ref _sprites[i];

            if (age < _death[i])
            {
                // 100f9ae6: u is what is left of the spark's life, so it shrinks as it flies out.
                float u = (float)((_death[i] - (double)age) / _life);
                float t = (float)(1.0 - u);
                float reach = (float)(_reach * (double)t);

                s.X = (float)(ox + (double)(float)(_dx[i] * (double)reach));
                s.Y = (float)(oy + (double)(float)(_dy[i] * (double)reach));
                s.Z = (float)(oz + (double)(float)(_dz[i] * (double)reach));
                s.Size = (float)(_sizeEnd + (double)(float)(u * (double)_gain));
                s.Argb = Palette[(i >> 2) & 7];
                s.Frame = 0;
                s.Visible = true;
                s.Serial = _slotSerial[i];
                live++;
                continue;
            }

            // 100f9ba1: the slot is hidden before anything else is decided.
            s.Visible = false;

            if ((i & (GroupSize - 1)) != 0 || budget <= 0 || Terminating)
                continue;

            // 100f9bbd: one direction for the whole streak.
            ElectraSim.RandomUnitVector(_rand, out float ux, out float uy, out float uz);
            for (int k = 0; k < GroupSize; k++)
            {
                int slot = i + k;
                _dx[slot] = ux;
                _dy[slot] = uy;
                _dz[slot] = uz;
                // 100f9be6: each follower dies a tenth of a life earlier, so it is that far behind.
                _death[slot] = (float)(age + (double)_life
                    - (double)(float)((float)(k * (double)GroupStagger) * (double)_life));
                _slotSerial[slot]++;
            }
            budget--;
            live++;
        }

        LiveCount = live;
        Drained = Terminating && live == 0;
    }

    /// <summary>Port-only; see <see cref="StarsCase3.Blend"/>.</summary>
    public StarsCase3.Sprite Blend(int i, float t) => StarsCase3.Blend(_previous[i], _sprites[i], t);
}
